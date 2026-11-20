//! DOGWOOD-SHARP-D3-PREP-01 upstream Dogwood authorizer oracle.
//!
//! Executes exactly one request through the *actual* upstream Dogwood
//! authorizer — `dogwood-language` at the revision the D2 bridge pins
//! (`929f49e7305534ac61ef06cf8e47269c596dc851`, dogwood 1.1.2) — and writes
//! one normalized JSON outcome to stdout.
//!
//! The oracle deliberately drives the upstream **public** surface
//! (`ServiceSchema` → `PolicySchema` → `LoweredPolicySet` → `Authorizer` →
//! `Event` → `is_authorized`), which is the same path production Dogwood
//! takes. That path includes the upstream three-tier entity-store
//! construction (caller entities validated against the augmented schema, bare
//! scope entities added unchecked, schema action entities merged last), the
//! temporal pipeline and the provider pipeline. Cedar-to-Cedar differential
//! results cannot stand in for it.
//!
//! Test tooling only: never a production execution path.
//!
//! Usage: `dogwoodsharp-upstream-oracle <envelope.json>` or
//! `dogwoodsharp-upstream-oracle -` to read the envelope from stdin.

use std::io::Read;

use dogwood_language::{
    Authorizer, Decision, Distincter, Event, LoweredPolicySet, PolicySchema, ServiceSchema, Value,
};
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value as Json, json};

/// The request envelope. One envelope is one upstream decision.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Envelope {
    /// Optional case id, echoed in a batch result.
    #[serde(default)]
    id: Option<String>,
    /// Dogwood policy source (the same source the D2 lowering consumes).
    source: String,
    /// The Cedar policy schema, in Cedar schema text form.
    policy_schema: String,
    /// Optional Dogwood event schema (the request/response kind DSL).
    #[serde(default)]
    event_schema: Option<String>,
    /// Optional Dogwood macro library source.
    #[serde(default)]
    macros: Option<String>,
    /// Optional distincter, matching the D2 lowering's distincter.
    #[serde(default)]
    distincter: Option<String>,
    /// The request event.
    event: EventInput,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct EventInput {
    /// The action type path, e.g. `["App", "Action"]`.
    action_namespace: Vec<String>,
    /// The action id, verbatim (never re-escaped).
    action_id: String,
    /// The event kind; `request` is the decision kind.
    #[serde(default = "default_kind")]
    kind: String,
    principal: EntityRef,
    resource: EntityRef,
    /// Caller-supplied entities (upstream tier 1: validated against the
    /// augmented schema), in Cedar entity JSON shape.
    #[serde(default)]
    entities: Vec<EntityInput>,
    /// Top-level Cedar request-context fields.
    #[serde(default)]
    context: Map<String, Json>,
}

fn default_kind() -> String {
    "request".to_string()
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct EntityRef {
    #[serde(rename = "type")]
    ty: String,
    id: String,
}

/// A parent edge, in either Cedar entity-reference shape.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct ParentRef {
    #[serde(rename = "type")]
    #[serde(default)]
    ty: Option<String>,
    #[serde(default)]
    id: Option<String>,
    #[serde(default)]
    entity: Option<EntityRef>,
}

impl ParentRef {
    fn resolved(&self) -> Option<(&str, &str)> {
        if let Some(reference) = &self.entity {
            return Some((reference.ty.as_str(), reference.id.as_str()));
        }
        match (&self.ty, &self.id) {
            (Some(ty), Some(id)) => Some((ty.as_str(), id.as_str())),
            _ => None,
        }
    }
}

