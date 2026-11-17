//! Independent D2 differential runner for DogwoodSharp.
//!
//! ```text
//! d2-diff generate <fixture.json>                  re-derive pinned expectations
//! d2-diff verify <fixture.json> --library <image>  compare upstream, fixture and ABI
//! ```
//!
//! `generate` rewrites the pinned expectations from the pinned upstream crate.
//! `verify` never writes: it re-derives every case from the pinned upstream
//! crate, checks the pinned fixture still matches upstream, and then compares
//! the bridge's exported C ABI against the same expectations. Any difference
//! exits nonzero.

mod abi;
mod oracle;

use std::path::{Path, PathBuf};
use std::process::ExitCode;

use serde_json::Value;

use oracle::{Case, Expect, Manifest};

const FIXTURE_VERSION: u32 = 1;
const DOGWOOD_REVISION: &str = "929f49e7305534ac61ef06cf8e47269c596dc851";
const OPERATION_VALIDATE: u32 = 2;
const OPERATION_LOWER: u32 = 3;
/// Longest rendered value shown in a failure line.
const PREVIEW: usize = 200;

fn main() -> ExitCode {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    if arguments.is_empty() {
        usage();
        return ExitCode::from(2);
    }

    match arguments[0].as_str() {
        "generate" => match arguments.get(1) {
            Some(path) => generate(Path::new(path)),
            None => {
                usage();
                ExitCode::from(2)
            }
        },
        "verify" => match verify_arguments(&arguments) {
            Ok((fixture, library)) => verify(&fixture, &library),
            Err(message) => {
                eprintln!("{message}");
                usage();
                ExitCode::from(2)
            }
        },
        _ => {
            usage();
            ExitCode::from(2)
        }
    }
}

fn usage() {
    eprintln!("usage: d2-diff generate <fixture.json>");
    eprintln!("       d2-diff verify <fixture.json> --library <bridge-image>");
}

fn verify_arguments(arguments: &[String]) -> Result<(PathBuf, PathBuf), String> {
    let mut fixture = None;
    let mut library = None;
    let mut index = 1;
    while index < arguments.len() {
        match arguments[index].as_str() {
            "--library" => {
                index += 1;
                let value = arguments
                    .get(index)
                    .ok_or("--library requires a path")?;
                library = Some(PathBuf::from(value));
            }
            other if !other.starts_with("--") && fixture.is_none() => {
                fixture = Some(PathBuf::from(other))
            }
            other => return Err(format!("unexpected argument {other}")),
        }
        index += 1;
    }
    let fixture = fixture.ok_or("verify requires a fixture path")?;
    let library = library.ok_or("verify requires --library")?;
    Ok((fixture, library))
}

fn read_manifest(path: &Path) -> Result<Manifest, String> {
    let text = std::fs::read_to_string(path)
        .map_err(|error| format!("cannot read {}: {error}", path.display()))?;
    serde_json::from_str(&text).map_err(|error| format!("invalid fixture {}: {error}", path.display()))
}

/// Re-derive every pinned expectation from upstream and rewrite the fixture.
fn generate(path: &Path) -> ExitCode {
    let manifest = match read_manifest(path) {
        Ok(manifest) => manifest,
        Err(error) => {
            eprintln!("{error}");
            return ExitCode::from(2);
        }
    };

    let mut cases = manifest.cases.clone();
    for case in &mut cases {
        case.expect = oracle::derive(case, &manifest);
    }

    let document = serde_json::json!({
        "fixtureVersion": FIXTURE_VERSION,
        "dogwoodRevision": DOGWOOD_REVISION,
        "defaultPolicySchema": manifest.default_policy_schema,
        "cases": cases,
    });
    let text = match serde_json::to_string_pretty(&document) {
        Ok(text) => text + "\n",
        Err(error) => {
            eprintln!("cannot serialize the fixture: {error}");
            return ExitCode::from(2);
        }
    };
    match std::fs::write(path, text) {
        Ok(()) => {
            println!("GENERATED cases={}", cases.len());
            ExitCode::SUCCESS
        }
        Err(error) => {
            eprintln!("cannot write {}: {error}", path.display());
            ExitCode::from(2)
        }
    }
}

