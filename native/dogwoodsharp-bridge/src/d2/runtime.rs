//! Trusted runtime binding for operation 4.
//!
//! This module deliberately delegates policy, temporal, and provider execution
//! to the pinned upstream Dogwood authorizer. The bridge only constructs the
//! upstream event/request inputs and supplies trusted provider outcomes; it
//! never reimplementing temporal semantics or provider method chains.

use std::collections::{BTreeMap, BTreeSet};
use std::sync::{Arc, Mutex};

use dogwood_language::{
    AuthorizationDecision, AuthorizationRequest, Authorizer, Decision, Event, LoweredPolicySet,
    PolicyEngine, ProviderRequest, ProviderResolver, Value,
    cedar::{PolicySet, Schema},
};
use serde::{Deserialize, Serialize};
use serde_json::Value as Json;

/// The strict operation-4 runtime-binding request.
#[derive(Debug, Clone, Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
pub(super) struct BindingInput {
    pub(super) timestamp: i64,
    pub(super) history: Vec<HistoryEvent>,
    pub(super) providers: Vec<ProviderOutcome>,
    pub(super) current: CurrentEvent,
}

/// One event in the durable history supplied for replay.
#[derive(Debug, Clone, Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
pub(super) struct HistoryEvent {
    pub(super) identity: String,
    pub(super) timestamp: i64,
    pub(super) action_type: String,
    pub(super) action_id: String,
    pub(super) kind: String,
    #[serde(deserialize_with = "deserialize_required_nullable")]
    pub(super) principal: Option<EntityRef>,
    #[serde(deserialize_with = "deserialize_required_nullable")]
    pub(super) resource: Option<EntityRef>,
    pub(super) context_json: String,
    pub(super) logged_json: String,
    pub(super) entities_json: String,
}

/// The current decision event.
#[derive(Debug, Clone, Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
pub(super) struct CurrentEvent {
    pub(super) action_type: String,
    pub(super) action_id: String,
    pub(super) kind: String,
    pub(super) principal: EntityRef,
    pub(super) resource: EntityRef,
    pub(super) context_json: String,
    pub(super) entities_json: String,
    pub(super) logged_json: String,
}

/// A structured entity reference.
#[derive(Debug, Clone, Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct EntityRef {
    #[serde(rename = "type")]
    pub(super) ty: String,
    pub(super) id: String,
}

/// Deserialize a required nullable member.
///
/// Unlike a plain `Option<T>`, serde's usual missing-field default is not
/// installed: omission is an invalid runtime envelope, while an explicit JSON
/// `null` is the valid nullable value.
fn deserialize_required_nullable<'de, D, T>(deserializer: D) -> Result<Option<T>, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Deserialize<'de>,
{
    Option::<T>::deserialize(deserializer)
}

/// The trusted result supplied for one information provider.
#[derive(Debug, Clone, Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
pub(super) struct ProviderOutcome {
    pub(super) name: String,
    pub(super) outcome: ProviderOutcomeKind,
    #[serde(deserialize_with = "deserialize_required_nullable")]
    pub(super) value_json: Option<String>,
    #[serde(deserialize_with = "deserialize_required_nullable")]
    pub(super) message: Option<String>,
}

/// The three permitted provider-outcome states.
#[derive(Debug, Clone, Copy, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub(super) enum ProviderOutcomeKind {
    Value,
    Omitted,
    Failure,
}

/// The operation-4 reply member for a version-1 runtime binding.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct BindingReply {
    version: u8,
    status: BindingStatus,
    context_json: String,
    error: Option<String>,
}

#[derive(Debug, Clone, Copy, Serialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub(super) enum BindingStatus {
    Bound,
    Failed,
}

impl BindingReply {
    pub(super) fn bound(context_json: String) -> Self {
        Self {
            version: 1,
            status: BindingStatus::Bound,
            context_json,
            error: None,
        }
    }

    pub(super) fn failed(error: String) -> Self {
        Self {
            version: 1,
            status: BindingStatus::Failed,
            context_json: String::new(),
            error: Some(error),
        }
    }
}

