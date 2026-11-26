//! DOGWOOD-SHARP-D3-IMPL-01-R5-R1 corrected Dogwood reference oracle.
//!
//! Fork of the R3/R4 upstream differential oracle with one bounded correction:
//! typed ipaddr and datetime request-context values reach the pinned upstream
//! Dogwood authorizer as typed values instead of degrading to plain records at
//! the Dogwood-to-Cedar value boundary (the R5 typed-context root cause).
//!
//! The correction has exactly two parts, each at the layer that owns it:
//!
//! 1. Adapter boundary (this file): the request context JSON is canonicalized
//!    field-by-field through Cedar's own schema-directed JSON authorization
//!    parsing (`Context::from_json_value` with the action schema), which is
//!    where the Cedar documentation places the three-encoding equivalence for
//!    extension-typed values. Only context fields the action's context schema
//!    declares as Cedar extension types are converted; every other field is
//!    forwarded verbatim, so unrelated envelope behavior is unchanged. A
//!    malformed typed value fails the envelope closed at this boundary,
//!    exactly as Cedar's parse stage does.
//! 2. Dogwood value boundary (dogwood-typed-context.patch): the patched
//!    `value_to_expr` honors Cedar's reserved single-key `__extn` record for
//!    the single-argument `ip` and `datetime` constructors instead of
//!    degrading it to a plain record. The constructor argument is validated
//!    eagerly by `Context::from_pairs`, so an invalid argument still fails
//!    closed.
//!
//! Everything else is the pinned upstream path: `ServiceSchema` ->
//! `PolicySchema` -> `LoweredPolicySet` -> `Validator` -> `Authorizer` ->
//! `Event` -> `is_authorized`, including the three-tier entity store, the
//! temporal pipeline and the provider pipeline. The `cedar-policy` dependency
//! performs JSON representation parsing only; no Cedar-to-Cedar comparison or
//! mock policy engine is introduced. History-event contexts and caller entity
//! attributes are outside this correction's scope (the R5 record documents
//! the temporal boundary separately).
//!
//! Test tooling only: never a production execution path.
//!
//! Usage: `dogwoodsharp-corrected-oracle <envelope.json>` or
//! `dogwoodsharp-corrected-oracle -` to read the envelope from stdin.

use std::collections::{BTreeMap, BTreeSet};
use std::io::Read;
use std::str::FromStr;
use std::sync::{Arc, Mutex};

use cedar_policy::Context as CedarJsonContext;
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