/// One caller-supplied entity, in Cedar entity JSON shape.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct EntityInput {
    uid: EntityRef,
    #[serde(default)]
    attrs: Map<String, Json>,
    #[serde(default)]
    parents: Vec<ParentRef>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct Outcome {
    /// True when the request reached the upstream decision path.
    ok: bool,
    /// `allow`, `deny`, or null when the event was not a decision point.
    decision: Option<String>,
    /// The determining Dogwood rules, in upstream order.
    determining_rules: Vec<DeterminingRule>,
    /// Upstream evaluation errors (a fail-closed Deny carries its errors here).
    errors: Vec<String>,
    /// True when the envelope could not be executed at all.
    failed: bool,
    /// The failure reason when `failed` is true.
    error: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct DeterminingRule {
    rule_index: usize,
    cedar_policy_id: String,
}

fn failure(message: String) -> Outcome {
    Outcome {
        ok: false,
        decision: None,
        determining_rules: Vec::new(),
        errors: Vec::new(),
        failed: true,
        error: Some(message),
    }
}

fn main() {
    let mut raw = String::new();
    let path = std::env::args().nth(1).unwrap_or_else(|| "-".to_string());
    let read = if path == "-" {
        std::io::stdin().read_to_string(&mut raw).map(|_| ())
    } else {
        std::fs::read_to_string(&path).map(|text| {
            raw = text;
        })
    };
    if let Err(error) = read {
        emit(failure(format!("envelope read: {error}")));
        return;
    }

    let parsed: Json = match serde_json::from_str(&raw) {
        Ok(value) => value,
        Err(error) => {
            emit(failure(format!("envelope parse: {error}")));
            return;
        }
    };

    // A batch envelope runs many cases in one process; a single envelope runs
    // one case. Both write one JSON document to stdout.
    if let Some(cases) = parsed.get("cases").and_then(Json::as_array) {
        let mut results = Vec::new();
        for case in cases {
            let id = case
                .get("id")
                .and_then(Json::as_str)
                .unwrap_or_default()
                .to_string();
            let outcome = match serde_json::from_value::<Envelope>(case.clone()) {
                Ok(envelope) => run(envelope),
                Err(error) => failure(format!("envelope parse: {error}")),
            };
            results.push(json!({ "id": id, "outcome": outcome }));
        }
        let text = serde_json::to_string_pretty(&json!({ "results": results }))
            .expect("batch result serializes");
        println!("{text}");
        return;
    }

    let envelope: Envelope = match serde_json::from_value(parsed) {
        Ok(value) => value,
        Err(error) => {
            emit(failure(format!("envelope parse: {error}")));
            return;
        }
    };
    emit(run(envelope));
}

fn emit(outcome: Outcome) {
    let text = serde_json::to_string_pretty(&outcome).expect("outcome serializes");
    println!("{text}");
}

fn run(envelope: Envelope) -> Outcome {
    // The batch runner reads the id for correlation; the single-envelope
    // runner accepts it as an optional annotation.
    let _case_id = envelope.id.as_deref();
    let service = match build_service(&envelope) {
        Ok(service) => service,
        Err(message) => return failure(message),
    };
    let policy_schema = match PolicySchema::from_cedarschema_str(&envelope.policy_schema) {
        Ok(schema) => schema,
        Err(error) => return failure(format!("policy schema: {error}")),
    };

    let lowered = match &envelope.distincter {
        Some(name) => {
            let distincter: Distincter = match name.parse() {
                Ok(value) => value,
                Err(error) => return failure(format!("distincter: {error}")),
            };
            LoweredPolicySet::from_many_str(
                &[(&envelope.source, distincter)],
                &service,
                &policy_schema,
            )
        }
        None => LoweredPolicySet::from_str(&envelope.source, &service, &policy_schema),
    };
    let lowered = match lowered {
        Ok(lowered) => lowered,
        Err(error) => return failure(format!("lower: {error}")),
    };

    let mut authorizer = Authorizer::new(lowered);
    let event = build_event(&envelope.event);
    match authorizer.is_authorized(&event) {
        Some(response) => {
            let decision = match response.decision() {
                Decision::Allow => "allow",
                Decision::Deny => "deny",
            };
            let determining_rules = response
                .diagnostics()
                .reason()
                .map(|rule| DeterminingRule {
                    rule_index: rule.rule_index,
                    cedar_policy_id: rule.cedar_policy_id.clone(),
                })
                .collect();
            let errors = response
                .diagnostics()
                .errors()
                .map(str::to_string)
                .collect();
            Outcome {
                ok: true,
                decision: Some(decision.to_string()),
                determining_rules,
                errors,
                failed: false,
                error: None,
            }
        }
        None => Outcome {
            ok: true,
            decision: None,
            determining_rules: Vec::new(),
            errors: Vec::new(),
            failed: false,
            error: None,
        },
    }
}

fn build_service(envelope: &Envelope) -> Result<ServiceSchema, String> {
    let mut builder = ServiceSchema::builder();
    if let Some(event_schema) = &envelope.event_schema {
        builder = builder.event_schema_str(event_schema);
    }
    if let Some(macros) = &envelope.macros {
        builder = builder.macros_str(macros);
    }
    builder
        .build()
        .map_err(|error| format!("service schema: {error}"))
}

fn build_event(input: &EventInput) -> Event {
    let namespace: Vec<&str> = input.action_namespace.iter().map(String::as_str).collect();
    let mut builder = Event::builder_for(&namespace, &input.action_id, &input.kind)
        .principal_for(&input.principal.ty, &input.principal.id)
        .resource_for(&input.resource.ty, &input.resource.id);
    for entity in &input.entities {
        let attrs = entity
            .attrs
            .iter()
            .map(|(name, value)| (name.as_str(), json_to_value(value)));
        builder = builder.entity_for(&entity.uid.ty, &entity.uid.id, attrs);
        let parents: Vec<(&str, &str)> = entity
            .parents
            .iter()
            .filter_map(ParentRef::resolved)
            .collect();
        if !parents.is_empty() {
            builder = builder.parents_for(&entity.uid.ty, &entity.uid.id, parents);
        }
    }
    for (name, value) in &input.context {
        builder = builder.request_context_field(name, json_to_value(value));
    }
    builder.build()
}

/// Map a JSON value onto an upstream `Value`. Cedar entity references keep
/// their `{"__entity": {"type": ..., "id": ...}}` shape.
fn json_to_value(value: &Json) -> Value {
    match value {
        Json::Null => Value::Null,
        Json::Bool(flag) => Value::Bool(*flag),
        Json::Number(number) => match number.as_i64() {
            Some(integer) => Value::Int(integer),
            None => Value::Decimal(number.to_string()),
        },
        Json::String(text) => Value::String(text.clone()),
        Json::Array(items) => Value::Array(items.iter().map(json_to_value).collect()),
        Json::Object(fields) => {
            if let Some(reference) = fields.get("__entity")
                && let Some(reference) = reference.as_object()
            {
                let ty = reference
                    .get("type")
                    .and_then(Json::as_str)
                    .unwrap_or_default()
                    .to_string();
                let id = reference
                    .get("id")
                    .and_then(Json::as_str)
                    .unwrap_or_default()
                    .to_string();
                return Value::Entity { ty, id };
            }
            Value::Object(
                fields
                    .iter()
                    .map(|(name, value)| (name.clone(), json_to_value(value)))
                    .collect(),
            )
        }
    }
}
