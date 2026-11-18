use super::*;
use dogwood_language::PolicySchema;
use serde_json::{Value, json};

fn call(abi: u32, op: u32, input: &[u8], buffer: &mut Buffer) -> u32 {
    unsafe { dogwoodsharp_call_v1(abi, op, input.as_ptr(), input.len() as u32, buffer) }
}

fn read(buffer: &Buffer) -> Value {
    assert!(!buffer.data.is_null());
    assert!((1..=RESPONSE_LIMIT).contains(&(buffer.len as usize)));
    let bytes = unsafe { slice::from_raw_parts(buffer.data, buffer.len as usize) };
    serde_json::from_slice(bytes).unwrap()
}

fn release(buffer: &mut Buffer) {
    unsafe { dogwoodsharp_free_v1(buffer) };
    assert!(buffer.data.is_null());
    assert_eq!(buffer.len, 0);
}

/// A D2 request body. Every D2 operation reads the same fields.
fn d2_request(source: &str, policy_schema: &str, extra: &str) -> String {
    format!("{{\"source\":{source},\"policySchema\":{policy_schema}{extra}}}")
}

fn d2_body(text: &str) -> String {
    serde_json::to_string(text).unwrap()
}

/// Cedar action-schema text used by the D2 tests.
const SCHEMA_TEXT: &str = "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }";

#[test]
fn d2_operation_gate_rejects_unknown_operations() {
    let mut buffer = Buffer::EMPTY;
    for operation in [OPERATION_LOWER + 1, u32::MAX] {
        assert_eq!(
            call(ABI, operation, b"{}", &mut buffer),
            Status::UnsupportedOperation as u32
        );
        release(&mut buffer);
    }
}

#[test]
fn d2_envelope_rejections_leave_empty_output() {
    let mut buffer = Buffer::EMPTY;
    let request = d2_request(
        &d2_body("permit(principal, action, resource);"),
        &d2_body(SCHEMA_TEXT),
        "",
    );
    let cases: &[(u32, &[u8], Status)] = &[
        // Missing an action schema.
        (
            OPERATION_LOWER,
            b"{\"source\":\"\"}",
            Status::InvalidEnvelope,
        ),
        (
            OPERATION_LOWER,
            b"{\"policySchema\":\"\"}",
            Status::InvalidEnvelope,
        ),
        // Wrong value kinds.
        (
            OPERATION_LOWER,
            br#"{"source":true,"policySchema":"x"}"#,
            Status::InvalidEnvelope,
        ),
        (
            OPERATION_LOWER,
            br#"{"source":"x","policySchema":{}}"#,
            Status::InvalidEnvelope,
        ),
        (
            OPERATION_LOWER,
            br#"{"source":"x","policySchema":"x","macros":5}"#,
            Status::InvalidEnvelope,
        ),
        (
            OPERATION_LOWER,
            br#"{"source":"x","policySchema":"x","eventSchema":[]}"#,
            Status::InvalidEnvelope,
        ),
        // Unknown, duplicate and trailing content.
        (OPERATION_LOWER, b"{}", Status::InvalidEnvelope),
        (
            OPERATION_LOWER,
            br#"{"source":"x","policySchema":"x","source":"y"}"#,
            Status::InvalidEnvelope,
        ),
        (
            OPERATION_LOWER,
            br#"{"source":"x","policySchema":"x","extra":1}"#,
            Status::InvalidEnvelope,
        ),
        (
            OPERATION_LOWER,
            br#"{"source":"x","policySchema":"x"} {}"#,
            Status::InvalidEnvelope,
        ),
        (OPERATION_LOWER, b"[]", Status::InvalidEnvelope),
        (OPERATION_LOWER, b"", Status::InvalidArgument),
    ];
    for &(operation, input, status) in cases {
        assert_eq!(call(ABI, operation, input, &mut buffer), status as u32);
        release(&mut buffer);
    }

    // A well-formed request is accepted.
    assert_eq!(
        call(ABI, OPERATION_LOWER, request.as_bytes(), &mut buffer),
        0
    );
    let reply = read(&buffer);
    assert_eq!(reply["parsed"], true);
    assert_eq!(reply["lowered"], true);
    release(&mut buffer);
}