/// Compare upstream, the pinned fixture, and the bridge through its C ABI.
fn verify(path: &Path, library_path: &Path) -> ExitCode {
    let manifest = match read_manifest(path) {
        Ok(manifest) => manifest,
        Err(error) => {
            eprintln!("{error}");
            return ExitCode::from(2);
        }
    };
    if manifest.fixture_version != FIXTURE_VERSION {
        eprintln!("unsupported fixture version {}", manifest.fixture_version);
        return ExitCode::from(2);
    }
    if manifest.dogwood_revision != DOGWOOD_REVISION {
        eprintln!("unexpected Dogwood pin {}", manifest.dogwood_revision);
        return ExitCode::from(2);
    }

    let library = match abi::Library::open(&library_path.display().to_string()) {
        Ok(library) => library,
        Err(error) => {
            eprintln!("{error}");
            return ExitCode::from(2);
        }
    };

    let mut failures = Vec::new();
    println!(
        "DIFFERENTIAL cases={} fixture={} library={}",
        manifest.cases.len(),
        path.display(),
        library_path.display()
    );

    for case in &manifest.cases {
        let derived = oracle::derive(case, &manifest);
        if &derived != &case.expect {
            failures.push(format!(
                "{}: the pinned expectation differs from the pinned upstream crate (pinned {}, upstream {})",
                case.id,
                compact_derived(&case.expect),
                compact_derived(&derived)
            ));
            continue;
        }

        match problems(case, &derived, &manifest, &library) {
            Ok(()) => {
                println!(
                    "  {}: parsed={} lowered={} rules={} selfContainedCedar={} validationPassed={}",
                    case.id,
                    derived.parsed,
                    derived.lowered,
                    derived.rule_count,
                    derived.self_contained_cedar,
                    derived.validation_passed
                );
            }
            Err(problems) => {
                for problem in problems {
                    failures.push(format!("{}: {problem}", case.id));
                }
            }
        }
    }

    println!("DIFFERENTIAL cases={} failures={}", manifest.cases.len(), failures.len());
    for failure in &failures {
        println!("FAIL {failure}");
    }
    println!("RESULT {}", if failures.is_empty() { "PASS" } else { "FAILED" });
    if failures.is_empty() {
        ExitCode::SUCCESS
    } else {
        ExitCode::FAILURE
    }
}

fn problems(
    case: &Case,
    derived: &Expect,
    manifest: &Manifest,
    library: &abi::Library,
) -> Result<(), Vec<String>> {
    let mut problems = Vec::new();

    let lower = call_case(library, OPERATION_LOWER, case, manifest);
    let validate = call_case(library, OPERATION_VALIDATE, case, manifest);

    match lower {
        Ok(reply) => problems.extend(compare(&reply, derived, Operation::Lower)),
        Err(status) => problems.push(format!("lower returned ABI status {status}")),
    };

    match validate {
        Ok(reply) => {
            if case.distincter.is_some() {
                problems.push("validate accepted a distincter, which it must reject".to_string());
            } else {
                problems.extend(compare(&reply, derived, Operation::Validate));
            }
        }
        Err(status) => {
            // Validation does not name hoisted fields, so a distincter is a
            // request error there rather than a pipeline stage.
            if !(case.distincter.is_some() && status == 5) {
                problems.push(format!("validate returned ABI status {status}"));
            }
        }
    }

    if problems.is_empty() {
        Ok(())
    } else {
        Err(problems)
    }
}

/// Which operation produced a reply. The lowering reply carries the artifacts
/// and the full rule/source mapping; the validation reply carries findings.
#[derive(Clone, Copy, PartialEq)]
enum Operation {
    Validate,
    Lower,
}