#[derive(Debug, Clone, Deserialize)]
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
    let resolver = match DeterministicResolver::new(envelope.provider_outcomes.clone(), expected_providers)
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

    let event = match build_event(&envelope) {
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

fn build_event(envelope: &Envelope) -> Result<Event, String> {
    let input = &envelope.event;
    let context = parse_context(input.context_json.as_deref(), &input.context)?;
    // Corrected-reference adapter boundary: canonicalize the extension-typed
    // request-context fields through Cedar's own schema-directed JSON
    // authorization parsing before the values enter the Dogwood event. See
    // `canonicalize_typed_context`.
    let context = canonicalize_typed_context(envelope, &context)?;
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

// ─── Corrected-reference typed-context adapter boundary ────────────────
//
// Cedar's documented schema-directed JSON parsing is what makes the three
// request-context encodings of an extension-typed value semantically
// equivalent: the explicit `__extn` escape, the implicit `{fn, arg}` escape,
// and the bare constructor argument (docs.cedarpolicy.com, Entities &
// context JSON syntax, "Schema-based parsing"). The pinned Dogwood event
// carries runtime values, not JSON, so the equivalence must be resolved at
// the reference adapter's JSON boundary — exactly the layer where Cedar
// itself resolves it. This section converts only the context fields the
// action's context schema declares as Cedar extension types; every other
// field is forwarded verbatim, so unrelated envelope behavior is unchanged.
//
// The conversion reuses Cedar's own JSON authorization parser
// (`Context::from_json_value`) against a synthesized single-field action
// schema carrying the declared type verbatim. No parsing or validation
// semantics are re-implemented here, and no Cedar evaluation is introduced:
// the canonical `__extn` values this produces are carried into the Dogwood
// event, whose patched `value_to_expr` now preserves the typed value, and
// the decision itself is made by the pinned Dogwood authorizer.

/// Cedar's built-in extension type names (Cedar documentation: "the built-in
/// extension type names (ipaddr, decimal, datetime, and duration) are always
/// available"). These are the types the corrected boundary canonicalizes.
const EXTENSION_TYPE_NAMES: [&str; 4] = ["ipaddr", "datetime", "decimal", "duration"];

/// Canonicalize the extension-typed request-context fields of the current
/// decision event. `Ok(map)` with unchanged non-typed fields, or a typed
/// envelope failure.
fn canonicalize_typed_context(
    envelope: &Envelope,
    context: &Map<String, Json>,
) -> Result<Map<String, Json>, String> {
    let typed = declared_extension_fields(
        &envelope.policy_schema,
        &envelope.event.action_namespace,
        &envelope.event.action_id,
    )?;
    if typed.is_empty() || context.is_empty() {
        return Ok(context.clone());
    }
    let mut canonical = context.clone();
    for (attr, type_text) in &typed {
        let Some(value) = context.get(attr) else {
            // The field is absent: Cedar's own parse of a missing required
            // attribute is handled by the action schema conformance layer
            // exactly as before (registered as ED-05). Nothing to convert.
            continue;
        };
        let converted = canonicalize_field(attr, type_text, value)?;
        canonical.insert(attr.clone(), converted);
    }
    Ok(canonical)
}

/// The synthesized single-field action schema driving one field's
/// canonicalization. Cedar parses `{ "<attr>": <value> }` with the declared
/// type and returns the canonical natural JSON for that field, which — for an
/// extension-typed value — is always the explicit `__extn` escape form.
fn canonicalize_field(attr: &str, type_text: &str, value: &Json) -> Result<Json, String> {
    // Cedar's built-in extension types without a constructor function cannot
    // be produced from JSON at all; a declared `duration` field only ever
    // fails exactly as Cedar's parser fails. Synthesizing the schema anyway
    // and letting Cedar answer keeps the behavior authoritative rather than
    // asserted.
    let mini = format!(
        "namespace RefAdj {{ entity RefP; entity RefR; action refadj appliesTo \
         {{ principal: [RefP], resource: [RefR], context: {{ {attr}: {type_text} }} }}; }}"
    );
    let (schema, _) = Schema::from_cedarschema_str(&mini)
        .map_err(|error| format!("typed context: synthesized schema for `{attr}`: {error}"))?;
    let action = cedar_policy::EntityUid::from_type_name_and_id(
        cedar_policy::EntityTypeName::from_str("RefAdj::Action")
            .map_err(|error| format!("typed context: synthesized action type: {error}"))?,
        cedar_policy::EntityId::from_str("refadj")
            .map_err(|error| format!("typed context: synthesized action id: {error}"))?,
    );
    let document = json!({ attr: value });
    let parsed = CedarJsonContext::from_json_value(document, Some((&schema, &action)))
        .map_err(|error| format!("typed context: {error}"))?;
    let canonical = parsed
        .to_json_value()
        .map_err(|error| format!("typed context: canonical form for `{attr}`: {error}"))?;
    Ok(canonical.get(attr).cloned().unwrap_or(Json::Null))
}

/// The top-level context fields the action's context schema declares as one
/// of Cedar's built-in extension types, as `(attribute, verbatim type text)`
/// pairs. The declaration is read from the natural Cedar schema text; the
/// verbatim type text is reused so the synthesized single-field schema
/// cannot drift from the declared type.
fn declared_extension_fields(
    schema_text: &str,
    action_namespace: &[String],
    action_id: &str,
) -> Result<Vec<(String, String)>, String> {
    // The event's action namespace path is `[Ns..., "Action"]`; the schema
    // namespace is everything before the trailing `Action` component. A
    // single-element path addresses a namespace-less action.
    let namespace = if action_namespace.len() > 1 {
        action_namespace[..action_namespace.len() - 1].join("::")
    } else {
        String::new()
    };
    let body = namespace_body(schema_text, &namespace)?;
    let Some(applies_to) = action_applies_to_block(body, action_id) else {
        return Ok(Vec::new());
    };
    let Some(context_type) = block_field_type(applies_to, "context") else {
        return Ok(Vec::new());
    };
    let mut fields = Vec::new();
    for (attr, attr_type) in record_attr_types(context_type)? {
        let name = attr.trim_end_matches('?').to_string();
        let bare = attr_type.trim();
        if EXTENSION_TYPE_NAMES.contains(&bare) {
            fields.push((name, bare.to_string()));
        }
    }
    Ok(fields)
}

/// The body (between braces) of the `namespace <name> { ... }` block whose
/// name matches, or the whole text for a namespace-less action. The scanner
/// is lexical only: it never interprets schema semantics, it locates text.
fn namespace_body<'a>(schema_text: &'a str, namespace: &str) -> Result<&'a str, String> {
    if namespace.is_empty() {
        return Ok(schema_text);
    }
    let bytes = schema_text.as_bytes();
    let mut cursor = 0usize;
    while let Some(relative) = find_word(schema_text, cursor, "namespace") {
        let mut position = relative + "namespace".len();
        while position < bytes.len() && bytes[position].is_ascii_whitespace() {
            position += 1;
        }
        let name_start = position;
        while position < bytes.len()
            && (bytes[position].is_ascii_alphanumeric()
                || bytes[position] == b'_'
                || bytes[position] == b':')
        {
            position += 1;
        }
        let name = &schema_text[name_start..position];
        while position < bytes.len() && bytes[position].is_ascii_whitespace() {
            position += 1;
        }
        if position >= bytes.len() || bytes[position] != b'{' {
            return Err("schema: namespace block is not braced".to_string());
        }
        let (open, close) =
            find_block(schema_text, position).ok_or("schema: unbalanced namespace braces")?;
        if name == namespace {
            return Ok(&schema_text[open + 1..close]);
        }
        cursor = close + 1;
    }
    Err(format!("schema: namespace `{namespace}` not found"))
}