#[test]
fn d2_request_limits_are_enforced() {
    let mut buffer = Buffer::EMPTY;

    let oversized_source = d2_request(
        &d2_body(&"a".repeat(SOURCE_LIMIT + 1)),
        &d2_body(SCHEMA_TEXT),
        "",
    );
    assert_eq!(
        call(
            ABI,
            OPERATION_LOWER,
            oversized_source.as_bytes(),
            &mut buffer
        ),
        Status::Limit as u32
    );
    release(&mut buffer);

    let oversized_schema = d2_request(
        &d2_body(&"a".repeat(SCHEMA_LIMIT + 1)),
        &d2_body(SCHEMA_TEXT),
        "",
    );
    assert_eq!(
        call(
            ABI,
            OPERATION_LOWER,
            oversized_schema.as_bytes(),
            &mut buffer
        ),
        Status::Limit as u32
    );
    release(&mut buffer);

    let oversized_event_schema = d2_request(
        &d2_body("permit(principal, action, resource);"),
        &d2_body(SCHEMA_TEXT),
        &format!(
            ",\"eventSchema\":{}",
            d2_body(&"a".repeat(SCHEMA_LIMIT + 1))
        ),
    );
    assert_eq!(
        call(
            ABI,
            OPERATION_LOWER,
            oversized_event_schema.as_bytes(),
            &mut buffer
        ),
        Status::Limit as u32
    );
    release(&mut buffer);

    let oversized_distincter = d2_request(
        &d2_body("permit(principal, action, resource);"),
        &d2_body(SCHEMA_TEXT),
        &format!(
            ",\"distincter\":{}",
            d2_body(&"a".repeat(DISTINCTER_LIMIT + 1))
        ),
    );
    assert_eq!(
        call(
            ABI,
            OPERATION_LOWER,
            oversized_distincter.as_bytes(),
            &mut buffer
        ),
        Status::Limit as u32
    );
    release(&mut buffer);

    // The whole-request ceiling still applies.
    let wide = vec![b' '; REQUEST_LIMIT + 1];
    assert_eq!(
        call(ABI, OPERATION_VALIDATE, &wide, &mut buffer),
        Status::Limit as u32
    );
    release(&mut buffer);
}

#[test]
fn d2_validate_and_export_use_the_same_distincter() {
    let mut buffer = Buffer::EMPTY;
    let request = d2_request(
        &d2_body("permit(principal, action == App::Action::\"read\", resource);"),
        &d2_body(SCHEMA_TEXT),
        ",\"distincter\":\"batch\"",
    );
    for operation in [OPERATION_VALIDATE, OPERATION_LOWER] {
        assert_eq!(call(ABI, operation, request.as_bytes(), &mut buffer), 0);
        let reply = read(&buffer);
        assert_eq!(reply["validationPassed"], true);
        assert_eq!(reply["artifactsExported"], operation == OPERATION_LOWER);
        assert_eq!(reply["rules"][0]["cedarPolicyId"], "batch_0");
        release(&mut buffer);
    }
}

#[test]
fn d2_failed_stages_never_look_complete() {
    let mut buffer = Buffer::EMPTY;

    // A parse failure: no lowering, no validation, diagnostics present.
    let request = d2_request(&d2_body("permit("), &d2_body(SCHEMA_TEXT), "");
    assert_eq!(
        call(ABI, OPERATION_VALIDATE, request.as_bytes(), &mut buffer),
        0
    );
    let reply = read(&buffer);
    assert_eq!(reply["parsed"], false);
    assert_eq!(reply["lowered"], false);
    assert_eq!(reply["validationPassed"], false);
    assert_eq!(reply["ruleCount"], 0);
    assert_eq!(reply["policyCount"], serde_json::Value::Null);
    assert_eq!(reply["stageError"], serde_json::Value::Null);
    assert!(
        reply["parseDiagnostics"]
            .as_array()
            .is_some_and(|d| !d.is_empty())
    );
    release(&mut buffer);

    // A lowering failure: succeeded to parse, then stopped with a stage error.
    let request = d2_request(
        &d2_body("permit(principal, action == App::Action::\"read\", resource);"),
        &d2_body("namespace App { entity"),
        "",
    );
    assert_eq!(
        call(ABI, OPERATION_LOWER, request.as_bytes(), &mut buffer),
        0
    );
    let reply = read(&buffer);
    assert_eq!(reply["parsed"], true);
    assert_eq!(reply["lowered"], false);
    assert_eq!(reply["ruleCount"], 0);
    assert_eq!(reply["stageError"]["stage"], "lower");
    assert_eq!(reply["stageError"]["category"], "schema");
    assert_eq!(reply["cedarPolicyText"], "");
    assert_eq!(reply["cedarSchemaJson"], "");
    assert!(reply["rules"].as_array().is_some_and(|r| r.is_empty()));
    release(&mut buffer);

    // A validation failure is still a completed pipeline with findings.
    let request = d2_request(
        &d2_body(
            "permit(principal, action == App::Action::\"read\", resource) when { context.nope > 3 };",
        ),
        &d2_body(SCHEMA_TEXT),
        "",
    );
    assert_eq!(
        call(ABI, OPERATION_VALIDATE, request.as_bytes(), &mut buffer),
        0
    );
    let reply = read(&buffer);
    assert_eq!(reply["parsed"], true);
    assert_eq!(reply["lowered"], true);
    assert_eq!(reply["validationPassed"], false);
    assert_eq!(reply["ruleCount"], 1);
    assert_eq!(reply["stageError"], serde_json::Value::Null);
    assert!(
        reply["validationErrors"]
            .as_array()
            .is_some_and(|e| !e.is_empty())
    );
    release(&mut buffer);
}

