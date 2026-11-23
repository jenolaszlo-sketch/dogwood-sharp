use serde_json::{Value, json};

use crate::{
    ABI, Buffer, OPERATION_EXPORT, OPERATION_LOWER, OPERATION_VALIDATE, Status,
    dogwoodsharp_call_v1, dogwoodsharp_free_v1,
};

const TEMPORAL_SCHEMA: &str = r#"
namespace Test {
  entity User;
  entity Item;
  action Approve appliesTo {
    principal: [User],
    resource: [Item],
    context: { input: { stock: String } }
  };
  action Sell appliesTo {
    principal: [User],
    resource: [Item],
    context: { input: { stock: String } }
  };
}
"#;

const TEMPORAL_SOURCE: &str = r#"
permit (principal, action == Test::Action::"Sell", resource)
when temporal { formerly within 1h Test::Action::"Approve"::request { input.stock: context.input.stock } };
"#;

const PROVIDER_SCHEMA: &str = r#"
namespace Test {
  entity User;
  entity Item;
  action Read appliesTo {
    principal: [User],
    resource: [Item],
    context: { input: { key: String } }
  };
}
"#;

const PROVIDER_SOURCE: &str = r#"
permit (principal, action == Test::Action::"Read", resource)
when { Http::Fetch("https://trusted.invalid/fetch", context.input.key).body == "ok" };
"#;

fn provider_declarations() -> String {
    json!({
        "availableProviders": {
            "Http::Fetch": {
                "argumentTypes": [
                    { "paramType": "string" },
                    { "paramType": "string" }
                ],
                "outputType": {
                    "paramType": "record",
                    "fields": { "body": { "paramType": "string" } },
                    "required": ["body"]
                },
                "implementation": {
                    "kind": "rhai",
                    "script": "fn evaluate(_url, _key) { #{ body: \"ok\" } }"
                }
            }
        }
    })
    .to_string()
}

fn entity(ty: &str, id: &str) -> Value {
    json!({ "type": ty, "id": id })
}

fn history_event(identity: &str, timestamp: i64, stock: &str) -> Value {
    let context = json!({ "input": { "stock": stock } }).to_string();
    json!({
        "identity": identity,
        "timestamp": timestamp,
        "actionType": "Test::Action",
        "actionId": "Approve",
        "kind": "request",
        "principal": entity("Test::User", "alice"),
        "resource": entity("Test::Item", "i1"),
        "contextJson": context,
        "loggedJson": context,
        "entitiesJson": "[]"
    })
}

fn current_event(action_id: &str, context: Value) -> Value {
    json!({
        "actionType": "Test::Action",
        "actionId": action_id,
        "kind": "request",
        "principal": entity("Test::User", "alice"),
        "resource": entity("Test::Item", "i1"),
        "contextJson": context.to_string(),
        "entitiesJson": "[]",
        "loggedJson": ""
    })
}

fn runtime_request(
    source: &str,
    policy_schema: &str,
    distincter: &str,
    provider_declarations: Option<&str>,
    binding: Value,
) -> Value {
    let mut request = json!({
        "source": source,
        "policySchema": policy_schema,
        "distincter": distincter,
        "runtimeBindingVersion": 1,
        "runtimeBinding": binding
    });
    if let Some(declarations) = provider_declarations {
        request["providerDeclarations"] = Value::String(declarations.to_owned());
    }
    request
}

fn call(operation: u32, request: &Value) -> (u32, Option<Value>) {
    let bytes = request.to_string();
    let mut output = Buffer::EMPTY;
    let status = unsafe {
        dogwoodsharp_call_v1(
            ABI,
            operation,
            bytes.as_ptr(),
            bytes.len() as u32,
            &mut output,
        )
    };
    if status != 0 {
        unsafe { dogwoodsharp_free_v1(&mut output) };
        return (status, None);
    }
    assert!(!output.data.is_null());
    assert!(output.len > 0);
    let reply = serde_json::from_slice(unsafe {
        std::slice::from_raw_parts(output.data, output.len as usize)
    })
    .expect("native reply is JSON");
    unsafe { dogwoodsharp_free_v1(&mut output) };
    (status, Some(reply))
}