/// Compare one ABI reply against the derived expectation.
///
/// Diagnostic message text is upstream-owned and may change, so only the
/// structural facts (category, location, ids and artifacts) are compared.
fn compare(reply: &Value, derived: &Expect, operation: Operation) -> Vec<String> {
    let object = reply.as_object();
    let mut problems = Vec::new();
    if object.is_none() {
        return vec![format!("the reply is not a JSON object: {}", compact(reply))];
    }
    let object = object.unwrap();

    if let Some(actual) = object.get("parsed") {
        check_bool("parsed", derived.parsed, actual, &mut problems);
    } else {
        problems.push("parsed is missing".to_string());
    }
    if let Some(actual) = object.get("policyCount") {
        check_count("policyCount", derived.policy_count, actual, &mut problems);
    }
    if let Some(actual) = object.get("parseDiagnostics") {
        check_json("parseDiagnostics", &derived.parse_diagnostics, &project_findings(actual), &mut problems);
    }
    if let Some(actual) = object.get("lowered") {
        check_bool("lowered", derived.lowered, actual, &mut problems);
    }
    if let Some(actual) = object.get("ruleCount") {
        check_number("ruleCount", derived.rule_count as u64, actual, &mut problems);
    }
    if let Some(actual) = object.get("selfContainedCedar") {
        check_bool("selfContainedCedar", derived.self_contained_cedar, actual, &mut problems);
    }
    if let Some(actual) = object.get("validationPassed") {
        check_bool("validationPassed", derived.validation_passed, actual, &mut problems);
    }
    if let Some(actual) = object.get("validationErrors") {
        check_json("validationErrors", &derived.validation_errors, &project_findings(actual), &mut problems);
    }
    if let Some(actual) = object.get("validationWarnings") {
        check_json("validationWarnings", &derived.validation_warnings, &project_findings(actual), &mut problems);
    }
    match object.get("stageError") {
        None => problems.push("stageError is missing".to_string()),
        Some(Value::Null) => {
            if let Some(error) = &derived.stage_error {
                problems.push(format!("stageError is null but upstream derives {error:?}"));
            }
        }
        Some(actual) => {
            check_json("stageError", &derived.stage_error, &project_stage(actual), &mut problems)
        }
    }

    if derived.lowered {
        if let Some(actual) = object.get("rules") {
            // The lowering reply carries the full mapping; the validation reply
            // carries the identity mapping only, so project both sides.
            let with_source = operation == Operation::Lower;
            let expected = project_rules(&serde_json::to_value(&derived.rules).unwrap(), with_source);
            check_json("rules", &expected, &project_rules(actual, with_source), &mut problems);
        }
        if let Some(actual) = object.get("cedarPolicyText") {
            check_text("cedarPolicyText", &derived.cedar_policy_text, actual, &mut problems);
        }
        if let Some(actual) = object.get("cedarSchemaJson") {
            check_json("cedarSchemaJson", &derived.cedar_schema_json, &project_schema(actual), &mut problems);
        }
        if let Some(actual) = object.get("cedarSchemaText") {
            check_text("cedarSchemaText", &derived.cedar_schema_text, actual, &mut problems);
        }
    }

    problems
}

/// Reduce a reply's findings array to the structural facts the pins record.
fn project_findings(value: &Value) -> Value {
    let entries = match value.as_array() {
        Some(entries) => entries,
        None => return Value::Null,
    };
    Value::Array(
        entries
            .iter()
            .map(|entry| {
                serde_json::json!({
                    "category": entry.get("category").cloned().unwrap_or(Value::Null),
                    "location": entry.get("location").cloned().unwrap_or(Value::Null),
                })
            })
            .collect(),
    )
}

