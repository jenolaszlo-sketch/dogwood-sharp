use cedar_policy::{PolicyId, PolicySet, Schema, SchemaFragment, ValidationMode, Validator};
use miette::Diagnostic;
use serde_json::{Value, json};
use std::io::{self, BufRead};

fn canonical(fragment: SchemaFragment) -> Value {
    let mut value = fragment.to_json_value().unwrap();
    if value[""] == json!({"entityTypes": {}, "actions": {}}) {
        value.as_object_mut().unwrap().remove("");
    }
    value
}

fn policy_body(policy: &cedar_policy::Policy) -> Value {
    let mut value = policy.to_json().unwrap();
    // Dogwood can materialize an implicit true condition when lowering.
    value["conditions"]
        .as_array_mut()
        .unwrap()
        .retain(|condition| *condition != json!({"kind": "when", "body": {"Value": true}}));
    value
}

fn inspect(row: &Value) -> Value {
    let artifact = &row["reply"];
    let from_json = SchemaFragment::from_json_str(artifact["cedarSchemaJson"].as_str().unwrap())
        .expect("parse exported schema JSON");
    let (from_text, _) =
        SchemaFragment::from_cedarschema_str(artifact["cedarSchemaText"].as_str().unwrap())
            .expect("parse exported Cedar schema text");
    assert_eq!(
        canonical(from_json.clone()),
        canonical(from_text.clone()),
        "Cedar's canonical schema fragments must agree"
    );

    let parsed: PolicySet = artifact["cedarPolicyText"]
        .as_str()
        .unwrap()
        .parse()
        .unwrap();
    let sources = row["rules"].as_array().unwrap();
    let rules = artifact["rules"].as_array().unwrap();
    assert_eq!(parsed.policies().count(), sources.len());
    assert_eq!(rules.len(), sources.len());
    let mut rekeyed = PolicySet::new();
    for (index, (source, rule)) in sources.iter().zip(rules).enumerate() {
        let source = source.as_str().unwrap();
        let expected: cedar_policy::Policy = source.parse().unwrap();
        let id: PolicyId = format!("policy_{index}").parse().unwrap();
        assert_eq!(rule["ruleIndex"], index);
        assert_eq!(rule["cedarPolicyId"], id.to_string());
        let mapped: cedar_policy::Policy = rule["sourceText"].as_str().unwrap().parse().unwrap();
        assert_eq!(mapped.to_json().unwrap(), expected.to_json().unwrap());
        let text_id: PolicyId = format!("policy{index}").parse().unwrap();
        let exported = parsed.policy(&text_id).unwrap();
        assert_eq!(policy_body(exported), policy_body(&expected));
        rekeyed.add(exported.new_id(id)).unwrap();
    }
    let mut warnings = Vec::new();
    for fragment in [from_json, from_text] {
        let schema = Schema::from_schema_fragments([fragment]).unwrap();
        let result = Validator::new(schema).validate(&rekeyed, ValidationMode::Strict);
        assert!(result.validation_passed());
        let current: Vec<_> = result
            .validation_warnings()
            .map(|warning| {
                json!({"message": warning.to_string(),
                       "labels": warning.labels().into_iter().flatten().count()})
            })
            .collect();
        if warnings.is_empty() {
            warnings = current;
        } else {
            assert_eq!(warnings, current);
        }
    }

    if row["warning"] == true {
        assert!(!warnings.is_empty() && warnings.iter().all(|w| w["labels"].as_u64().unwrap() > 0));
    } else {
        assert!(warnings.is_empty());
    }
    let wrapped: Vec<_> = artifact["validationWarnings"]
        .as_array()
        .unwrap()
        .iter()
        .map(|w| {
            assert_eq!(w["category"], "cedar");
            assert_eq!(w["location"], Value::Null);
            w["message"].as_str().unwrap()
        })
        .collect();
    assert_eq!(
        wrapped,
        warnings
            .iter()
            .map(|w| w["message"].as_str().unwrap())
            .collect::<Vec<_>>()
    );
    json!({"id": row["id"], "rulesVerified": rules.len(), "cedarWarnings": warnings})
}

fn main() {
    for line in io::stdin().lock().lines() {
        let value = serde_json::from_str(&line.unwrap()).unwrap();
        println!("{}", inspect(&value));
    }
}