/// Bind one request against the exact validated lowered policy set.
///
/// The lowered set is consumed into a fresh upstream authorizer. History is
/// replayed in the supplied order, then the current decision event is
/// evaluated. The only result retained from the deliberately deny-only policy
/// engine is the upstream-enriched Cedar context it captured.
pub(super) fn bind(lowered: LoweredPolicySet, input: BindingInput) -> Result<BindingReply, String> {
    validate_timeline(&input.history, input.timestamp)?;

    let temporal_fields: BTreeSet<String> = lowered
        .temporal_fields()
        .map(|field| field.id.clone())
        .collect();
    let provider_fields: BTreeSet<String> = lowered
        .provider_fields()
        .map(|field| field.id.clone())
        .collect();
    let expected_providers: BTreeSet<String> = lowered
        .provider_fields()
        .map(|field| field.invocation.key())
        .collect();
    if let Some(field) = lowered
        .provider_fields()
        .find(|field| !field.methods.is_empty())
    {
        return Err(format!(
            "trusted provider `{}` has a method chain; trusted outcomes do not supply provider method bodies",
            field.invocation.key()
        ));
    }
    let mut reserved_fields = temporal_fields.iter().cloned().collect::<Vec<_>>();
    if !provider_fields.is_empty() {
        reserved_fields.push("providers".to_owned());
    }
    validate_reserved_contexts(&input, &reserved_fields)?;

    let resolver = TrustedProviderResolver::new(input.providers, expected_providers)?;

    if !lowered.is_decision_kind(&input.current.kind) {
        return Err(format!(
            "current event kind `{}` is not a decision kind",
            input.current.kind
        ));
    }

    let capture = CapturePolicyEngine::default();
    let captured_context = capture.slot();
    let mut authorizer = Authorizer::builder(lowered)
        .policy_engine(capture)
        .provider_resolver(resolver)
        .build()
        .map_err(|error| format!("authorizer build failed: {error}"))?;

    for history in &input.history {
        let event = build_event(EventInput {
            timestamp: history.timestamp,
            action_type: &history.action_type,
            action_id: &history.action_id,
            kind: &history.kind,
            principal: history.principal.as_ref(),
            resource: history.resource.as_ref(),
            context_json: &history.context_json,
            logged_json: &history.logged_json,
            entities_json: &history.entities_json,
            derive_current_input: false,
        })
        .map_err(|error| format!("history event `{}` is malformed: {error}", history.identity))?;

        if let Some(response) = authorizer.is_authorized(&event) {
            let errors: Vec<String> = response.diagnostics().errors().map(str::to_owned).collect();
            if !errors.is_empty() {
                return Err(format!(
                    "history event `{}` failed upstream evaluation: {}",
                    history.identity,
                    errors.join("; ")
                ));
            }
        }
    }

    let current = build_event(EventInput {
        timestamp: input.timestamp,
        action_type: &input.current.action_type,
        action_id: &input.current.action_id,
        kind: &input.current.kind,
        principal: Some(&input.current.principal),
        resource: Some(&input.current.resource),
        context_json: &input.current.context_json,
        logged_json: &input.current.logged_json,
        entities_json: &input.current.entities_json,
        derive_current_input: true,
    })
    .map_err(|error| format!("current event is malformed: {error}"))?;

    // A history decision event may also have reached the capture engine. Only
    // the current decision's context is authoritative.
    {
        let mut slot = captured_context
            .lock()
            .map_err(|_| "captured-context lock is poisoned".to_owned())?;
        *slot = None;
    }

    let response = authorizer
        .is_authorized(&current)
        .ok_or_else(|| "current event did not reach the upstream decision path".to_owned())?;
    let errors: Vec<String> = response.diagnostics().errors().map(str::to_owned).collect();
    if !errors.is_empty() {
        return Err(format!(
            "current event failed upstream evaluation: {}",
            errors.join("; ")
        ));
    }

    let context = {
        let slot = captured_context
            .lock()
            .map_err(|_| "captured-context lock is poisoned".to_owned())?;
        slot.clone()
    };
    let context = context
        .ok_or_else(|| "upstream did not produce a readable Cedar request context".to_owned())?;
    validate_captured_context(&context, &temporal_fields, &provider_fields)?;
    let context_json = serde_json::to_string(&context)
        .map_err(|error| format!("captured context serialization failed: {error}"))?;

    Ok(BindingReply::bound(context_json))
}

