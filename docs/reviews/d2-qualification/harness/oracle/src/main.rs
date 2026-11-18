//! Upstream-only oracle. This binary never loads DogwoodSharp.
use cedar_policy::{PolicyId, PolicySet, Schema, ValidationMode, Validator as CedarValidator};
use cedar_policy_core::{
    ast::{Eid, EntityUID, Name},
    validator::{ValidatorSchema, types::Type},
};
use dogwood_language::{
    Error, ParsedPolicySet, PolicySchema, ServiceSchema, SourceLoc, ValidationError,
    ValidationWarning, Validator,
};
use serde_json::{Value, json};
use std::io::{self, BufRead};

fn coordinates(source: &str, upstream: &str, offset: usize, length: usize) -> Value {
    let adjusted = match source.strip_prefix('\u{feff}') {
        _ if source == upstream => offset,
        Some(tail) if tail == upstream => offset + 3,
        _ => return Value::Null,
    };
    assert!(adjusted + length <= source.len() || (adjusted == source.len() && length == 1));
    json!({"offset": adjusted, "length": length})
}

fn parse_finding(kind: &str, loc: &SourceLoc, source: &str) -> Value {
    let span = loc.span();
    // Syntax diagnostics retain the original source alongside offsets into
    // the BOM-stripped lexer input. The wire coordinates index the original.
    let lexer_source = if loc.source() == source {
        source.strip_prefix('\u{feff}').unwrap_or(source)
    } else {
        loc.source()
    };
    json!({"category": kind, "location": coordinates(source, lexer_source, span.offset(), span.len())})
}

fn derive(input: &Value) -> Value {
    let source = input["source"].as_str().unwrap();
    let schema_text = input["policySchema"].as_str().unwrap();
    let mut builder = ServiceSchema::builder();
    if let Some(event) = input["eventSchema"].as_str() {
        builder = builder.event_schema_str(event);
    }
    if let Some(library) = input["macros"].as_str() {
        builder = builder.macros_str(library);
    }
    let service = match builder.build() {
        Ok(service) => service,
        Err(error) => return json!({"terminal": "service-schema", "message": error.to_string()}),
    };
    let parsed = match ParsedPolicySet::parse(source, &service) {
        Ok(parsed) => parsed,
        Err(Error::Parse(errors)) => {
            let diagnostics: Vec<_> = errors
                .iter()
                .map(|e| parse_finding("syntax", e.location(), source))
                .collect();
            return json!({"terminal": "parse", "diagnostics": diagnostics});
        }
        Err(Error::Macro(error)) => {
            return json!({"terminal": "parse", "diagnostics": [parse_finding("macro", error.location(), source)]});
        }
        Err(error) => panic!("Unexpected parse outcome: {error}"),
    };
    let count = parsed.policy_count();
    let schema = match PolicySchema::from_cedarschema_str(schema_text) {
        Ok(schema) => schema,
        Err(error) => {
            return json!({"terminal": "action-schema", "policyCount": count, "message": error.to_string()});
        }
    };
    let lowering = match input["distincter"].as_str() {
        Some(name) => parsed.lower_with_distincter(&schema, name),
        None => parsed.lower(&schema),
    };
    let lowered = match lowering {
        Ok(lowered) => lowered,
        Err(error) => {
            return json!({"terminal": "lower", "policyCount": count, "message": error.to_string()});
        }
    };
    let original = Schema::from_cedarschema_str(schema_text.trim_start_matches('\u{feff}'))
        .ok()
        .map(|(schema, _)| schema);
    let mut fields = Vec::new();
    for leaf in lowered.temporal_fields() {
        for action in &leaf.target_actions {
            let name: Name = action
                .namespace
                .as_ref()
                .map_or("Action".into(), |ns| format!("{ns}::Action"))
                .parse()
                .unwrap();
            let uid = EntityUID::from_components(name.into(), Eid::new(action.id.as_str()), None);
            let before = original
                .as_ref()
                .and_then(|schema| attribute(schema.as_ref(), &uid, &leaf.id));
            let after = attribute(lowered.cedar_schema().as_ref(), &uid, &leaf.id);
            fields.push(
                json!({"field": leaf.id, "actionNamespace": action.namespace, "actionId": action.id,
                "original": before, "augmented": after}),
            );
        }
    }
    let outcome = Validator::new().validate(&lowered);
    let errors: Vec<_> = outcome
        .validation_errors()
        .map(|error| {
            let (category, location) = match error {
                ValidationError::Cedar { span, src, .. } => {
                    ("cedar", coordinates(source, src, span.offset(), span.len()))
                }
                ValidationError::Extension {
                    code, span, src, ..
                } => (*code, coordinates(source, src, span.offset(), span.len())),
                _ => panic!("Unrecognized upstream validation finding"),
            };
            json!({"category":category,"location":location})
        })
        .collect();
    let warnings: Vec<_> = outcome
        .validation_warnings()
        .map(|warning| {
            let category = match warning {
                ValidationWarning::Cedar(_) => "cedar",
                ValidationWarning::Extension { code, .. } => *code,
                _ => panic!("Unrecognized upstream validation warning"),
            };
            json!({"category":category,"location":null})
        })
        .collect();
    let rules: Vec<_> = lowered
        .rules()
        .zip(parsed.policies())
        .map(|(rule, authored)| {
            assert_eq!(rule.rule_index, authored.index());
            let policy = lowered
                .as_cedar()
                .policy(&PolicyId::new(&rule.cedar_policy_id))
                .unwrap();
            json!({"ruleIndex": authored.index(), "cedarPolicyId": rule.cedar_policy_id,
            "sourceText": authored.expanded_source(), "body": policy.to_json().unwrap()})
        })
        .collect();
    assert_eq!(rules.len(), count);
    let emission_order: Vec<_> = lowered
        .as_cedar()
        .policies()
        .map(|p| p.id().to_string())
        .collect();
    json!({"terminal": "validate", "policyCount": count, "fields": fields,
        "originalResolved": original.is_some(), "rules": rules, "emissionOrder": emission_order,
        "selfContainedCedar": lowered.is_self_contained_cedar(), "errors": errors, "warnings": warnings,
        "cedarPolicyText": lowered.as_cedar().to_string(),
        "cedarSchemaJson": serde_json::from_str::<Value>(&lowered.cedar_schema_json().unwrap()).unwrap(),
        "cedarSchemaText": lowered.cedar_schema_str().unwrap()})
}