/// The `appliesTo { ... }` block of the `action <id>` declaration inside the
/// namespace body, if the action declares one. Modifiers such as
/// `action <id> in <Group> appliesTo { ... }` are handled: the scan runs to
/// the end of the declaration (a top-level `;`) and takes the first block
/// whose preceding word is `appliesTo`.
fn action_applies_to_block<'a>(body: &'a str, action_id: &str) -> Option<&'a str> {
    let bytes = body.as_bytes();
    let mut cursor = 0usize;
    while let Some(relative) = find_word(body, cursor, "action") {
        let mut position = relative + "action".len();
        while position < bytes.len() && bytes[position].is_ascii_whitespace() {
            position += 1;
        }
        let id_start = position;
        while position < bytes.len()
            && (bytes[position].is_ascii_alphanumeric() || bytes[position] == b'_')
        {
            position += 1;
        }
        let id = &body[id_start..position];
        if id != action_id {
            cursor = position.max(relative + 1);
            continue;
        }
        let mut scan = position;
        while scan < bytes.len() {
            match bytes[scan] {
                b'{' => {
                    let (open, close) = find_block(body, scan)?;
                    let head = body[position..open].trim_start();
                    if last_word(head) == Some("appliesTo") {
                        return Some(&body[open + 1..close]);
                    }
                    scan = close + 1;
                }
                b';' => return None,
                _ => scan += 1,
            }
        }
        return None;
    }
    None
}

/// The last standalone identifier of a text fragment, if any.
fn last_word(text: &str) -> Option<&str> {
    text.split_whitespace().next_back().map(|word| {
        word.trim_end_matches(|c: char| !c.is_ascii_alphanumeric() && c != '_')
    })
}