fn temporal_binding(timestamp: i64, history: Vec<Value>) -> Value {
    json!({
        "timestamp": timestamp,
        "history": history,
        "providers": [],
        "current": current_event(
            "Sell",
            json!({ "input": { "stock": "AMZN" } })
        )
    })
}

#[test]
fn operation_four_runtime_binds_true_and_false_temporal_values() {
    let true_request = runtime_request(
        TEMPORAL_SOURCE,
        TEMPORAL_SCHEMA,
        "r2t",
        None,
        temporal_binding(100, vec![history_event("history-1", 0, "AMZN")]),
    );
    let (status, reply) = call(OPERATION_EXPORT, &true_request);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["artifactsExported"], true);
    assert_eq!(reply["runtimeBinding"]["version"], 1);
    assert_eq!(reply["runtimeBinding"]["status"], "bound");
    assert_eq!(reply["runtimeBinding"]["error"], Value::Null);
    let context: Value = serde_json::from_str(
        reply["runtimeBinding"]["contextJson"]
            .as_str()
            .expect("bound context"),
    )
    .unwrap();
    assert_eq!(context["r2t_0__temporal_0"], true);

    let false_request = runtime_request(
        TEMPORAL_SOURCE,
        TEMPORAL_SCHEMA,
        "r2t",
        None,
        temporal_binding(100, vec![history_event("history-1", 0, "MSFT")]),
    );
    let (status, reply) = call(OPERATION_EXPORT, &false_request);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    let context: Value = serde_json::from_str(
        reply["runtimeBinding"]["contextJson"]
            .as_str()
            .expect("bound context"),
    )
    .unwrap();
    assert_eq!(context["r2t_0__temporal_0"], false);
}

#[test]
fn operation_four_runtime_accepts_non_decision_history_without_promoting_it() {
    let mut response = history_event("history-response", 0, "AMZN");
    response["kind"] = json!("response");
    let request = runtime_request(
        TEMPORAL_SOURCE,
        TEMPORAL_SCHEMA,
        "r2response",
        None,
        temporal_binding(100, vec![response]),
    );
    let (status, reply) = call(OPERATION_EXPORT, &request);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["runtimeBinding"]["status"], "bound");
    let context: Value = serde_json::from_str(
        reply["runtimeBinding"]["contextJson"]
            .as_str()
            .expect("bound context"),
    )
    .unwrap();
    assert_eq!(context["r2response_0__temporal_0"], false);
}

#[test]
fn operation_four_runtime_uses_the_closed_temporal_window_boundary() {
    let at_boundary = runtime_request(
        TEMPORAL_SOURCE,
        TEMPORAL_SCHEMA,
        "r2boundary",
        None,
        temporal_binding(3600, vec![history_event("history-1", 0, "AMZN")]),
    );
    let (status, reply) = call(OPERATION_EXPORT, &at_boundary);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["runtimeBinding"]["status"], "bound");
    let context: Value = serde_json::from_str(
        reply["runtimeBinding"]["contextJson"]
            .as_str()
            .expect("bound context"),
    )
    .unwrap();
    assert_eq!(context["r2boundary_0__temporal_0"], true);

    let outside_boundary = runtime_request(
        TEMPORAL_SOURCE,
        TEMPORAL_SCHEMA,
        "r2boundary",
        None,
        temporal_binding(3601, vec![history_event("history-1", 0, "AMZN")]),
    );
    let (status, reply) = call(OPERATION_EXPORT, &outside_boundary);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["runtimeBinding"]["status"], "bound");
    let context: Value = serde_json::from_str(
        reply["runtimeBinding"]["contextJson"]
            .as_str()
            .expect("bound context"),
    )
    .unwrap();
    assert_eq!(context["r2boundary_0__temporal_0"], false);
}