/// Validate the trusted event timeline before any upstream state is created.
fn validate_timeline(history: &[HistoryEvent], current_timestamp: i64) -> Result<(), String> {
    let mut identities = BTreeSet::new();
    let mut previous_timestamp = None;

    for event in history {
        if event.identity.trim().is_empty() {
            return Err("history event identity must not be empty".to_owned());
        }
        if !identities.insert(event.identity.clone()) {
            return Err(format!(
                "history event identity `{}` is not unique",
                event.identity
            ));
        }
        if previous_timestamp.is_some_and(|previous| event.timestamp <= previous) {
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
        previous_timestamp = Some(event.timestamp);
    }

    Ok(())
}

/// Reject caller context that tries to supply a trusted-pipeline-owned field.
/// The managed adapter performs the same check before dispatch; this native
/// check keeps the ABI boundary independently fail-closed.
fn validate_reserved_contexts(
    input: &BindingInput,
    reserved_fields: &[String],
) -> Result<(), String> {
    for event in &input.history {
        validate_reserved_context(
            &event.context_json,
            reserved_fields,
            &format!("history event `{}`", event.identity),
        )?;
    }
    validate_reserved_context(
        &input.current.context_json,
        reserved_fields,
        "current event",
    )
}

fn validate_reserved_context(
    context_json: &str,
    reserved_fields: &[String],
    label: &str,
) -> Result<(), String> {
    if reserved_fields.is_empty() {
        return Ok(());
    }
    let context: Json = serde_json::from_str(context_json)
        .map_err(|error| format!("{label} contextJson is invalid JSON: {error}"))?;
    let Some(object) = context.as_object() else {
        return Err(format!("{label} contextJson must contain a JSON object"));
    };
    if let Some(field) = reserved_fields
        .iter()
        .find(|field| object.contains_key(field.as_str()))
    {
        return Err(format!(
            "{label} contextJson carries the reserved generated field `{field}`"
        ));
    }
    Ok(())
}

/// Validate the exact fields the upstream runtime was required to bind. This
/// checks identity as well as value shape: a missing or extra provider field
/// cannot silently become an incomplete trusted context.
fn validate_captured_context(
    context: &Json,
    temporal_fields: &BTreeSet<String>,
    provider_fields: &BTreeSet<String>,
) -> Result<(), String> {
    let object = context
        .as_object()
        .ok_or_else(|| "upstream captured context is not a JSON object".to_owned())?;
    for field in temporal_fields {
        match object.get(field) {
            Some(Json::Bool(_)) => {}
            Some(_) => {
                return Err(format!(
                    "upstream captured temporal field `{field}` is not a Boolean"
                ));
            }
            None => {
                return Err(format!("upstream did not bind temporal field `{field}`"));
            }
        }
    }

    if !provider_fields.is_empty() {
        let providers = object
            .get("providers")
            .and_then(Json::as_object)
            .ok_or_else(|| "upstream did not bind the providers record".to_owned())?;
        let captured = providers
            .keys()
            .map(String::as_str)
            .collect::<BTreeSet<_>>();
        let expected = provider_fields
            .iter()
            .map(String::as_str)
            .collect::<BTreeSet<_>>();
        if captured != expected {
            return Err(format!(
                "upstream provider field identities differ: expected {:?}, captured {:?}",
                expected, captured
            ));
        }
    }

    Ok(())
}

/// The structured fields needed to construct one upstream event.
struct EventInput<'a> {
    timestamp: i64,
    action_type: &'a str,
    action_id: &'a str,
    kind: &'a str,
    principal: Option<&'a EntityRef>,
    resource: Option<&'a EntityRef>,
    context_json: &'a str,
    logged_json: &'a str,
    entities_json: &'a str,
    derive_current_input: bool,
}

