//! DOGWOOD-SHARP-D3-IMPL-01-R5-R1 direct Cedar adjudication harness.
//!
//! Reads a corpus of independently constructed minimal Cedar policies and
//! schema-directed JSON requests and evaluates each one through the
//! underlying Cedar engine itself. It does not link the Dogwood crate and
//! does not use the Dogwood reference conversion path: the context JSON is
//! parsed by Cedar's own JSON authorization API against the action schema,
//! exactly as the D3 CedarSharp path does.
//!
//! For every case it records the stage reached, the decision, the determining
//! policy ids, the operation-failed state, the evaluation errors, the policy
//! error ids, the clean-Allow flag, and the engine's type interpretation of
//! each context field (so that representation equivalence is established by
//! the engine itself, not by agreement between two implementations).
//!
//! Test tooling only: never a production execution path.
//!
//! Usage: `cedar-adjudicator <corpus.json> <outcome.json>`

use std::collections::BTreeSet;
use std::str::FromStr;

use cedar_policy::{
    Authorizer, Context, Decision, Entities, EntityId, EntityTypeName, EntityUid, Policy,
    PolicyId, PolicySet, Request, Schema,
};
use serde::Deserialize;
use serde_json::Value as Json;

#[derive(Debug, Deserialize)]
struct Case {
    id: String,
    note: String,
    #[serde(default, rename = "fixtureId")]
    fixture_id: Option<String>,
    /// Optional explicit Cedar policy id, matching the id the fixture's
    /// lowered policy carries (e.g. `r3c23h_0`). When absent the policy id is
    /// engine-generated.
    #[serde(default, rename = "policyId")]
    policy_id: Option<String>,
    schema: String,
    policy: String,
    action: String,
    principal: String,
    resource: String,
    context: Json,
    #[serde(default)]
    entities: Option<Json>,
}

#[derive(Debug, Deserialize)]
struct Corpus {
    cases: Vec<Case>,
    #[serde(default, rename = "sharedEntities")]
    shared_entities: Option<Json>,
}

#[derive(Debug, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct Outcome {
    id: String,
    note: String,
    fixture_id: Option<String>,
    engine: String,
    stage: String,
    decision: Option<String>,
    determining: Vec<String>,
    operation_failed: bool,
    has_errors: bool,
    errors: Vec<String>,
    policy_error_ids: Vec<String>,
    clean_allow: bool,
    interpretation: serde_json::Map<String, Json>,
}

fn parse_uid(text: &str) -> Result<EntityUid, String> {
    // The corpus uses Cedar textual uid syntax: `Type::"id"`. The id may
    // itself contain `::`, so the type prefix is everything before the last
    // `::` separator.
    let (ty, id) = text
        .rsplit_once("::")
        .ok_or_else(|| format!("uid `{text}` has no `::`"))?;
    let entity_id = EntityId::from_str(id.trim_matches('"'))
        .map_err(|error| format!("entity id `{id}`: {error}"))?;
    let entity_ty =
        EntityTypeName::from_str(ty).map_err(|error| format!("entity type `{ty}`: {error}"))?;
    Ok(EntityUid::from_type_name_and_id(entity_ty, entity_id))
}

/// Describe the typed value Cedar itself built from one JSON encoding. The
/// description is structural: an extension-function application names the
/// constructor and its literal arguments, so two encodings that reach the
/// same typed value produce byte-identical descriptions.
fn describe_context(context: &Context) -> Json {
    // Cedar writes each typed value back in its canonical natural JSON form. A
    // typed ipaddr/datetime/decimal value always serializes through the
    // `__extn` escape with its constructor name and literal argument, while a
    // plain string or record keeps its plain form. Two encodings that reached
    // the same typed value therefore produce byte-identical natural JSON, and
    // an encoding that did not remains distinguishable.
    match context.to_json_value() {
        Ok(value) => value,
        Err(error) => serde_json::json!({ "naturalJsonError": format!("{error}") }),
    }
}

fn fault(case: &Case, engine: &str, stage: &str, message: String) -> Outcome {
    Outcome {
        id: case.id.clone(),
        note: case.note.clone(),
        fixture_id: case.fixture_id.clone(),
        engine: engine.to_string(),
        stage: stage.to_string(),
        decision: None,
        determining: Vec::new(),
        operation_failed: true,
        has_errors: true,
        errors: vec![message],
        policy_error_ids: Vec::new(),
        clean_allow: false,
        interpretation: serde_json::Map::new(),
    }
}