/// The verbatim type text declared for `field` inside an appliesTo block
/// body: the text after `field` `:`, either a braced record or an identifier
/// up to the next top-level separator.
fn block_field_type<'a>(block: &'a str, field: &str) -> Option<&'a str> {
    let bytes = block.as_bytes();
    let mut cursor = 0usize;
    while let Some(relative) = find_field_name(block, cursor, field) {
        let mut position = relative + field.len();
        while position < bytes.len() && bytes[position].is_ascii_whitespace() {
            position += 1;
        }
        if position >= bytes.len() || bytes[position] != b':' {
            cursor = position.max(relative + 1);
            continue;
        }
        position += 1;
        while position < bytes.len() && bytes[position].is_ascii_whitespace() {
            position += 1;
        }
        if position < bytes.len() && bytes[position] == b'{' {
            let (open, close) = find_block(block, position)?;
            return Some(&block[open..=close]);
        }
        let start = position;
        while position < bytes.len()
            && bytes[position] != b','
            && bytes[position] != b'}'
            && bytes[position] != b';'
        {
            position += 1;
        }
        return Some(block[start..position].trim());
    }
    None
}

/// The `(attribute, verbatim type text)` pairs of a braced record type text.
/// Splitting is top-level only: nested braces, brackets and angle brackets
/// are respected.
fn record_attr_types(record: &str) -> Result<Vec<(String, String)>, String> {
    let trimmed = record.trim();
    if !(trimmed.starts_with('{') && trimmed.ends_with('}')) {
        return Err(format!("schema: context type `{trimmed}` is not a record"));
    }
    let inner = &trimmed[1..trimmed.len() - 1];
    let bytes = inner.as_bytes();
    let mut parts: Vec<String> = Vec::new();
    let mut depth = 0usize;
    let mut current = String::new();
    for &byte in bytes {
        match byte {
            b'{' | b'[' | b'<' => {
                depth += 1;
                current.push(byte as char);
            }
            b'}' | b']' | b'>' => {
                depth = depth.saturating_sub(1);
                current.push(byte as char);
            }
            b',' if depth == 0 => {
                parts.push(current.clone());
                current.clear();
            }
            _ => current.push(byte as char),
        }
    }
    if !current.trim().is_empty() {
        parts.push(current);
    }
    let mut pairs = Vec::new();
    for part in parts {
        let part = part.trim();
        if part.is_empty() {
            continue;
        }
        let Some(separator) = part.find(':') else {
            return Err(format!("schema: context attribute `{part}` has no type"));
        };
        let name = part[..separator].trim().to_string();
        let attr_type = part[separator + 1..].trim().to_string();
        if name.is_empty() || attr_type.is_empty() {
            return Err(format!("schema: context attribute `{part}` is malformed"));
        }
        pairs.push((name, attr_type));
    }
    Ok(pairs)
}

/// The byte offsets of the braces of the first `{ ... }` block at or after
/// `from`, with proper nesting.
fn find_block(text: &str, from: usize) -> Option<(usize, usize)> {
    let bytes = text.as_bytes();
    let mut depth = 0usize;
    let mut open = None;
    for (offset, byte) in bytes.iter().enumerate().skip(from) {
        match byte {
            b'{' => {
                if depth == 0 {
                    open = Some(offset);
                }
                depth += 1;
            }
            b'}' => {
                depth = depth.saturating_sub(1);
                if depth == 0 {
                    return Some((open?, offset));
                }
            }
            _ => {}
        }
    }
    None
}

/// The byte offset of `word` as a standalone identifier at or after `from`.
fn find_word(text: &str, from: usize, word: &str) -> Option<usize> {
    let bytes = text.as_bytes();
    let mut cursor = from;
    while cursor + word.len() <= bytes.len() {
        let candidate = &text[cursor..cursor + word.len()];
        if candidate == word {
            let before_ok = cursor == 0
                || !bytes[cursor - 1].is_ascii_alphanumeric()
                    && bytes[cursor - 1] != b'_'
                    && bytes[cursor - 1] != b':';
            let after = cursor + word.len();
            let after_ok = after >= bytes.len()
                || !bytes[after].is_ascii_alphanumeric()
                    && bytes[after] != b'_'
                    && bytes[after] != b':';
            if before_ok && after_ok {
                return Some(cursor);
            }
        }
        cursor += 1;
    }
    None
}