fn attribute(schema: &ValidatorSchema, action: &EntityUID, field: &str) -> Option<Value> {
    let Type::Record { attrs, .. } = schema.context_type(action)? else {
        panic!("Cedar action context is not a record");
    };
    let found = attrs.get_attr(field)?;
    Some(json!({"type": found.attr_type.to_string(), "required": found.is_required()}))
}

fn inspect(artifact: &Value) -> Value {
    let policies: PolicySet = artifact["cedarPolicyText"]
        .as_str()
        .unwrap()
        .parse()
        .unwrap();
    let from_json = Schema::from_json_str(artifact["cedarSchemaJson"].as_str().unwrap()).unwrap();
    let (from_text, _) =
        Schema::from_cedarschema_str(artifact["cedarSchemaText"].as_str().unwrap()).unwrap();
    let a: &ValidatorSchema = from_json.as_ref();
    let b: &ValidatorSchema = from_text.as_ref();
    assert_eq!(a.actions().count(), b.actions().count());
    for action in a.actions() {
        assert_eq!(a.context_type(action), b.context_type(action), "{action}");
    }
    for schema in [from_json, from_text] {
        let result = CedarValidator::new(schema).validate(&policies, ValidationMode::Strict);
        assert!(
            result.validation_passed(),
            "{:?}",
            result.validation_errors().collect::<Vec<_>>()
        );
    }
    // Reparsed Cedar assigns positional IDs. Compare each policy body and
    // annotation independently of these fresh parser-generated IDs.
    let bodies: Vec<_> = policies.policies().map(|p| p.to_json().unwrap()).collect();
    json!({"bodies": bodies, "validWithBothSchemas": true})
}

fn main() {
    let mode = std::env::args().nth(1).unwrap();
    for line in io::stdin().lock().lines() {
        let value = serde_json::from_str(&line.unwrap()).unwrap();
        let reply = match mode.as_str() {
            "derive" => derive(&value),
            "inspect" => inspect(&value),
            _ => panic!("Expected derive or inspect"),
        };
        println!("{reply}");
    }
}