#[test]
fn d2_lowering_matches_pinned_upstream() {
    // A small in-suite sample of the D2 differential: the ABI reply must agree
    // with a direct call to the pinned crate. The full fixture differential runs
    // in dogwoodsharp-d2-diff.
    let service = ServiceSchema::defaults();
    let schema_text = "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }";
    let broken_schema_text = "namespace App { entity";

    let cases: &[(&str, &str, bool, usize)] = &[
        ("permit(principal, action, resource);", schema_text, true, 1),
        (
            "permit(principal, action == App::Action::\"read\", resource);",
            schema_text,
            true,
            1,
        ),
        ("permit(", schema_text, false, 0),
        (
            "permit(principal, action == App::Action::\"read\", resource) when { context.nope > 3 };",
            schema_text,
            true,
            1,
        ),
        (
            "permit(principal, action == App::Action::\"read\", resource) when temporal { formerly within 1h App::Action::\"read\"::request { context.count: 3 } };",
            schema_text,
            true,
            1,
        ),
        (
            "permit(principal, action == App::Action::\"read\", resource);",
            broken_schema_text,
            false,
            0,
        ),
    ];

    let mut buffer = Buffer::EMPTY;
    for &(source, policy_schema, parse_ok, rule_count) in cases {
        let request = d2_request(&d2_body(source), &d2_body(policy_schema), "");
        assert_eq!(
            call(ABI, OPERATION_LOWER, request.as_bytes(), &mut buffer),
            0,
            "{source}"
        );
        let reply = read(&buffer);
        release(&mut buffer);

        match ParsedPolicySet::parse(source, &service) {
            Ok(parsed) => {
                match parsed.lower(&PolicySchema::from_cedarschema_str(policy_schema).unwrap()) {
                    Ok(lowered) => {
                        assert_eq!(reply["parsed"], true, "{source}");
                        assert_eq!(reply["lowered"], true, "{source}");
                        assert_eq!(reply["ruleCount"], lowered.rules().count(), "{source}");
                        assert_eq!(reply["ruleCount"], rule_count, "{source}");
                        let valid = dogwood_language::Validator::new()
                            .validate(&lowered)
                            .validation_passed();
                        assert_eq!(reply["validationPassed"], valid, "{source}");
                        assert_eq!(reply["artifactsExported"], valid, "{source}");
                        let expected_text = if valid {
                            lowered.as_cedar().to_string()
                        } else {
                            String::new()
                        };
                        assert_eq!(reply["cedarPolicyText"], expected_text, "{source}");
                        assert_eq!(
                            reply["selfContainedCedar"],
                            lowered.is_self_contained_cedar(),
                            "{source}"
                        );
                    }
                    Err(_) => {
                        assert_eq!(reply["parsed"], true, "{source}");
                        assert_eq!(reply["lowered"], false, "{source}");
                        assert_eq!(reply["ruleCount"], 0, "{source}");
                        assert_eq!(reply["stageError"]["stage"], "lower", "{source}");
                        assert_eq!(reply["stageError"]["category"], "schema", "{source}");
                        assert!(
                            !reply["stageError"]["message"].as_str().unwrap().is_empty(),
                            "{source}"
                        );
                    }
                }
            }
            Err(_) => {
                assert_eq!(reply["parsed"], false, "{source}");
                assert_eq!(reply["lowered"], false, "{source}");
                assert!(!parse_ok, "{source}");
            }
        }
    }
}