#[test]
fn operation_four_runtime_rejects_invalid_timelines_and_reserved_context() {
    let duplicate = temporal_binding(
        100,
        vec![
            history_event("duplicate", 0, "AMZN"),
            history_event("duplicate", 1, "AMZN"),
        ],
    );
    let (status, reply) = call(
        OPERATION_EXPORT,
        &runtime_request(TEMPORAL_SOURCE, TEMPORAL_SCHEMA, "r2t", None, duplicate),
    );
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["artifactsExported"], true);
    assert_eq!(reply["runtimeBinding"]["status"], "failed");
    assert!(
        reply["runtimeBinding"]["error"]
            .as_str()
            .unwrap()
            .contains("not unique")
    );

    let mut reserved = temporal_binding(100, vec![history_event("history-1", 0, "AMZN")]);
    reserved["current"]["contextJson"] = Value::String(
        json!({
            "input": { "stock": "AMZN" },
            "r2t_0__temporal_0": true
        })
        .to_string(),
    );
    let (status, reply) = call(
        OPERATION_EXPORT,
        &runtime_request(TEMPORAL_SOURCE, TEMPORAL_SCHEMA, "r2t", None, reserved),
    );
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["runtimeBinding"]["status"], "failed");
    assert!(
        reply["runtimeBinding"]["error"]
            .as_str()
            .unwrap()
            .contains("reserved generated field")
    );
}

#[test]
fn operation_four_runtime_binds_provider_value_and_fails_closed_without_one() {
    let binding = json!({
        "timestamp": 100,
        "history": [],
        "providers": [{
            "name": "Http::Fetch",
            "outcome": "value",
            "valueJson": "{\"body\":\"ok\"}",
            "message": null
        }],
        "current": current_event(
            "Read",
            json!({ "input": { "key": "document" } })
        )
    });
    let request = runtime_request(
        PROVIDER_SOURCE,
        PROVIDER_SCHEMA,
        "r2p",
        Some(&provider_declarations()),
        binding,
    );
    let (status, reply) = call(OPERATION_EXPORT, &request);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["artifactsExported"], true);
    assert_eq!(reply["runtimeBinding"]["status"], "bound");
    let context: Value = serde_json::from_str(
        reply["runtimeBinding"]["contextJson"]
            .as_str()
            .expect("bound context"),
    )
    .unwrap();
    assert_eq!(context["providers"]["r2p_0_p_0"]["body"], "ok");

    let missing = json!({
        "timestamp": 100,
        "history": [],
        "providers": [],
        "current": current_event(
            "Read",
            json!({ "input": { "key": "document" } })
        )
    });
    let request = runtime_request(
        PROVIDER_SOURCE,
        PROVIDER_SCHEMA,
        "r2p",
        Some(&provider_declarations()),
        missing,
    );
    let (status, reply) = call(OPERATION_EXPORT, &request);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["artifactsExported"], true);
    assert_eq!(reply["runtimeBinding"]["status"], "failed");
    assert!(
        reply["runtimeBinding"]["error"]
            .as_str()
            .unwrap()
            .contains("no trusted outcome")
    );
}

#[test]
fn operation_four_runtime_provider_failure_never_falls_back() {
    for outcome in ["omitted", "failure"] {
        let binding = json!({
            "timestamp": 100,
            "history": [],
            "providers": [{
                "name": "Http::Fetch",
                "outcome": outcome,
                "valueJson": null,
                "message": "trusted outcome is unavailable"
            }],
            "current": current_event(
                "Read",
                json!({ "input": { "key": "document" } })
            )
        });
        let request = runtime_request(
            PROVIDER_SOURCE,
            PROVIDER_SCHEMA,
            "r2p",
            Some(&provider_declarations()),
            binding,
        );
        let (status, reply) = call(OPERATION_EXPORT, &request);
        assert_eq!(status, 0);
        let reply = reply.unwrap();
        assert_eq!(reply["runtimeBinding"]["status"], "failed");
        assert_eq!(reply["runtimeBinding"]["contextJson"], "");
    }
}

