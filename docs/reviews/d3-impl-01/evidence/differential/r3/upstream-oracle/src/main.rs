//! DOGWOOD-SHARP-D3-IMPL-01-R3 upstream Dogwood authorizer oracle.
//!
//! Executes each envelope through the actual pinned upstream public path:
//! `ServiceSchema` -> `PolicySchema` -> `LoweredPolicySet` -> `Validator` ->
//! `Authorizer` -> `Event` -> `is_authorized`. The validation phase mirrors the
//! pinned upstream CLI's `validate` operation and the D2 lowering pipeline: a
//! policy set that does not type-check against its augmented schema (including
//! Cedar's extension-function argument validation) fails at lowering, exactly
//! as it does for D3. The optional trusted-runtime inputs use the upstream
//! `InMemoryTemporalEngine` in global mode and a deterministic
//! `ProviderResolver`; no Cedar-to-Cedar comparison or mock policy engine is
//! used here.
//!
//! Test tooling only: never a production execution path.
//!
//! Usage: `dogwoodsharp-upstream-oracle <envelope.json>` or
//! `dogwoodsharp-upstream-oracle -` to read the envelope from stdin.

use std::collections::{BTreeMap, BTreeSet};
use std::io::Read;
use std::sync::{Arc, Mutex};

use dogwood_language::cedar::Schema;
use dogwood_language::{
    Authorizer, Decision, Distincter, Event, EventSignature, ExtensionId, InMemoryTemporalEngine,
    LoweredPolicySet, PartitionKey, PolicySchema, ProviderDeclarations, ProviderRequest,
    ProviderResolver, ServiceSchema, TemporalBindings, TemporalEngine, TemporalField, Validator,
    Value,
};
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value as Json, json};