#[test]
fn d2_repeated_output_lifecycle() {
    let good = d2_request(&d2_body(""), &d2_body(SCHEMA_TEXT), "");
    let invalid = d2_request(
        &d2_body("permit(principal, action, resource) when { context.unknown > 3 };"),
        &d2_body(SCHEMA_TEXT),
        "",
    );
    for _ in 0..64 {
        let mut good_output = Buffer::EMPTY;
        let mut invalid_output = Buffer::EMPTY;
        assert_eq!(
            call(ABI, OPERATION_LOWER, good.as_bytes(), &mut good_output),
            0
        );
        assert_eq!(
            call(
                ABI,
                OPERATION_LOWER,
                invalid.as_bytes(),
                &mut invalid_output
            ),
            0
        );
        assert_ne!(good_output.data, invalid_output.data);
        assert_eq!(read(&good_output)["artifactsExported"], true);
        release(&mut good_output);
        let rejected = read(&invalid_output);
        assert_eq!(rejected["lowered"], true);
        assert_eq!(rejected["validationPassed"], false);
        assert_eq!(rejected["artifactsExported"], false);
        assert_eq!(rejected["cedarSchemaJson"], "");
        assert!(!rejected["validationErrors"].as_array().unwrap().is_empty());
        release(&mut invalid_output);
        release(&mut invalid_output);
    }
}

#[test]
fn d2_decoded_source_and_encoded_request_limits_are_independent() {
    let source = format!("//{}", "a".repeat(SOURCE_LIMIT - 2));
    let request = d2_request(&d2_body(&source), &d2_body(SCHEMA_TEXT), "");
    let mut buffer = Buffer::EMPTY;
    assert_eq!(
        call(ABI, OPERATION_LOWER, request.as_bytes(), &mut buffer),
        0
    );
    assert_eq!(read(&buffer)["artifactsExported"], true);
    release(&mut buffer);

    let escaped_source = format!("\"\\u002f\\u002f{}\"", "\\u0061".repeat(SOURCE_LIMIT - 2));
    let request = d2_request(&escaped_source, &d2_body(SCHEMA_TEXT), "");
    assert!(request.len() > REQUEST_LIMIT);
    assert_eq!(
        call(ABI, OPERATION_LOWER, request.as_bytes(), &mut buffer),
        Status::Limit as u32
    );
    assert!(buffer.data.is_null());
    assert_eq!(buffer.len, 0);
}

#[test]
fn version_and_repeated_output_lifecycle() {
    assert_eq!(dogwoodsharp_abi_version(), 1);
    let mut buffer = Buffer::EMPTY;
    for _ in 0..128 {
        assert_eq!(call(ABI, 0, b"{}", &mut buffer), 0);
        let version = read(&buffer);
        assert_eq!(version["abiVersion"], ABI);
        assert_eq!(version["target"], env!("DOGWOODSHARP_TARGET"));
        assert_eq!(version["dogwoodFeatures"], json!([]));
        release(&mut buffer);
        release(&mut buffer);
    }
    unsafe { dogwoodsharp_free_v1(ptr::null_mut()) };
}