#[test]
fn operation_four_runtime_rejects_provider_method_chains_that_require_rhai() {
    let declarations = json!({
        "availableProviders": {
            "Http::Fetch": {
                "argumentTypes": [
                    { "paramType": "string" },
                    { "paramType": "string" }
                ],
                "outputType": {
                    "paramType": "record",
                    "fields": { "body": { "paramType": "string" } },
                    "required": ["body"]
                },
                "availableMethods": {
                    "normalize": {
                        "argumentTypes": [],
                        "outputType": { "paramType": "string" }
                    }
                },
                "implementation": {
                    "kind": "rhai",
                    "script": "fn evaluate(_url, _key) { #{ body: \"ok\" } } fn normalize(input) { input.body }"
                }
            }
        }
    })
    .to_string();
    let source = r#"
permit (principal, action == Test::Action::"Read", resource)
when guardrails { Http::Fetch("https://trusted.invalid/fetch", context.input.key).normalize() == "ok" };
"#;
    let binding = json!({
        "timestamp": 100,
        "history": [],
        "providers": [{
            "name": "Http::Fetch",
            "outcome": "value",
            "valueJson": "{\"body\":\"ok\"}",
            "message": null
        }],
        "current": current_event(
            "Read",
            json!({ "input": { "key": "document" } })
        )
    });
    let request = runtime_request(
        source,
        PROVIDER_SCHEMA,
        "r2method",
        Some(&declarations),
        binding,
    );
    let (status, reply) = call(OPERATION_EXPORT, &request);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["artifactsExported"], true);
    assert_eq!(reply["runtimeBinding"]["status"], "failed");
    assert!(
        reply["runtimeBinding"]["error"]
            .as_str()
            .unwrap()
            .contains("method chain")
    );
}

#[test]
fn frozen_operations_reject_operation_four_extensions() {
    let base = json!({
        "source": "permit(principal, action, resource);",
        "policySchema": "namespace App { entity User; entity Doc; action read appliesTo { principal: [User], resource: [Doc] }; }"
    });
    for operation in [OPERATION_VALIDATE, OPERATION_LOWER] {
        for extension in [
            json!({ "providerDeclarations": "{}" }),
            json!({ "providerDeclarations": null }),
            json!({ "runtimeBindingVersion": 1 }),
            json!({ "runtimeBindingVersion": null }),
            json!({ "runtimeBinding": {} }),
            json!({ "runtimeBinding": null }),
        ] {
            let mut request = base.clone();
            for (name, value) in extension.as_object().unwrap() {
                request[name] = value.clone();
            }
            let (status, reply) = call(operation, &request);
            assert_eq!(status, Status::InvalidEnvelope as u32);
            assert!(reply.is_none());
        }
    }
}

#[test]
fn operation_four_rejects_invalid_runtime_version_pairings() {
    let base = json!({
        "source": "permit(principal, action, resource);",
        "policySchema": "namespace App { entity User; entity Doc; action read appliesTo { principal: [User], resource: [Doc] }; }"
    });
    let binding = json!({
        "timestamp": 100,
        "history": [],
        "providers": [],
        "current": {
            "actionType": "App::Action",
            "actionId": "read",
            "kind": "request",
            "principal": entity("App::User", "alice"),
            "resource": entity("App::Doc", "d1"),
            "contextJson": "{}",
            "entitiesJson": "[]",
            "loggedJson": ""
        }
    });
    for fields in [
        json!({ "runtimeBindingVersion": 1 }),
        json!({ "runtimeBinding": binding }),
        json!({ "runtimeBindingVersion": null }),
        json!({ "runtimeBinding": null }),
        json!({ "runtimeBindingVersion": null, "runtimeBinding": null }),
        json!({ "runtimeBindingVersion": 2, "runtimeBinding": binding }),
        json!({ "runtimeBindingVersion": 1, "runtimeBinding": null }),
    ] {
        let mut request = base.clone();
        for (name, value) in fields.as_object().unwrap() {
            request[name] = value.clone();
        }
        let (status, reply) = call(OPERATION_EXPORT, &request);
        assert_eq!(status, Status::InvalidEnvelope as u32);
        assert!(reply.is_none());
    }
}