/// Build one upstream event from the bridge's structured runtime request.
fn build_event(input: EventInput<'_>) -> Result<Event, String> {
    let namespace = parse_action_type(input.action_type)?;
    let namespace_refs: Vec<&str> = namespace.iter().map(String::as_str).collect();
    let context = parse_object(input.context_json, "contextJson")?;
    let mut logged = parse_object(input.logged_json, "loggedJson")?;
    let entities = parse_entities(input.entities_json)?;

    if input.derive_current_input
        && input.logged_json.is_empty()
        && let Some(input) = context.get("input")
    {
        let Value::Object(_) = input else {
            return Err("contextJson input must be an object".to_owned());
        };
        logged.insert("input".to_owned(), input.clone());
    }

    let mut builder = Event::builder_for(&namespace_refs, input.action_id, input.kind);
    for (name, value) in &logged {
        builder = builder.logged_field(name, value.clone());
    }
    for (name, value) in &context {
        builder = builder.request_context_field(name, value.clone());
    }
    for entity in &entities {
        let attributes = entity
            .attrs
            .iter()
            .map(|(name, value)| (name.as_str(), value.clone()));
        builder = builder.entity_for(&entity.ty, &entity.id, attributes);
        if !entity.parents.is_empty() {
            let parents = entity
                .parents
                .iter()
                .map(|(ty, id)| (ty.as_str(), id.as_str()));
            builder = builder.parents_for(&entity.ty, &entity.id, parents);
        }
    }

    // Apply the scope aliases last so a logged record cannot replace the
    // principal/resource carried by the event's actual scope.
    if let Some(principal) = input.principal {
        builder = builder.principal_for(&principal.ty, &principal.id);
    }
    if let Some(resource) = input.resource {
        builder = builder.resource_for(&resource.ty, &resource.id);
    }

    Ok(builder.timestamp(input.timestamp).build())
}

/// Parse a qualified action type into the namespace expected by upstream.
fn parse_action_type(action_type: &str) -> Result<Vec<String>, String> {
    if action_type == "Action" {
        return Ok(Vec::new());
    }

    let parts: Vec<&str> = action_type.split("::").collect();
    if parts.len() < 2 || parts.last() != Some(&"Action") {
        return Err(format!(
            "actionType `{action_type}` must be `Action` or a qualified action type ending in `Action`"
        ));
    }
    Ok(parts.into_iter().map(str::to_owned).collect())
}

/// Parse a JSON object into upstream values. Empty text means an empty object.
fn parse_object(text: &str, label: &str) -> Result<BTreeMap<String, Value>, String> {
    let value = if text.is_empty() {
        Json::Object(serde_json::Map::new())
    } else {
        serde_json::from_str(text).map_err(|error| format!("{label} is invalid JSON: {error}"))?
    };
    let object = value
        .as_object()
        .ok_or_else(|| format!("{label} must contain a JSON object"))?;
    Ok(object
        .iter()
        .map(|(name, value)| (name.clone(), json_to_value(value)))
        .collect())
}

/// One parsed entity from the Cedar entity-JSON array.
struct ParsedEntity {
    ty: String,
    id: String,
    attrs: BTreeMap<String, Value>,
    parents: Vec<(String, String)>,
}