#[test]
fn invalid_input_leaves_empty_output() {
    let mut buffer = Buffer::EMPTY;
    let cases: &[(u32, u32, &[u8], Status)] = &[
        (99, 0, b"{}", Status::UnsupportedAbi),
        (1, 99, b"{}", Status::UnsupportedOperation),
        (1, 1, b"", Status::InvalidArgument),
        (1, 1, b"\xff", Status::InvalidUtf8),
        (1, 1, b"{", Status::InvalidEnvelope),
        (1, 1, b"[]", Status::InvalidEnvelope),
        (1, 1, b"[\"source\"]", Status::InvalidEnvelope),
        (1, 0, b"[]", Status::InvalidEnvelope),
        (1, 0, br#"{"unexpected":true}"#, Status::InvalidEnvelope),
        (1, 1, b"{}", Status::InvalidEnvelope),
        (1, 1, br#"{"source":null}"#, Status::InvalidEnvelope),
        (
            1,
            1,
            br#"{"source":"","source":"duplicate"}"#,
            Status::InvalidEnvelope,
        ),
        (
            1,
            1,
            br#"{"source":"","unknown":true}"#,
            Status::InvalidEnvelope,
        ),
        (1, 1, br#"{"source":"\ud800"}"#, Status::InvalidEnvelope),
        (1, 1, br#"{"source":""} {}"#, Status::InvalidEnvelope),
    ];
    for &(abi, op, input, status) in cases {
        assert_eq!(call(abi, op, input, &mut buffer), status as u32);
        release(&mut buffer);
    }

    let status = unsafe { dogwoodsharp_call_v1(ABI, 1, ptr::null(), 1, &mut buffer) };
    assert_eq!(status, Status::InvalidArgument as u32);
    release(&mut buffer);
    let status = unsafe { dogwoodsharp_call_v1(ABI, 0, b"{}".as_ptr(), 2, ptr::null_mut()) };
    assert_eq!(status, Status::InvalidArgument as u32);
}

#[test]
fn request_source_and_response_limits() {
    let mut buffer = Buffer::EMPTY;
    let input = vec![b' '; REQUEST_LIMIT + 1];
    assert_eq!(call(ABI, 1, &input, &mut buffer), Status::Limit as u32);
    release(&mut buffer);

    let input = json!({"source": " ".repeat(SOURCE_LIMIT + 1)}).to_string();
    assert_eq!(
        call(ABI, 1, input.as_bytes(), &mut buffer),
        Status::Limit as u32
    );
    release(&mut buffer);

    let input = json!({"source": " ".repeat(SOURCE_LIMIT)}).to_string();
    assert_eq!(call(ABI, 1, input.as_bytes(), &mut buffer), 0);
    assert_eq!(read(&buffer)["policyCount"], 0);
    release(&mut buffer);

    assert_eq!(encode(&" ".repeat(RESPONSE_LIMIT)), Err(Status::Limit));
}

#[test]
fn unwind_returns_panic_status_without_disabling_subsequent_calls() {
    let mut buffer = Buffer::EMPTY;
    TEST_PANIC.with(|panic| panic.set(true));
    assert_eq!(call(ABI, 0, b"{}", &mut buffer), Status::Panic as u32);
    release(&mut buffer);
    assert_eq!(call(ABI, 0, b"{}", &mut buffer), 0);
    release(&mut buffer);
}

#[test]
fn differential_against_pinned_parser() {
    let manifest: Value = serde_json::from_str(include_str!(
        "../../../tests/DogwoodSharp.Conformance.Tests/parsing-v1.json"
    ))
    .unwrap();
    assert_eq!(manifest["fixtureVersion"], 1);
    assert_eq!(
        manifest["dogwoodRevision"],
        "929f49e7305534ac61ef06cf8e47269c596dc851"
    );
    let schema = ServiceSchema::defaults();

    for case in manifest["cases"].as_array().unwrap() {
        let id = case["id"].as_str().unwrap();
        let source = case["source"].as_str().unwrap();
        let input = json!({"source": source}).to_string();
        let mut buffer = Buffer::EMPTY;
        assert_eq!(call(ABI, 1, input.as_bytes(), &mut buffer), 0, "{id}");
        let reply = read(&buffer);
        release(&mut buffer);

        match ParsedPolicySet::parse(source, &schema) {
            Ok(policies) => {
                assert_eq!(reply["success"], true, "{id}");
                assert_eq!(reply["policyCount"], policies.policy_count(), "{id}");
                assert_eq!(reply["policyCount"], case["policyCount"], "{id}");
                assert_eq!(reply["diagnostics"], json!([]), "{id}");
            }
            Err(error) => {
                assert_eq!(reply["success"], false, "{id}");
                assert!(reply["policyCount"].is_null(), "{id}");
                let diagnostics = reply["diagnostics"].as_array().unwrap();
                let (category, locations) = match &error {
                    Error::Parse(errors) => (
                        "syntax",
                        errors.iter().map(|e| e.location()).collect::<Vec<_>>(),
                    ),
                    Error::Macro(error) => ("macro", vec![error.location()]),
                    _ => panic!("{id}: unexpected upstream error {error}"),
                };
                assert_eq!(case["category"], category, "{id}");
                assert_eq!(diagnostics.len(), locations.len(), "{id}");
                let mut actual_locations = Vec::new();
                for (entry, location) in diagnostics.iter().zip(locations) {
                    assert_eq!(entry["category"], category, "{id}");
                    assert!(!entry["message"].as_str().unwrap().is_empty(), "{id}");
                    assert_eq!(location.source(), source, "{id}");
                    assert_eq!(
                        entry["location"],
                        json!({
                            "offset": location.span().offset() + if source.starts_with('\u{feff}') { 3 } else { 0 },
                            "length": location.span().len()
                        }),
                        "{id}"
                    );
                    actual_locations.push(entry["location"].clone());
                }
                assert_eq!(json!(actual_locations), case["locations"], "{id}");
                eprintln!("{id}: {category} {}", json!(actual_locations));
            }
        }
    }
}