#[test]
fn runtime_reply_adds_exactly_one_property_to_the_legacy_export() {
    let schema = "namespace App { entity User; entity Doc; action read appliesTo { principal: [User], resource: [Doc] }; }";
    let legacy = json!({
        "source": "permit(principal, action, resource);",
        "policySchema": schema
    });
    let (status, reply) = call(OPERATION_EXPORT, &legacy);
    assert_eq!(status, 0);
    let legacy = reply.unwrap();
    assert!(legacy.get("runtimeBinding").is_none());

    let binding = json!({
        "timestamp": 100,
        "history": [],
        "providers": [],
        "current": {
            "actionType": "App::Action",
            "actionId": "read",
            "kind": "request",
            "principal": entity("App::User", "alice"),
            "resource": entity("App::Doc", "d1"),
            "contextJson": "{}",
            "entitiesJson": "[]",
            "loggedJson": ""
        }
    });
    let extended_request = runtime_request(
        "permit(principal, action, resource);",
        schema,
        "r2shape",
        None,
        binding,
    );
    let (status, reply) = call(OPERATION_EXPORT, &extended_request);
    assert_eq!(status, 0);
    let extended = reply.unwrap();

    let legacy_keys = legacy
        .as_object()
        .unwrap()
        .keys()
        .map(String::as_str)
        .collect::<std::collections::BTreeSet<_>>();
    let extended_keys = extended
        .as_object()
        .unwrap()
        .keys()
        .map(String::as_str)
        .collect::<std::collections::BTreeSet<_>>();
    let mut expected_keys = legacy_keys.clone();
    expected_keys.insert("runtimeBinding");
    assert_eq!(extended_keys, expected_keys);
    assert_eq!(extended_keys.len(), legacy_keys.len() + 1);

    let runtime = extended["runtimeBinding"].as_object().unwrap();
    assert_eq!(
        runtime
            .keys()
            .map(String::as_str)
            .collect::<std::collections::BTreeSet<_>>(),
        std::collections::BTreeSet::from(["version", "status", "contextJson", "error"])
    );
    assert_eq!(runtime["version"], 1);
    assert_eq!(runtime["status"], "bound");
    assert_eq!(runtime["error"], Value::Null);
}