/// The byte offset of `word` as a standalone identifier at or after `from`,
/// where the identifier may be followed by `:` (a record attribute name).
/// `find_word` deliberately rejects a trailing `:` so that qualified names
/// such as `App::Action` are never split; record attributes need the
/// opposite rule.
fn find_field_name(text: &str, from: usize, word: &str) -> Option<usize> {
    let bytes = text.as_bytes();
    let mut cursor = from;
    while cursor + word.len() <= bytes.len() {
        let candidate = &text[cursor..cursor + word.len()];
        if candidate == word {
            let before_ok = cursor == 0
                || !bytes[cursor - 1].is_ascii_alphanumeric()
                    && bytes[cursor - 1] != b'_'
                    && bytes[cursor - 1] != b':';
            let after = cursor + word.len();
            let after_ok = after >= bytes.len()
                || !bytes[after].is_ascii_alphanumeric()
                    && bytes[after] != b'_';
            if before_ok && after_ok {
                return Some(cursor);
            }
        }
        cursor += 1;
    }
    None
}

#[cfg(test)]
mod adapter_tests {
    use super::*;

    const IP_SCHEMA: &str =
        "namespace App { entity User; entity Doc; action read appliesTo { principal: [User], resource: [Doc], context: { ip: ipaddr } }; }";

    #[test]
    fn extracts_extension_fields_from_action_context() {
        let fields = declared_extension_fields(IP_SCHEMA, &["App".into(), "Action".into()], "read")
            .expect("extraction succeeds");
        assert_eq!(fields, vec![("ip".to_string(), "ipaddr".to_string())]);
    }

    #[test]
    fn canonicalizes_the_three_encodings_identically() {
        let explicit = serde_json::json!({ "__extn": { "fn": "ip", "arg": "192.168.1.1" } });
        let implicit = serde_json::json!({ "fn": "ip", "arg": "192.168.1.1" });
        let short = serde_json::json!("192.168.1.1");
        let a = canonicalize_field("ip", "ipaddr", &explicit).expect("explicit");
        let b = canonicalize_field("ip", "ipaddr", &implicit).expect("implicit");
        let c = canonicalize_field("ip", "ipaddr", &short).expect("short");
        assert_eq!(a, b);
        assert_eq!(b, c);
        assert_eq!(
            a,
            serde_json::json!({ "__extn": { "fn": "ip", "arg": "192.168.1.1" } })
        );
    }

    #[test]
    fn rejects_an_invalid_typed_argument_at_the_boundary() {
        let invalid = serde_json::json!("not-an-ip");
        let outcome = canonicalize_field("ip", "ipaddr", &invalid);
        assert!(outcome.is_err(), "invalid ip must fail closed at the adapter boundary");
    }

    #[test]
    fn leaves_non_extension_fields_untouched() {
        let long_schema =
            "namespace App { entity User; entity Doc; action read appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }";
        let fields = declared_extension_fields(
            long_schema,
            &["App".into(), "Action".into()],
            "read",
        )
        .expect("extraction succeeds");
        assert!(fields.is_empty());
    }

    #[test]
    fn handles_in_modified_action_declarations() {
        let schema = "namespace App { entity User; entity Doc; action ReadGroup; action read2 in ReadGroup appliesTo { principal: [User], resource: [Doc], context: { price: decimal } }; }";
        let fields = declared_extension_fields(
            schema,
            &["App".into(), "Action".into()],
            "read2",
        )
        .expect("extraction succeeds");
        assert_eq!(fields, vec![("price".to_string(), "decimal".to_string())]);
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
