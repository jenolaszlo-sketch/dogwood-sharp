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
//! the bridge's exported C ABI against the R1 contract. Any difference
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
                let value = arguments.get(index).ok_or("--library requires a path")?;
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
    serde_json::from_str(&text)
        .map_err(|error| format!("invalid fixture {}: {error}", path.display()))
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
        if derived != case.expect {
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
                    "  {}: parsed={} lowered={} rules={} selfContainedCedar={} upstreamValidationPassed={} wrapperCollision={}",
                    case.id,
                    derived.parsed,
                    derived.lowered,
                    derived.rule_count,
                    derived.self_contained_cedar,
                    derived.validation_passed,
                    case.wrapper_collision.is_some()
                );
            }
            Err(problems) => {
                for problem in problems {
                    failures.push(format!("{}: {problem}", case.id));
                }
            }
        }
    }

    println!(
        "DIFFERENTIAL cases={} failures={}",
        manifest.cases.len(),
        failures.len()
    );
    for failure in &failures {
        println!("FAIL {failure}");
    }
    println!(
        "RESULT {}",
        if failures.is_empty() {
            "PASS"
        } else {
            "FAILED"
        }
    );
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
    for operation in [OPERATION_VALIDATE, OPERATION_LOWER] {
        match call_case(library, operation, case, manifest) {
            Ok(reply) => problems.extend(
                compare(
                    &reply,
                    derived,
                    case,
                    manifest,
                    operation == OPERATION_LOWER,
                )
                .into_iter()
                .map(|failure| format!("operation {operation}: {failure}")),
            ),
            Err(status) => problems.push(format!(
                "operation {operation} returned ABI status {status}"
            )),
        }
    }
    if problems.is_empty() {
        Ok(())
    } else {
        Err(problems)
    }
}

/// Keep the upstream expectation intact. Project it onto the R1 boundary only
/// after checking the pinned upstream result, including any replaced field.
fn compare(
    reply: &Value,
    derived: &Expect,
    case: &Case,
    manifest: &Manifest,
    export: bool,
) -> Vec<String> {
    let mut problems = Vec::new();
    let service_built = derived
        .stage_error
        .as_ref()
        .is_none_or(|error| error.stage != "service-schema");
    let action_built = derived.parsed
        && dogwood_language::PolicySchema::from_cedarschema_str(case.policy_schema(manifest))
            .is_ok();
    let collision = case.wrapper_collision.as_ref();
    let checked = derived.lowered && collision.is_none();
    let validated = checked && derived.validation_passed;
    let exported = export && validated;
    let mut stage = serde_json::to_value(&derived.stage_error).unwrap();
    if derived.parsed && !action_built {
        stage["stage"] = "action-schema".into();
    }
    if let Some(collision) = collision {
        stage = serde_json::json!({"stage":"collision-check","category":"generated-context-field-collision"});
        let namespace = collision.action_namespace.as_deref().unwrap_or("");
        let field_type = &derived.cedar_schema_json[namespace]["actions"][&collision.action_id]["appliesTo"]
            ["context"]["attributes"][&collision.field]["type"];
        if field_type != "Boolean" {
            problems
                .push("collision control must show the upstream Boolean replacement".to_owned());
        }
    }
    let rules = if validated {
        project_rules(&serde_json::to_value(&derived.rules).unwrap(), export)
    } else {
        serde_json::json!([])
    };
    let mut expected = serde_json::json!({
        "serviceSchemaBuilt": service_built,
        "parsed": derived.parsed,
        "policyCount": derived.policy_count,
        "parseDiagnostics": derived.parse_diagnostics,
        "actionSchemaBuilt": action_built,
        "lowered": derived.lowered,
        "ruleCount": derived.rule_count,
        "selfContainedCedar": derived.self_contained_cedar,
        "collisionCheckPassed": checked,
        "collision": collision,
        "validationPassed": validated,
        "validationErrors": if checked { derived.validation_errors.as_slice() } else { &[] },
        "validationWarnings": if checked { derived.validation_warnings.as_slice() } else { &[] },
        "artifactsExported": exported,
        "stageError": stage,
        "rules": rules,
    });
    if export {
        expected["cedarPolicyText"] = if exported {
            derived.cedar_policy_text.as_str()
        } else {
            ""
        }
        .into();
        expected["cedarSchemaText"] = if exported {
            derived.cedar_schema_text.as_str()
        } else {
            ""
        }
        .into();
        expected["cedarSchemaJson"] = if exported {
            derived.cedar_schema_json.clone()
        } else {
            "".into()
        };
    }

    let Some(actual) = reply.as_object() else {
        return vec![format!(
            "the reply is not a JSON object: {}",
            compact(reply)
        )];
    };
    for (key, want) in expected.as_object().unwrap() {
        let Some(value) = actual.get(key) else {
            problems.push(format!("{key} is missing"));
            continue;
        };
        let value = match key.as_str() {
            "stageError" if !value.is_null() => project_stage(value),
            "parseDiagnostics" | "validationErrors" | "validationWarnings" => {
                project_findings(value)
            }
            "cedarSchemaJson" if exported => project_schema(value),
            _ => value.clone(),
        };
        if &value != want {
            problems.push(format!(
                "{key} is {}, expected {}",
                compact(&value),
                compact(want)
            ));
        }
    }
    for key in actual.keys().filter(|key| expected.get(*key).is_none()) {
        problems.push(format!("unexpected reply field {key}"));
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