#[test]
fn operation_four_runtime_envelope_shape_is_strict() {
    let valid = temporal_binding(100, vec![history_event("history-1", 0, "AMZN")]);

    let mut missing_timestamp = valid.clone();
    missing_timestamp
        .as_object_mut()
        .unwrap()
        .remove("timestamp");
    let mut missing_history = valid.clone();
    missing_history.as_object_mut().unwrap().remove("history");
    let mut missing_providers = valid.clone();
    missing_providers
        .as_object_mut()
        .unwrap()
        .remove("providers");
    let mut missing_current = valid.clone();
    missing_current.as_object_mut().unwrap().remove("current");
    let mut unknown_member = valid.clone();
    unknown_member["unknown"] = json!(true);

    let mut missing_identity = valid.clone();
    missing_identity["history"][0]
        .as_object_mut()
        .unwrap()
        .remove("identity");
    let mut missing_nullable_scope = valid.clone();
    missing_nullable_scope["history"][0]
        .as_object_mut()
        .unwrap()
        .remove("principal");
    let mut unknown_history_member = valid.clone();
    unknown_history_member["history"][0]["unknown"] = json!(true);

    let mut missing_logged = valid.clone();
    missing_logged["current"]
        .as_object_mut()
        .unwrap()
        .remove("loggedJson");
    let mut wrong_current_scope = valid.clone();
    wrong_current_scope["current"]["principal"] = json!("alice");

    let mut missing_provider_value = valid.clone();
    missing_provider_value["providers"] = json!([{
        "name": "Http::Fetch",
        "outcome": "value",
        "message": null
    }]);
    let mut unknown_provider_member = valid.clone();
    unknown_provider_member["providers"] = json!([{
        "name": "Http::Fetch",
        "outcome": "value",
        "valueJson": "{}",
        "message": null,
        "unknown": true
    }]);
    let mut unknown_provider_kind = valid.clone();
    unknown_provider_kind["providers"] = json!([{
        "name": "Http::Fetch",
        "outcome": "unknown",
        "valueJson": "{}",
        "message": null
    }]);

    for binding in [
        missing_timestamp,
        missing_history,
        missing_providers,
        missing_current,
        unknown_member,
        missing_identity,
        missing_nullable_scope,
        unknown_history_member,
        missing_logged,
        wrong_current_scope,
        missing_provider_value,
        unknown_provider_member,
        unknown_provider_kind,
    ] {
        let request = runtime_request(TEMPORAL_SOURCE, TEMPORAL_SCHEMA, "r2strict", None, binding);
        let (status, reply) = call(OPERATION_EXPORT, &request);
        assert_eq!(status, Status::InvalidEnvelope as u32);
        assert!(reply.is_none());
    }
}

#[test]
fn operation_four_runtime_malformed_history_payloads_fail_the_binding() {
    for (field, value, expected) in [
        ("contextJson", "{", "contextJson is invalid JSON"),
        ("loggedJson", "[]", "loggedJson must contain a JSON object"),
        (
            "entitiesJson",
            "{}",
            "entitiesJson must contain a JSON array",
        ),
    ] {
        let mut binding = temporal_binding(100, vec![history_event("history-1", 0, "AMZN")]);
        binding["history"][0][field] = json!(value);
        let request = runtime_request(
            TEMPORAL_SOURCE,
            TEMPORAL_SCHEMA,
            "r2malformed",
            None,
            binding,
        );
        let (status, reply) = call(OPERATION_EXPORT, &request);
        assert_eq!(status, 0);
        let reply = reply.unwrap();
        assert_eq!(reply["artifactsExported"], true);
        assert_eq!(reply["runtimeBinding"]["status"], "failed");
        assert_eq!(reply["runtimeBinding"]["contextJson"], "");
        assert!(
            reply["runtimeBinding"]["error"]
                .as_str()
                .unwrap()
                .contains(expected)
        );
    }
}

#[test]
fn operation_four_runtime_rejects_non_monotonic_and_non_historic_timestamps() {
    let cases = [
        (
            vec![
                history_event("history-1", 10, "AMZN"),
                history_event("history-2", 9, "AMZN"),
            ],
            "not strictly increasing",
        ),
        (
            vec![
                history_event("history-1", 10, "AMZN"),
                history_event("history-2", 10, "AMZN"),
            ],
            "not strictly increasing",
        ),
        (
            vec![history_event("history-1", 100, "AMZN")],
            "is not before current timestamp",
        ),
    ];

    for (history, expected) in cases {
        let request = runtime_request(
            TEMPORAL_SOURCE,
            TEMPORAL_SCHEMA,
            "r2timeline",
            None,
            temporal_binding(100, history),
        );
        let (status, reply) = call(OPERATION_EXPORT, &request);
        assert_eq!(status, 0);
        let reply = reply.unwrap();
        assert_eq!(reply["runtimeBinding"]["status"], "failed");
        assert!(
            reply["runtimeBinding"]["error"]
                .as_str()
                .unwrap()
                .contains(expected)
        );
    }
}

