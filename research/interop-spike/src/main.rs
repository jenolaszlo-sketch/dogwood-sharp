use dogwood_language::{
    Authorizer, Event, LoweredPolicySet, PolicySchema, ServiceSchema, Validator, Value,
};
use serde_json::json;

fn main() {
    let schema = PolicySchema::from_cedarschema_str(r#"
namespace Test {
 entity User;
 entity Item;
 action "Approve" appliesTo { principal: [User], resource: [Item], context: { input: { key: String } } };
 action "Read" appliesTo { principal: [User], resource: [Item], context: { input: { key: String } } };
}
"#).unwrap();
    // Explicitly global history for this bounded experiment; no tenant-isolation claim.
    let service = ServiceSchema::builder()
        .event_schema_str(dogwood_language::UNPINNED_EVENT_SCHEMA)
        .build()
        .unwrap();
    let base = r#"permit(principal, action == Test::Action::"Read", resource)"#;
    let temporal = format!(
        r#"{base} when temporal {{ formerly within 10s Test::Action::"Approve"::response{{ input.key: context.input.key }} }};"#
    );
    let mut cases = Vec::new();
    for (name, policy, approved, time, key, expected) in [
        ("plain-allow", format!("{base};"), false, 5, "a", true),
        (
            "plain-forbid",
            format!("{base}; forbid(principal, action, resource);"),
            false,
            5,
            "a",
            false,
        ),
        (
            "temporal-no-history",
            temporal.clone(),
            false,
            5,
            "a",
            false,
        ),
        ("temporal-approved", temporal.clone(), true, 5, "a", true),
        ("temporal-expired", temporal.clone(), true, 20, "a", false),
        ("temporal-wrong-key", temporal.clone(), true, 5, "b", false),
    ] {
        let lowered = LoweredPolicySet::from_str(&policy, &service, &schema).unwrap();
        assert!(
            Validator::new().validate(&lowered).validation_passed(),
            "{name}: invalid"
        );
        let policies: serde_json::Map<String, serde_json::Value> = lowered
            .as_cedar()
            .policies()
            .map(|p| (p.id().to_string(), json!(p.to_string())))
            .collect();
        let exported_schema: serde_json::Value =
            serde_json::from_str(&lowered.cedar_schema_json().unwrap()).unwrap();
        let mut context = json!({"input": {"key": key}});
        // Independent oracle for ONLY this fixed formerly-within expression.
        let binding = approved && time > 1 && time - 1 <= 10 && key == "a";
        for field in lowered.temporal_fields() {
            context[field.id.to_string()] = json!(binding);
        }
        let mut auth = Authorizer::new(lowered);
        if approved {
            let approval = Event::builder("Test::Action::Approve", "response")
                .timestamp(1)
                .principal("Test::User::\"u\"")
                .resource("Test::Item::\"i\"")
                .field("input", "key", Value::String("a".into()))
                .build();
            assert!(auth.is_authorized(&approval).is_none());
        }
        let request = Event::builder("Test::Action::Read", "request")
            .timestamp(time)
            .principal("Test::User::\"u\"")
            .resource("Test::Item::\"i\"")
            .field("input", "key", Value::String(key.into()))
            .request_context("input", "key", Value::String(key.into()))
            .build();
        let response = auth.is_authorized(&request).unwrap();
        assert_eq!(response.allowed(), expected, "{name}");
        let errors: Vec<_> = response.diagnostics().errors().collect();
        assert!(errors.is_empty(), "{name}: {errors:?}");
        cases.push(json!({"name":name,"source":policy,"policies":policies,"schema":exported_schema,"context":context,"allow":response.allowed(),"reasons":response.diagnostics().reason().map(|r|r.cedar_policy_id.clone()).collect::<Vec<_>>(),"errors":errors}));
    }
    println!("{}", serde_json::to_string_pretty(&cases).unwrap());
}