/// One upstream request plus its optional trusted temporal/provider inputs.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Envelope {
    /// Case identifier for batch mode.
    #[serde(default)]
    id: Option<String>,
    source: String,
    policy_schema: String,
    #[serde(default)]
    event_schema: Option<String>,
    #[serde(default)]
    macros: Option<String>,
    #[serde(default)]
    distincter: Option<String>,
    /// Provider declarations as the upstream providers.json JSON document.
    #[serde(default)]
    provider_declarations: Option<String>,
    /// Deterministic outcomes for every provider used by the lowered set.
    #[serde(default)]
    provider_outcomes: Vec<ProviderOutcomeInput>,
    event: EventInput,
    /// Complete, ordered trusted history before the current decision.
    #[serde(default)]
    history: Vec<HistoryEventInput>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct EventInput {
    #[serde(default)]
    timestamp: i64,
    action_namespace: Vec<String>,
    action_id: String,
    #[serde(default = "default_kind")]
    kind: String,
    #[serde(default)]
    principal: Option<EntityRef>,
    #[serde(default)]
    resource: Option<EntityRef>,
    #[serde(default)]
    entities: Vec<EntityInput>,
    /// Raw entity JSON, used only by malformed-input controls.
    #[serde(default)]
    entities_json: Option<String>,
    #[serde(default)]
    context: Map<String, Json>,
    /// Raw context JSON, used only by malformed-input controls.
    #[serde(default)]
    context_json: Option<String>,
    /// The durable logged record for this event.
    #[serde(default)]
    logged: Map<String, Json>,
    /// Raw logged JSON, used only by malformed-input controls.
    #[serde(default)]
    logged_json: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct HistoryEventInput {
    identity: String,
    timestamp: i64,
    action_namespace: Vec<String>,
    action_id: String,
    #[serde(default = "default_kind")]
    kind: String,
    #[serde(default)]
    principal: Option<EntityRef>,
    #[serde(default)]
    resource: Option<EntityRef>,
    #[serde(default)]
    entities: Vec<EntityInput>,
    #[serde(default)]
    entities_json: Option<String>,
    #[serde(default)]
    context: Map<String, Json>,
    #[serde(default)]
    context_json: Option<String>,
    #[serde(default)]
    logged: Map<String, Json>,
    #[serde(default)]
    logged_json: Option<String>,
}

fn default_kind() -> String {
    "request".to_string()
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct EntityRef {
    #[serde(rename = "type")]
    ty: String,
    id: String,
}

/// A parent edge, in either Cedar entity-reference shape.
#[derive(Debug, Clone, Deserialize)]
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
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct EntityInput {
    uid: EntityRef,
    #[serde(default)]
    attrs: Map<String, Json>,
    #[serde(default)]
    parents: Vec<ParentRef>,
}

#[derive(Debug, Clone, Copy, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
enum ProviderOutcomeKind {
    Value,
    Omitted,
    Failure,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct ProviderOutcomeInput {
    name: String,
    outcome: ProviderOutcomeKind,
    #[serde(default)]
    value: Option<Json>,
    #[serde(default)]
    message: Option<String>,
}

#[derive(Debug, Clone)]
enum TrustedProviderOutcome {
    Value(Value),
    Omitted,
    Failure(String),
}

/// The normalized outcome of one upstream execution.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct Outcome {
    /// `decision`, `non-decision`, or `operation-failure`.
    execution: String,
    /// Canonical failure/evaluation stage used by the managed comparison.
    stage: String,
    /// `allow`, `deny`, or null.
    decision: Option<String>,
    determining_rules: Vec<DeterminingRule>,
    /// Upstream diagnostic text. The normalized categories and policy ids are
    /// the comparison keys; wording remains visible for audit.
    errors: Vec<String>,
    error_categories: Vec<String>,
    policy_error_ids: Vec<String>,
    /// True when failure occurred before a usable policy decision was made.
    pre_decision_failure: bool,
    /// True only when the envelope could not be executed at all.
    failed: bool,
    error: Option<String>,
    trusted_bindings: TrustedBindings,
    history: Vec<HistoryObservation>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct DeterminingRule {
    rule_index: usize,
    cedar_policy_id: String,
}

#[derive(Debug, Serialize, Default)]
#[serde(rename_all = "camelCase")]
struct TrustedBindings {
    temporal: Vec<TemporalBinding>,
    providers: Vec<ProviderBinding>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct TemporalBinding {
    id: String,
    value: bool,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ProviderBinding {
    name: String,
    field_id: String,
    outcome: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    value: Option<Json>,
    #[serde(skip_serializing_if = "Option::is_none")]
    message: Option<String>,
    args: Vec<Json>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct HistoryObservation {
    identity: String,
    timestamp: i64,
    kind: String,
    decision: Option<String>,
    had_errors: bool,
}

fn failure(stage: &str, message: String) -> Outcome {
    Outcome {
        execution: "operation-failure".to_string(),
        stage: stage.to_string(),
        decision: None,
        determining_rules: Vec::new(),
        errors: Vec::new(),
        error_categories: Vec::new(),
        policy_error_ids: Vec::new(),
        pre_decision_failure: true,
        failed: true,
        error: Some(message),
        trusted_bindings: TrustedBindings::default(),
        history: Vec::new(),
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
        emit(failure(
            "request-validation",
            format!("envelope read: {error}"),
        ));
        return;
    }

    let parsed: Json = match serde_json::from_str(&raw) {
        Ok(value) => value,
        Err(error) => {
            emit(failure(
                "request-validation",
                format!("envelope parse: {error}"),
            ));
            return;
        }
    };

    if let Some(cases) = parsed.get("cases").and_then(Json::as_array) {
        let mut results = Vec::new();
        for case in cases {
            let (id, outcome) = match serde_json::from_value::<Envelope>(case.clone()) {
                Ok(envelope) => {
                    let id = envelope.id.clone().unwrap_or_default();
                    (id, run(envelope))
                }
                Err(error) => (
                    case.get("id")
                        .and_then(Json::as_str)
                        .unwrap_or_default()
                        .to_string(),
                    failure("request-validation", format!("envelope parse: {error}")),
                ),
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
            emit(failure(
                "request-validation",
                format!("envelope parse: {error}"),
            ));
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
    let service = match build_service(&envelope) {
        Ok(service) => service,
        Err(message) => return failure("lowering", message),
    };
    let policy_schema = match PolicySchema::from_cedarschema_str(&envelope.policy_schema) {
        Ok(schema) => schema,
        Err(error) => return failure("lowering", format!("policy schema: {error}")),
    };

    let lowered = match &envelope.distincter {
        Some(name) => {
            let distincter: Distincter = match name.parse() {
                Ok(value) => value,
                Err(error) => return failure("lowering", format!("distincter: {error}")),
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
        Err(error) => return failure("lowering", format!("lower: {error}")),
    };

    // Schema-aware validation, identical to the pinned upstream CLI's
    // `validate` operation and to the D2 lowering pipeline's validation phase:
    // a lowered set that does not type-check (extension constructor arguments
    // included) is not a usable policy set.
    let validation = Validator::new().validate(&lowered);
    if !validation.validation_passed() {
        let errors: Vec<String> = validation
            .validation_errors()
            .map(|error| error.to_string())
            .collect();
        return failure("lowering", format!("validation: {}", errors.join("; ")));
    }

    let expected_providers: BTreeMap<String, String> = lowered
        .provider_fields()
        .map(|field| (field.invocation.key(), field.id.clone()))
        .collect();
    let resolver = match DeterministicResolver::new(envelope.provider_outcomes, expected_providers)
    {
        Ok(resolver) => resolver,
        Err(message) => return failure("trusted-binding", message),
    };

    let temporal_observed = Arc::new(Mutex::new(BTreeMap::<ExtensionId, bool>::new()));
    let provider_observed = Arc::new(Mutex::new(Vec::<ProviderBinding>::new()));
    let recording_temporal = RecordingTemporalEngine::new(Arc::clone(&temporal_observed));
    let resolver = resolver.with_observations(Arc::clone(&provider_observed));

    let mut authorizer = match Authorizer::builder(lowered)
        .temporal_engine(recording_temporal)
        .provider_resolver(resolver)
        .build()
    {
        Ok(authorizer) => authorizer,
        Err(error) => return failure("lowering", format!("authorizer build: {error}")),
    };

    let mut history_observations = Vec::new();
    if let Err(message) = validate_history(&envelope.history, envelope.event.timestamp) {
        return failure("trusted-binding", message);
    }
    for history in &envelope.history {
        let event = match build_history_event(history) {
            Ok(event) => event,
            Err(error) => {
                return failure(
                    "request-validation",
                    format!("history event `{}` is malformed: {error}", history.identity),
                );
            }
        };
        let response = authorizer.is_authorized(&event);
        let (decision, had_errors) = match &response {
            Some(response) => {
                let decision = match response.decision() {
                    Decision::Allow => "allow",
                    Decision::Deny => "deny",
                };
                (
                    Some(decision.to_string()),
                    !response.diagnostics().errors().next().is_none(),
                )
            }
            None => (None, false),
        };
        history_observations.push(HistoryObservation {
            identity: history.identity.clone(),
            timestamp: history.timestamp,
            kind: history.kind.clone(),
            decision,
            had_errors,
        });
        if had_errors {
            let errors = response
                .as_ref()
                .map(|response| {
                    response
                        .diagnostics()
                        .errors()
                        .map(str::to_string)
                        .collect::<Vec<_>>()
                })
                .unwrap_or_default();
            return failure(
                "evaluation",
                format!(
                    "history event `{}` failed upstream evaluation: {}",
                    history.identity,
                    errors.join("; ")
                ),
            );
        }
    }

    let event = match build_event(&envelope.event) {
        Ok(event) => event,
        Err(error) => {
            return failure(
                "request-validation",
                format!("current event is malformed: {error}"),
            );
        }
    };
    let response = match authorizer.is_authorized(&event) {
        Some(response) => response,
        None => {
            return Outcome {
                execution: "non-decision".to_string(),
                stage: "evaluation".to_string(),
                decision: None,
                determining_rules: Vec::new(),
                errors: Vec::new(),
                error_categories: Vec::new(),
                policy_error_ids: Vec::new(),
                pre_decision_failure: false,
                failed: false,
                error: None,
                trusted_bindings: observed_bindings(&temporal_observed, &provider_observed),
                history: history_observations,
            };
        }
    };

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
    let errors: Vec<String> = response
        .diagnostics()
        .errors()
        .map(str::to_string)
        .collect();
    let error_categories = normalized_categories(&errors);
    let policy_error_ids = policy_error_ids(&errors);
    let pre_decision_failure = errors.iter().any(|message| {
        let category = categorize_error(message);
        matches!(
            category,
            "entity-conformance" | "provider-evaluation-error" | "temporal-evaluation-error"
        )
    });

    Outcome {
        execution: "decision".to_string(),
        stage: "evaluation".to_string(),
        decision: Some(decision.to_string()),
        determining_rules,
        errors,
        error_categories,
        policy_error_ids,
        pre_decision_failure,
        failed: false,
        error: None,
        trusted_bindings: observed_bindings(&temporal_observed, &provider_observed),
        history: history_observations,
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
    if let Some(declarations) = &envelope.provider_declarations {
        let declarations = ProviderDeclarations::from_json(declarations)
            .map_err(|error| format!("provider declarations: {error}"))?;
        builder = builder.providers(declarations);
    }
    builder
        .build()
        .map_err(|error| format!("service schema: {error}"))
}

fn validate_history(history: &[HistoryEventInput], current_timestamp: i64) -> Result<(), String> {
    let mut identities = BTreeSet::new();
    let mut previous = None;
    for event in history {
        if event.identity.trim().is_empty() {
            return Err("history event identity must not be empty".to_string());
        }
        if !identities.insert(event.identity.clone()) {
            return Err(format!(
                "history event identity `{}` is not unique",
                event.identity
            ));
        }
        if previous.is_some_and(|value| event.timestamp <= value) {
            return Err(format!(
                "history event `{}` timestamp {} is not strictly increasing",
                event.identity, event.timestamp
            ));
        }
        if event.timestamp >= current_timestamp {
            return Err(format!(
                "history event `{}` timestamp {} is not before current timestamp {current_timestamp}",
                event.identity, event.timestamp
            ));
        }
        previous = Some(event.timestamp);
    }
    Ok(())
}

fn build_history_event(input: &HistoryEventInput) -> Result<Event, String> {
    let context = parse_context(input.context_json.as_deref(), &input.context)?;
    let logged = parse_logged(input.logged_json.as_deref(), &input.logged)?;
    let entities = parse_entities(input.entities_json.as_deref(), &input.entities)?;
    build_event_parts(
        input.timestamp,
        &input.action_namespace,
        &input.action_id,
        &input.kind,
        input.principal.as_ref(),
        input.resource.as_ref(),
        &context,
        &logged,
        &entities,
        false,
    )
}

fn build_event(input: &EventInput) -> Result<Event, String> {
    let context = parse_context(input.context_json.as_deref(), &input.context)?;
    let logged = parse_logged(input.logged_json.as_deref(), &input.logged)?;
    let entities = parse_entities(input.entities_json.as_deref(), &input.entities)?;
    build_event_parts(
        input.timestamp,
        &input.action_namespace,
        &input.action_id,
        &input.kind,
        input.principal.as_ref(),
        input.resource.as_ref(),
        &context,
        &logged,
        &entities,
        true,
    )
}

#[allow(clippy::too_many_arguments)]
fn build_event_parts(
    timestamp: i64,
    action_namespace: &[String],
    action_id: &str,
    kind: &str,
    principal: Option<&EntityRef>,
    resource: Option<&EntityRef>,
    context: &Map<String, Json>,
    logged: &Map<String, Json>,
    entities: &[EntityInput],
    derive_current_input: bool,
) -> Result<Event, String> {
    let namespace: Vec<&str> = action_namespace.iter().map(String::as_str).collect();
    let mut logged = logged.clone();
    if derive_current_input
        && logged.is_empty()
        && let Some(input) = context.get("input")
    {
        let Json::Object(_) = input else {
            return Err("context input must be an object".to_string());
        };
        logged.insert("input".to_string(), input.clone());
    }

    let mut builder = Event::builder_for(&namespace, action_id, kind);
    for (name, value) in &logged {
        builder = builder.logged_field(name, json_to_value(value));
    }
    for (name, value) in context {
        builder = builder.request_context_field(name, json_to_value(value));
    }
    for entity in entities {
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

    // Apply scope aliases last so a logged record cannot replace the actual scope.
    if let Some(principal) = principal {
        builder = builder.principal_for(&principal.ty, &principal.id);
    }
    if let Some(resource) = resource {
        builder = builder.resource_for(&resource.ty, &resource.id);
    }

    Ok(builder.timestamp(timestamp).build())
}

fn parse_context(raw: Option<&str>, map: &Map<String, Json>) -> Result<Map<String, Json>, String> {
    match raw {
        None => Ok(map.clone()),
        Some(text) => serde_json::from_str(text).map_err(|error| format!("context JSON: {error}")),
    }
}

fn parse_logged(raw: Option<&str>, map: &Map<String, Json>) -> Result<Map<String, Json>, String> {
    match raw {
        None => Ok(map.clone()),
        Some(text) => serde_json::from_str(text).map_err(|error| format!("logged JSON: {error}")),
    }
}

fn parse_entities(raw: Option<&str>, entities: &[EntityInput]) -> Result<Vec<EntityInput>, String> {
    match raw {
        None => Ok(entities.to_vec()),
        Some(text) => serde_json::from_str(text).map_err(|error| format!("entities JSON: {error}")),
    }
}

/// Map JSON onto the upstream runtime value representation. Decimal uses
/// Cedar's reserved extension encoding; entity references use `__entity`.
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
            if fields.len() == 1 {
                if let Some(reference) = fields.get("__entity").and_then(Json::as_object)
                    && let Some(ty) = reference.get("type").and_then(Json::as_str)
                    && let Some(id) = reference.get("id").and_then(Json::as_str)
                {
                    return Value::Entity {
                        ty: ty.to_owned(),
                        id: id.to_owned(),
                    };
                }
                if let Some(extension) = fields.get("__extn").and_then(Json::as_object)
                    && extension.get("fn").and_then(Json::as_str) == Some("decimal")
                    && let Some(argument) = extension.get("arg").and_then(Json::as_str)
                {
                    return Value::Decimal(argument.to_owned());
                }
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

fn value_to_json(value: &Value) -> Json {
    match value {
        Value::Null => Json::Null,
        Value::Bool(flag) => Json::Bool(*flag),
        Value::Int(integer) => Json::Number((*integer).into()),
        Value::Decimal(decimal) => json!({
            "__extn": { "fn": "decimal", "arg": decimal }
        }),
        Value::String(text) => Json::String(text.clone()),
        Value::Entity { ty, id } => json!({
            "__entity": { "type": ty, "id": id }
        }),
        Value::Array(items) => Json::Array(items.iter().map(value_to_json).collect()),
        Value::Object(members) => Json::Object(
            members
                .iter()
                .map(|(name, value)| (name.clone(), value_to_json(value)))
                .collect(),
        ),
    }
}

/// A deterministic provider resolver. It never declines to a declared Rhai
/// implementation: value, omission, and failure are explicit trusted states.
struct DeterministicResolver {
    outcomes: BTreeMap<String, TrustedProviderOutcome>,
    field_ids: BTreeMap<String, String>,
    observed: Arc<Mutex<Vec<ProviderBinding>>>,
}

impl DeterministicResolver {
    fn new(
        inputs: Vec<ProviderOutcomeInput>,
        expected: BTreeMap<String, String>,
    ) -> Result<Self, String> {
        let mut outcomes = BTreeMap::new();
        for input in inputs {
            if input.name.trim().is_empty() {
                return Err("provider outcome name must not be empty".to_string());
            }
            if !expected.contains_key(&input.name) {
                return Err(format!(
                    "provider outcome names unknown provider `{}`",
                    input.name
                ));
            }
            if outcomes.contains_key(&input.name) {
                return Err(format!(
                    "provider outcome `{}` is supplied more than once",
                    input.name
                ));
            }
            let outcome = match input.outcome {
                ProviderOutcomeKind::Value => {
                    if input.message.is_some() {
                        return Err(format!(
                            "provider `{}` has a value outcome and a message",
                            input.name
                        ));
                    }
                    let value = input
                        .value
                        .ok_or_else(|| format!("provider `{}` has no value", input.name))?;
                    TrustedProviderOutcome::Value(json_to_value(&value))
                }
                ProviderOutcomeKind::Omitted => {
                    if input.value.is_some() || input.message.is_some() {
                        return Err(format!(
                            "provider `{}` has an omitted outcome with a value or message",
                            input.name
                        ));
                    }
                    TrustedProviderOutcome::Omitted
                }
                ProviderOutcomeKind::Failure => {
                    if input.value.is_some() {
                        return Err(format!(
                            "provider `{}` has a failure outcome with a value",
                            input.name
                        ));
                    }
                    let message = input.message.ok_or_else(|| {
                        format!("provider `{}` has no failure message", input.name)
                    })?;
                    TrustedProviderOutcome::Failure(message)
                }
            };
            outcomes.insert(input.name, outcome);
        }
        for name in expected.keys() {
            if !outcomes.contains_key(name) {
                return Err(format!("provider `{name}` has no trusted outcome"));
            }
        }
        Ok(Self {
            outcomes,
            field_ids: expected,
            observed: Arc::new(Mutex::new(Vec::new())),
        })
    }

    fn with_observations(self, observed: Arc<Mutex<Vec<ProviderBinding>>>) -> Self {
        Self { observed, ..self }
    }
}

impl ProviderResolver for DeterministicResolver {
    fn resolve(&self, request: ProviderRequest<'_>) -> Option<Result<Value, String>> {
        let name = request.key();
        let field_id = self.field_ids.get(&name).cloned().unwrap_or_default();
        let args: Vec<Json> = request.args.iter().map(value_to_json).collect();
        let (result, observation) = match self.outcomes.get(&name) {
            Some(TrustedProviderOutcome::Value(value)) => (
                Ok(value.clone()),
                ProviderBinding {
                    name: name.clone(),
                    field_id: field_id.clone(),
                    outcome: "value".to_string(),
                    value: Some(value_to_json(value)),
                    message: None,
                    args,
                },
            ),
            Some(TrustedProviderOutcome::Omitted) => (
                Err(format!("trusted provider outcome for `{name}` was omitted")),
                ProviderBinding {
                    name: name.clone(),
                    field_id: field_id.clone(),
                    outcome: "omitted".to_string(),
                    value: None,
                    message: None,
                    args,
                },
            ),
            Some(TrustedProviderOutcome::Failure(message)) => (
                Err(format!("trusted provider `{name}` failed: {message}")),
                ProviderBinding {
                    name: name.clone(),
                    field_id: field_id.clone(),
                    outcome: "failure".to_string(),
                    value: None,
                    message: Some(message.clone()),
                    args,
                },
            ),
            None => (
                Err(format!("trusted provider `{name}` has no supplied outcome")),
                ProviderBinding {
                    name: name.clone(),
                    field_id: field_id.clone(),
                    outcome: "missing".to_string(),
                    value: None,
                    message: None,
                    args,
                },
            ),
        };
        if let Ok(mut observed) = self.observed.lock() {
            observed.push(observation);
        }
        Some(result)
    }
}

/// The upstream in-memory temporal engine in global mode, with a read-only
/// recording of the normalized per-leaf Booleans it computed.
struct RecordingTemporalEngine {
    inner: InMemoryTemporalEngine,
    observed: Arc<Mutex<BTreeMap<ExtensionId, bool>>>,
}

impl RecordingTemporalEngine {
    fn new(observed: Arc<Mutex<BTreeMap<ExtensionId, bool>>>) -> Self {
        Self {
            inner: InMemoryTemporalEngine::new(),
            observed,
        }
    }
}

impl TemporalEngine for RecordingTemporalEngine {
    fn prepare(
        &mut self,
        leaves: &[TemporalField],
        schema: &Schema,
        events: &[EventSignature],
    ) -> Result<(), dogwood_language::Error> {
        self.inner.prepare(leaves, schema, events)
    }

    fn observe(&mut self, event: &Event) {
        self.inner.observe(event);
    }

    fn evaluate(&mut self) -> Result<TemporalBindings, String> {
        let bindings = self.inner.evaluate()?;
        if let Ok(mut observed) = self.observed.lock() {
            *observed = bindings.clone();
        }
        Ok(bindings)
    }

    fn supports_partitioning(&self) -> bool {
        self.inner.supports_partitioning()
    }

    fn set_partition_keys(&mut self, keys: &[PartitionKey]) {
        self.inner.set_partition_keys(keys);
    }
}

fn observed_bindings(
    temporal: &Arc<Mutex<BTreeMap<ExtensionId, bool>>>,
    providers: &Arc<Mutex<Vec<ProviderBinding>>>,
) -> TrustedBindings {
    let temporal = temporal
        .lock()
        .map(|bindings| {
            bindings
                .iter()
                .map(|(id, value)| TemporalBinding {
                    id: id.clone(),
                    value: *value,
                })
                .collect()
        })
        .unwrap_or_default();
    let providers = providers
        .lock()
        .map(|bindings| bindings.clone())
        .unwrap_or_default();
    TrustedBindings {
        temporal,
        providers,
    }
}

fn categorize_error(message: &str) -> &'static str {
    if message.contains("entity conformance") {
        "entity-conformance"
    } else if message.contains("provider") {
        "provider-evaluation-error"
    } else if message.contains("temporal") {
        "temporal-evaluation-error"
    } else if message.contains("does not have the attribute")
        || message.contains("record does not have the attribute")
    {
        "missing-attribute"
    } else if message.contains("does not exist") {
        "missing-entity"
    } else {
        "policy-evaluation-error"
    }
}

fn normalized_categories(errors: &[String]) -> Vec<String> {
    let mut categories: BTreeSet<String> = errors
        .iter()
        .map(|message| categorize_error(message).to_string())
        .collect();
    categories.remove("policy-evaluation-error");
    if categories.is_empty() && !errors.is_empty() {
        categories.insert("policy-evaluation-error".to_string());
    }
    categories.into_iter().collect()
}

fn policy_error_ids(errors: &[String]) -> Vec<String> {
    let mut ids = BTreeSet::new();
    for message in errors {
        let Some(start) = message.find("policy `") else {
            continue;
        };
        let rest = &message[start + "policy `".len()..];
        if let Some(end) = rest.find('`') {
            ids.insert(rest[..end].to_string());
        }
    }
    ids.into_iter().collect()
}