/// Parse Cedar entity JSON into the entity shape accepted by upstream events.
fn parse_entities(text: &str) -> Result<Vec<ParsedEntity>, String> {
    let value = if text.is_empty() {
        Json::Array(Vec::new())
    } else {
        serde_json::from_str(text)
            .map_err(|error| format!("entitiesJson is invalid JSON: {error}"))?
    };
    let array = value
        .as_array()
        .ok_or_else(|| "entitiesJson must contain a JSON array".to_owned())?;

    let mut entities = Vec::with_capacity(array.len());
    let mut seen = BTreeSet::new();
    for (index, value) in array.iter().enumerate() {
        let object = value
            .as_object()
            .ok_or_else(|| format!("entity {index} must be a JSON object"))?;
        if let Some(field) = object
            .keys()
            .find(|field| !matches!(field.as_str(), "uid" | "attrs" | "parents"))
        {
            return Err(format!("entity {index} has unsupported field `{field}`"));
        }

        let uid = object
            .get("uid")
            .ok_or_else(|| format!("entity {index} is missing uid"))?;
        let (ty, id) = parse_entity_ref(uid, &format!("entity {index} uid"))?;
        if !seen.insert((ty.clone(), id.clone())) {
            return Err(format!("entity {index} has duplicate uid `{ty}::{id}`"));
        }

        let attrs = object
            .get("attrs")
            .ok_or_else(|| format!("entity {index} is missing attrs"))?
            .as_object()
            .ok_or_else(|| format!("entity {index} attrs must be a JSON object"))?;
        let parents = object
            .get("parents")
            .ok_or_else(|| format!("entity {index} is missing parents"))?
            .as_array()
            .ok_or_else(|| format!("entity {index} parents must be a JSON array"))?;

        entities.push(ParsedEntity {
            ty,
            id,
            attrs: attrs
                .iter()
                .map(|(name, value)| (name.clone(), json_to_value(value)))
                .collect(),
            parents: parents
                .iter()
                .enumerate()
                .map(|(parent_index, parent)| {
                    parse_entity_ref(parent, &format!("entity {index} parent {parent_index}"))
                })
                .collect::<Result<Vec<_>, _>>()?,
        });
    }
    Ok(entities)
}

/// Parse either explicit (`__entity`) or implicit entity-reference JSON.
fn parse_entity_ref(value: &Json, label: &str) -> Result<(String, String), String> {
    let object = value
        .as_object()
        .ok_or_else(|| format!("{label} must be a JSON object"))?;

    if let Some(explicit) = object.get("__entity") {
        return parse_type_and_id(explicit, label);
    }
    if let Some(wrapped) = object.get("entity") {
        return parse_type_and_id(wrapped, label);
    }
    parse_type_and_id(value, label)
}

/// Parse the `{ "type": ..., "id": ... }` part of an entity reference.
fn parse_type_and_id(value: &Json, label: &str) -> Result<(String, String), String> {
    let object = value
        .as_object()
        .ok_or_else(|| format!("{label} must contain type and id"))?;
    if let Some(field) = object
        .keys()
        .find(|field| !matches!(field.as_str(), "type" | "id"))
    {
        return Err(format!("{label} has unsupported field `{field}`"));
    }
    let ty = object
        .get("type")
        .and_then(Json::as_str)
        .ok_or_else(|| format!("{label} is missing a string type"))?
        .to_owned();
    let id = object
        .get("id")
        .and_then(Json::as_str)
        .ok_or_else(|| format!("{label} is missing a string id"))?
        .to_owned();
    if ty.is_empty() {
        return Err(format!("{label} has an empty type"));
    }
    Ok((ty, id))
}

/// Convert JSON to the pinned upstream runtime-value representation.
///
/// This mirrors upstream's Cedar JSON escape handling: a single-key
/// `__entity` object becomes an entity reference and a single-key decimal
/// `__extn` object becomes a decimal. Other extension functions remain plain
/// records because upstream's runtime `Value` has no corresponding variant.
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

/// A resolver that can only return values explicitly supplied by the trusted
/// runtime request. It always returns `Some`, so upstream never falls back to
/// a declared Rhai implementation.
struct TrustedProviderResolver {
    outcomes: BTreeMap<String, TrustedOutcome>,
}

enum TrustedOutcome {
    Value(Value),
    Omitted(Option<String>),
    Failure(String),
}