fn run_case(case: &Case, corpus_shared_entities: &Option<Json>, engine: &str) -> Outcome {
    let (schema, _warnings) = match Schema::from_cedarschema_str(&case.schema) {
        Ok(pair) => pair,
        Err(error) => return fault(case, engine, "schema-parse", format!("schema parse failed: {error}")),
    };

    let policies = match &case.policy_id {
        Some(id) => match PolicyId::from_str(id)
            .map_err(|error| format!("policy id `{id}`: {error}"))
            .and_then(|policy_id| {
                Policy::parse(Some(policy_id), &case.policy)
                    .map_err(|error| format!("policy parse failed: {error}"))
            })
            .and_then(|policy| {
                PolicySet::from_policies([policy]).map_err(|error| format!("policy set: {error}"))
            }) {
            Ok(policies) => policies,
            Err(error) => return fault(case, engine, "policy-parse", error),
        },
        None => match PolicySet::from_str(&case.policy) {
            Ok(policies) => policies,
            Err(error) => {
                return fault(case, engine, "policy-parse", format!("policy parse failed: {error}"))
            }
        },
    };

    let action = match parse_uid(&case.action) {
        Ok(action) => action,
        Err(error) => return fault(case, engine, "uid-parse", error),
    };

    // Cedar's own JSON authorization parsing, directed by the action schema.
    let context = match Context::from_json_value(case.context.clone(), Some((&schema, &action))) {
        Ok(context) => context,
        Err(error) => {
            return fault(case, engine, "context-parse", format!("context JSON parse failed: {error}"))
        }
    };

    // Record the engine's own interpretation of the whole context document, in
    // canonical natural JSON. This is the machine-checkable proof that two
    // encodings reached the same typed value: the interpretation is produced by
    // Cedar's parser, not inferred.
    let interpretation = serde_json::json!({ "contextNaturalJson": describe_context(&context) });
    let interpretation = interpretation.as_object().cloned().unwrap_or_default();

    let entities_json = case
        .entities
        .clone()
        .or_else(|| corpus_shared_entities.clone())
        .unwrap_or_else(|| serde_json::json!([]));
    let entities = match Entities::from_json_value(entities_json, Some(&schema)) {
        Ok(entities) => entities,
        Err(error) => {
            return fault(case, engine, "entity-parse", format!("entity JSON parse failed: {error}"))
        }
    };

    let request = match Request::new(
        parse_uid(&case.principal).expect("principal uid parses"),
        action,
        parse_uid(&case.resource).expect("resource uid parses"),
        context,
        Some(&schema),
    ) {
        Ok(request) => request,
        Err(error) => {
            return fault(case, engine, "request-validation", format!("request validation failed: {error}"))
        }
    };

    let response = Authorizer::new().is_authorized(&request, &policies, &entities);
    let decision = match response.decision() {
        Decision::Allow => Some("allow".to_string()),
        Decision::Deny => Some("deny".to_string()),
    };
    let determining: Vec<String> = response
        .diagnostics()
        .reason()
        .map(|id| id.to_string())
        .collect::<BTreeSet<_>>()
        .into_iter()
        .collect();
    let mut errors = Vec::new();
    let mut policy_error_ids = BTreeSet::new();
    for error in response.diagnostics().errors() {
        errors.push(format!("{error}"));
        match error {
            cedar_policy::AuthorizationError::PolicyEvaluationError(evaluation) => {
                policy_error_ids.insert(evaluation.policy_id().to_string());
            }
            _ => {}
        }
    }
    let has_errors = !errors.is_empty();
    let clean_allow =
        matches!(response.decision(), Decision::Allow) && !has_errors && policy_error_ids.is_empty();
    Outcome {
        id: case.id.clone(),
        note: case.note.clone(),
        fixture_id: case.fixture_id.clone(),
        engine: engine.to_string(),
        stage: "evaluation".to_string(),
        decision,
        determining,
        operation_failed: false,
        has_errors,
        errors,
        policy_error_ids: policy_error_ids.into_iter().collect(),
        clean_allow,
        interpretation,
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() != 3 {
        eprintln!("usage: cedar-adjudicator <corpus.json> <outcome.json>");
        std::process::exit(2);
    }
    let corpus_text = std::fs::read_to_string(&args[1]).expect("corpus");
    let corpus: Corpus = serde_json::from_str(&corpus_text).expect("corpus parse");
    let engine = "cedar-4.13.0".to_string();
    let outcomes: Vec<Outcome> = corpus
        .cases
        .iter()
        .map(|case| run_case(case, &corpus.shared_entities, &engine))
        .collect();
    let text = serde_json::to_string_pretty(&outcomes).expect("serialize");
    std::fs::write(&args[2], text + "\n").expect("write");
    println!("cedar adjudicator ({}): {} cases -> {}", engine, outcomes.len(), args[2]);
}

#[cfg(test)]
mod tests {
    use super::*;

    // The harness never silently accepts a corpus that its own parser cannot
    // drive: every case must carry the five identity fields the outcome
    // records. The corpus is validated on every run by construction (serde
    // rejects a malformed case), and this test pins the structural minimum.
    #[test]
    fn corpus_shape_minimum() {
        let corpus: Corpus = serde_json::from_str(
            r#"{"cases":[{"id":"x","note":"n","schema":"namespace App { entity User; entity Doc; action read appliesTo { principal: [User], resource: [Doc], context: { ip: ipaddr } }; }","policy":"permit(principal, action, resource);","action":"App::Action::\"read\"","principal":"App::User::\"alice\"","resource":"App::Doc::\"d1\"","context":{},"entities":[]}]}"#,
        )
        .expect("minimum corpus parses");
        assert_eq!(corpus.cases.len(), 1);
    }
}
#[test]
fn explicit_policy_id_is_preserved() {
    let policy =
        Policy::parse(Some(PolicyId::from_str("r3c23h_0").unwrap()), "permit(principal, action, resource);")
            .expect("parse");
    let policies = PolicySet::from_policies([policy]).expect("set");
    let ids: Vec<String> = policies.policies().map(|p| p.id().to_string()).collect();
    assert_eq!(ids, vec!["r3c23h_0".to_string()]);
}