#[test]
fn operation_four_runtime_rejects_unknown_duplicate_and_malformed_provider_outcomes() {
    let base = json!({
        "timestamp": 100,
        "history": [],
        "providers": [],
        "current": current_event(
            "Read",
            json!({ "input": { "key": "document" } })
        )
    });

    let mut unknown = base.clone();
    unknown["providers"] = json!([{
        "name": "Unknown::Provider",
        "outcome": "value",
        "valueJson": "{}",
        "message": null
    }]);
    let mut duplicate = base.clone();
    duplicate["providers"] = json!([{
        "name": "Http::Fetch",
        "outcome": "value",
        "valueJson": "{\"body\":\"ok\"}",
        "message": null
    }, {
        "name": "Http::Fetch",
        "outcome": "value",
        "valueJson": "{\"body\":\"ok\"}",
        "message": null
    }]);
    let mut malformed = base.clone();
    malformed["providers"] = json!([{
        "name": "Http::Fetch",
        "outcome": "value",
        "valueJson": "{not-json",
        "message": null
    }]);

    for (binding, expected) in [
        (unknown, "unknown provider"),
        (duplicate, "supplied more than once"),
        (malformed, "malformed valueJson"),
    ] {
        let request = runtime_request(
            PROVIDER_SOURCE,
            PROVIDER_SCHEMA,
            "r2provider",
            Some(&provider_declarations()),
            binding,
        );
        let (status, reply) = call(OPERATION_EXPORT, &request);
        assert_eq!(status, 0);
        let reply = reply.unwrap();
        assert_eq!(reply["runtimeBinding"]["status"], "failed");
        assert_eq!(reply["runtimeBinding"]["contextJson"], "");
        assert!(
            reply["runtimeBinding"]["error"]
                .as_str()
                .unwrap()
                .contains(expected)
        );
    }
}

#[test]
fn operation_four_malformed_provider_declarations_are_schema_failures() {
    let binding = json!({
        "timestamp": 100,
        "history": [],
        "providers": [],
        "current": current_event(
            "Read",
            json!({ "input": { "key": "document" } })
        )
    });
    let request = runtime_request(
        PROVIDER_SOURCE,
        PROVIDER_SCHEMA,
        "r2decl",
        Some("{not-json"),
        binding,
    );
    let (status, reply) = call(OPERATION_EXPORT, &request);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["artifactsExported"], false);
    assert_eq!(reply["generatedContextFields"], json!([]));
    assert_eq!(reply["stageError"]["stage"], "service-schema");
    assert_eq!(reply["stageError"]["category"], "schema");
    assert_eq!(reply["runtimeBinding"]["status"], "failed");
    assert_eq!(reply["runtimeBinding"]["contextJson"], "");
    assert!(
        reply["runtimeBinding"]["error"]
            .as_str()
            .unwrap()
            .contains("providers.json")
    );
}

#[test]
fn operation_four_runtime_binding_is_reported_when_the_policy_pipeline_fails() {
    let request = runtime_request(
        "permit(",
        PROVIDER_SCHEMA,
        "r2pipeline",
        None,
        json!({
            "timestamp": 100,
            "history": [],
            "providers": [],
            "current": current_event(
                "Read",
                json!({ "input": { "key": "document" } })
            )
        }),
    );
    let (status, reply) = call(OPERATION_EXPORT, &request);
    assert_eq!(status, 0);
    let reply = reply.unwrap();
    assert_eq!(reply["parsed"], false);
    assert_eq!(reply["artifactsExported"], false);
    assert_eq!(reply["runtimeBinding"]["status"], "failed");
    assert_eq!(reply["runtimeBinding"]["contextJson"], "");
    assert!(
        reply["runtimeBinding"]["error"]
            .as_str()
            .unwrap()
            .contains("policy parsing failed")
    );
}