impl TrustedProviderResolver {
    fn new(supplied: Vec<ProviderOutcome>, expected: BTreeSet<String>) -> Result<Self, String> {
        let mut outcomes = BTreeMap::new();
        for outcome in supplied {
            if outcome.name.is_empty() {
                return Err("provider outcome name must not be empty".to_owned());
            }
            if !expected.contains(&outcome.name) {
                return Err(format!(
                    "provider outcome names unknown provider `{}`",
                    outcome.name
                ));
            }
            if outcomes.contains_key(&outcome.name) {
                return Err(format!(
                    "provider outcome `{}` is supplied more than once",
                    outcome.name
                ));
            }

            let trusted = match outcome.outcome {
                ProviderOutcomeKind::Value => {
                    if outcome.message.is_some() {
                        return Err(format!(
                            "provider `{}` has a value outcome and a message",
                            outcome.name
                        ));
                    }
                    let value_json = outcome.value_json.ok_or_else(|| {
                        format!(
                            "provider `{}` has a value outcome without valueJson",
                            outcome.name
                        )
                    })?;
                    let value: Json = serde_json::from_str(&value_json).map_err(|error| {
                        format!(
                            "provider `{}` has malformed valueJson: {error}",
                            outcome.name
                        )
                    })?;
                    TrustedOutcome::Value(json_to_value(&value))
                }
                ProviderOutcomeKind::Omitted => {
                    if outcome.value_json.is_some() {
                        return Err(format!(
                            "provider `{}` has an omitted outcome with valueJson",
                            outcome.name
                        ));
                    }
                    TrustedOutcome::Omitted(outcome.message)
                }
                ProviderOutcomeKind::Failure => {
                    if outcome.value_json.is_some() {
                        return Err(format!(
                            "provider `{}` has a failure outcome with valueJson",
                            outcome.name
                        ));
                    }
                    let message = outcome.message.ok_or_else(|| {
                        format!(
                            "provider `{}` has a failure outcome without message",
                            outcome.name
                        )
                    })?;
                    TrustedOutcome::Failure(message)
                }
            };
            outcomes.insert(outcome.name, trusted);
        }

        for name in &expected {
            if !outcomes.contains_key(name) {
                return Err(format!("provider `{name}` has no trusted outcome"));
            }
        }

        Ok(Self { outcomes })
    }
}

impl ProviderResolver for TrustedProviderResolver {
    fn resolve(&self, request: ProviderRequest<'_>) -> Option<Result<Value, String>> {
        let name = request.key();
        let result = match self.outcomes.get(&name) {
            Some(TrustedOutcome::Value(value)) => Ok(value.clone()),
            Some(TrustedOutcome::Omitted(message)) => Err(match message {
                Some(message) => {
                    format!("trusted provider outcome for `{name}` was omitted: {message}")
                }
                None => format!("trusted provider outcome for `{name}` was omitted"),
            }),
            Some(TrustedOutcome::Failure(message)) => {
                Err(format!("trusted provider `{name}` failed: {message}"))
            }
            None => Err(format!("trusted provider `{name}` has no supplied outcome")),
        };
        Some(result)
    }
}

/// A deny-only upstream policy engine that captures the fully enriched Cedar
/// request context built by upstream.
#[derive(Clone, Default)]
struct CapturePolicyEngine {
    context: Arc<Mutex<Option<Json>>>,
}

impl CapturePolicyEngine {
    fn slot(&self) -> Arc<Mutex<Option<Json>>> {
        Arc::clone(&self.context)
    }
}

impl PolicyEngine for CapturePolicyEngine {
    fn prepare(
        &mut self,
        _policies: &PolicySet,
        _schema: &Schema,
    ) -> Result<(), dogwood_language::Error> {
        Ok(())
    }

    fn is_authorized(&self, request: AuthorizationRequest<'_>) -> AuthorizationDecision {
        let captured = request
            .request
            .context()
            .and_then(|context| context.to_json_value().ok());
        let Some(captured) = captured else {
            return AuthorizationDecision {
                decision: Decision::Deny,
                determining_policy_ids: Vec::new(),
                errors: vec![
                    "upstream did not produce a readable Cedar request context".to_owned(),
                ],
            };
        };
        match self.context.lock() {
            Ok(mut slot) => *slot = Some(captured),
            Err(_) => {
                return AuthorizationDecision {
                    decision: Decision::Deny,
                    determining_policy_ids: Vec::new(),
                    errors: vec!["captured-context lock is poisoned".to_owned()],
                };
            }
        }

        AuthorizationDecision {
            decision: Decision::Deny,
            determining_policy_ids: Vec::new(),
            errors: Vec::new(),
        }
    }
}