/// Reduce a reply's rules array to the structural facts the pins record.
fn project_rules(value: &Value, with_source: bool) -> Value {
    let entries = match value.as_array() {
        Some(entries) => entries,
        None => return Value::Null,
    };
    Value::Array(
        entries
            .iter()
            .map(|entry| {
                let mut object = serde_json::Map::new();
                object.insert(
                    "ruleIndex".to_string(),
                    entry.get("ruleIndex").cloned().unwrap_or(Value::Null),
                );
                object.insert(
                    "cedarPolicyId".to_string(),
                    entry.get("cedarPolicyId").cloned().unwrap_or(Value::Null),
                );
                if with_source {
                    object.insert(
                        "sourceText".to_string(),
                        entry.get("sourceText").cloned().unwrap_or(Value::Null),
                    );
                }
                Value::Object(object)
            })
            .collect(),
    )
}

/// Reduce a reply's stage error to the facts the pins record.
fn project_stage(value: &Value) -> Value {
    serde_json::json!({
        "stage": value.get("stage").cloned().unwrap_or(Value::Null),
        "category": value.get("category").cloned().unwrap_or(Value::Null),
    })
}

/// The schema reply is a JSON document carried as a string; parse it so the
/// comparison is structural rather than textual.
fn project_schema(value: &Value) -> Value {
    match value.as_str() {
        Some(text) => serde_json::from_str(text).unwrap_or(Value::Null),
        None => Value::Null,
    }
}

fn check_bool(key: &str, expected: bool, actual: &Value, problems: &mut Vec<String>) {
    if actual.as_bool() != Some(expected) {
        problems.push(format!("{key} is {} but upstream derives {expected}", compact(actual)));
    }
}

fn check_count(key: &str, expected: Option<usize>, actual: &Value, problems: &mut Vec<String>) {
    match (expected, actual) {
        (None, Value::Null) => {}
        (Some(count), Value::Number(number)) if number.as_u64() == Some(count as u64) => {}
        _ => {
            let derived = match expected {
                Some(count) => Value::from(count as u64),
                None => Value::Null,
            };
            problems.push(format!(
                "{key} is {} but upstream derives {}",
                compact(actual),
                compact(&derived)
            ));
        }
    }
}

fn check_number(key: &str, expected: u64, actual: &Value, problems: &mut Vec<String>) {
    if actual.as_u64() != Some(expected) {
        problems.push(format!("{key} is {} but upstream derives {expected}", compact(actual)));
    }
}

fn check_text(key: &str, expected: &str, actual: &Value, problems: &mut Vec<String>) {
    if actual.as_str() != Some(expected) {
        problems.push(format!("{key} is {} but upstream derives \"{expected}\"", compact(actual)));
    }
}

fn check_json(key: &str, expected: &impl serde::Serialize, actual: &Value, problems: &mut Vec<String>) {
    let expected = serde_json::to_value(expected).unwrap_or(Value::Null);
    if actual != &expected {
        problems.push(format!(
            "{key} is {} but upstream derives {}",
            compact(actual),
            compact(&expected)
        ));
    }
}

fn call_case(
    library: &abi::Library,
    operation: u32,
    case: &Case,
    manifest: &Manifest,
) -> Result<Value, u32> {
    let mut request = serde_json::Map::new();
    request.insert("source".to_string(), Value::String(case.source.clone()));
    request.insert(
        "policySchema".to_string(),
        Value::String(case.policy_schema(manifest).to_string()),
    );
    if let Some(text) = &case.event_schema {
        request.insert("eventSchema".to_string(), Value::String(text.clone()));
    }
    if let Some(text) = &case.macros {
        request.insert("macros".to_string(), Value::String(text.clone()));
    }
    if let Some(name) = &case.distincter {
        request.insert("distincter".to_string(), Value::String(name.clone()));
    }
    let body = Value::Object(request).to_string();
    let reply = library.call(operation, body.as_bytes())?;
    serde_json::from_slice(&reply).map_err(|_| u32::MAX)
}

fn compact(value: &Value) -> String {
    let text = serde_json::to_string(value).unwrap();
    if text.chars().count() <= PREVIEW {
        text
    } else {
        format!("{}...", text.chars().take(PREVIEW).collect::<String>())
    }
}

fn compact_derived(expect: &Expect) -> String {
    compact(&serde_json::to_value(expect).unwrap_or(Value::Null))
}