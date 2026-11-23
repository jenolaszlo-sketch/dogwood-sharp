//! D2 lowering and validation operations and the D3 export operation
//! (ABI v1 operations 2, 3 and 4).
//!
//! Every operation runs the same pipeline against the pinned Dogwood crate:
//! build the service schema, parse the source, build the action schema, lower
//! to Cedar, check the original schema for generated-field collisions, and
//! validate the exact lowered set before an exporting operation reports its
//! artifacts. Operations 2 and 3 are the frozen D2 contract; operation 4 is the
//! D3 export, which additionally reports the exact per-rule Cedar bodies and
//! the trusted-pipeline-owned generated context-field names.
//!
//! See docs/architecture/native-interop.md and
//! docs/architecture/d2-upstream-mapping.md.

mod collision;
mod runtime;

#[cfg(test)]
mod runtime_tests;

use dogwood_language::{
    Error, LoweredPolicySet, ParsedPolicySet, PolicySchema, ProviderDeclarations, ServiceSchema,
    ValidationError, ValidationWarning, Validator,
};
use serde::{Deserialize, Serialize};

use super::{
    D2_RESPONSE_LIMIT, DISTINCTER_LIMIT, Diagnostic, Location, OPERATION_EXPORT, OPERATION_LOWER,
    OPERATION_VALIDATE, SCHEMA_LIMIT, SOURCE_LIMIT, Status, diagnostic, encode_within,
};

/// The frozen operation-2/3 request shape. Operation 4's additive fields are
/// deliberately absent, so using one on operation 2 or 3 is an invalid
/// envelope rather than a silently ignored extension.
#[derive(Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
pub(super) struct LegacyRequest {
    source: String,
    policy_schema: String,
    #[serde(default)]
    event_schema: Option<String>,
    #[serde(default)]
    macros: Option<String>,
    #[serde(default)]
    distincter: Option<String>,
}

impl LegacyRequest {
    fn into_request(self) -> Request {
        Request {
            source: self.source,
            policy_schema: self.policy_schema,
            event_schema: self.event_schema,
            macros: self.macros,
            distincter: self.distincter,
            provider_declarations: None,
            runtime_binding_version: None,
            runtime_binding: None,
        }
    }
}

/// The operation-4 request shape. The original D2 fields keep their exact
/// meaning; the three optional extensions are accepted only by this operation.
#[derive(Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
pub(super) struct Request {
    pub(super) source: String,
    pub(super) policy_schema: String,
    #[serde(default)]
    pub(super) event_schema: Option<String>,
    #[serde(default)]
    pub(super) macros: Option<String>,
    #[serde(default)]
    pub(super) distincter: Option<String>,
    /// Upstream `providers.json` text. `null` and omission both mean no
    /// provider declarations.
    #[serde(default)]
    pub(super) provider_declarations: Option<String>,
    /// A present-but-null version is distinct from omission and is invalid.
    #[serde(default, deserialize_with = "deserialize_present")]
    pub(super) runtime_binding_version: Option<Option<u32>>,
    /// A present-but-null binding is distinct from omission and is invalid.
    #[serde(default, deserialize_with = "deserialize_present")]
    runtime_binding: Option<Option<runtime::BindingInput>>,
}

/// Deserialize a field while retaining the distinction between an omitted
/// field and an explicit JSON `null`.
fn deserialize_present<'de, D, T>(deserializer: D) -> Result<Option<Option<T>>, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Deserialize<'de>,
{
    Option::<T>::deserialize(deserializer).map(Some)
}

impl Request {
    fn within_limits(&self) -> bool {
        self.source.len() <= SOURCE_LIMIT
            && self.policy_schema.len() <= SCHEMA_LIMIT
            && self
                .event_schema
                .as_ref()
                .is_none_or(|text| text.len() <= SCHEMA_LIMIT)
            && self
                .macros
                .as_ref()
                .is_none_or(|text| text.len() <= SCHEMA_LIMIT)
            && self
                .distincter
                .as_ref()
                .is_none_or(|name| name.len() <= DISTINCTER_LIMIT)
            && self
                .provider_declarations
                .as_ref()
                .is_none_or(|text| text.len() <= SCHEMA_LIMIT)
    }

    /// Validate the runtime-binding version/binding pairing. Version 1 is the
    /// only supported version, and its binding is mandatory.
    fn runtime_binding_input(&self) -> Result<Option<&runtime::BindingInput>, Status> {
        match (
            self.runtime_binding_version.as_ref(),
            self.runtime_binding.as_ref(),
        ) {
            (None, None) => Ok(None),
            (Some(Some(1)), Some(Some(binding))) => Ok(Some(binding)),
            (Some(Some(_version)), _) => Err(Status::InvalidEnvelope),
            (Some(None), _) => Err(Status::InvalidEnvelope),
            (_, Some(Some(_))) => Err(Status::InvalidEnvelope),
            (_, Some(None)) => Err(Status::InvalidEnvelope),
        }
    }
}

/// A fatal pipeline error. The record names the stage that stopped and a stable
/// category for the reason, and never admits a partial artifact.
#[derive(Serialize)]
struct StageError {
    stage: &'static str,
    category: &'static str,
    message: String,
    location: Option<Location>,
}

/// One validated rule, in source order. An exporting operation also supplies its
/// expanded source, and the D3 export operation supplies the exact Cedar body
/// for its generated policy id.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Rule {
    rule_index: usize,
    cedar_policy_id: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    source_text: Option<String>,
    /// The exact Cedar body stored under <see cref="cedar_policy_id"/>, rendered
    /// from the compiled `cedar_policy::Policy`. Operation 4 only: it is never
    /// split from the concatenated `cedar_policy_text`, so a consumer can build
    /// an identity-bearing policy set from `(id, body)` pairs. The frozen
    /// operation 3 reply never carries it.
    #[serde(skip_serializing_if = "Option::is_none")]
    cedar_body: Option<String>,
}

/// One validation finding, in upstream order.
#[derive(Serialize)]
struct Finding {
    category: &'static str,
    message: String,
    location: Option<Location>,
}

#[derive(Default, Serialize)]
#[serde(rename_all = "camelCase")]
struct Reply {
    service_schema_built: bool,
    parsed: bool,
    policy_count: Option<usize>,
    parse_diagnostics: Vec<Diagnostic>,
    action_schema_built: bool,
    lowered: bool,
    rule_count: usize,
    self_contained_cedar: bool,
    collision_check_passed: bool,
    collision: Option<collision::Collision>,
    validation_passed: bool,
    validation_errors: Vec<Finding>,
    validation_warnings: Vec<Finding>,
    artifacts_exported: bool,
    stage_error: Option<StageError>,
    /// Present only for a version-1 runtime binding. Legacy operation-4
    /// replies retain their exact key set.
    #[serde(skip_serializing_if = "Option::is_none")]
    runtime_binding: Option<runtime::BindingReply>,
    rules: Vec<Rule>,
    #[serde(flatten)]
    artifacts: Option<Artifacts>,
}

#[derive(Default, Serialize)]
#[serde(rename_all = "camelCase")]
struct Artifacts {
    cedar_policy_text: String,
    cedar_schema_json: String,
    cedar_schema_text: String,
    /// The trusted-pipeline-owned generated context field names: each hoisted
    /// temporal boolean's top-level field name, plus the single reserved
    /// `providers` record when any provider field is hoisted. A consumer must
    /// reject a caller request that carries any of these before dispatch.
    /// Operation 4 only: the frozen operation 3 reply never carries it.
    #[serde(skip_serializing_if = "Option::is_none")]
    generated_context_fields: Option<Vec<String>>,
}

impl Artifacts {
    /// The empty artifact set an exporting operation starts from. The three artifact
    /// strings are reported as empty until the export commits. The D3 export reports
    /// its generated context-field list from the first field onward, so the list is
    /// present even when a later stage fails; the frozen lowering export never has it.
    fn empty(d3: bool) -> Self {
        Self {
            generated_context_fields: d3.then(Vec::new),
            ..Self::default()
        }
    }
}

/// What an operation reports. Operations 2 and 3 are the frozen D2 contract;
/// operation 4 is the D3 export operation, which additionally reports the exact
/// per-rule Cedar bodies and the trusted-pipeline-owned generated context-field
/// names. Every variant runs one pipeline, so no operation can disagree about
/// the lowering itself, and the frozen variants never carry the D3 fields.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Export {
    Validation,
    Lowering,
    D3,
}

pub(super) fn run_legacy(operation: u32, request: LegacyRequest) -> Result<Box<[u8]>, Status> {
    let request = request.into_request();
    run(operation, &request)
}

pub(super) fn run(operation: u32, request: &Request) -> Result<Box<[u8]>, Status> {
    // Pairing is part of the envelope contract, so reject it before any
    // pipeline work or limit handling.
    let runtime_input = request.runtime_binding_input()?;
    if !request.within_limits() {
        return Err(Status::Limit);
    }
    let export = match operation {
        OPERATION_VALIDATE => Export::Validation,
        OPERATION_LOWER => Export::Lowering,
        OPERATION_EXPORT => Export::D3,
        _ => return Err(Status::UnsupportedOperation),
    };
    encode_within(
        &pipeline(request, export, runtime_input)?,
        D2_RESPONSE_LIMIT,
    )
}

fn pipeline(
    request: &Request,
    export: Export,
    runtime_input: Option<&runtime::BindingInput>,
) -> Result<Reply, Status> {
    let mut reply = Reply {
        artifacts: (export != Export::Validation).then(|| Artifacts::empty(export == Export::D3)),
        runtime_binding: runtime_input.map(|_| {
            runtime::BindingReply::failed(
                "runtime binding did not run because the policy pipeline failed".to_owned(),
            )
        }),
        ..Reply::default()
    };

    let service = match build_service(request) {
        Ok(service) => service,
        Err(error) => {
            fail_runtime(
                &mut reply,
                format!("service schema construction failed: {error}"),
            );
            reply.stage_error = Some(stage_error("service-schema", &error, &request.source));
            return Ok(reply);
        }
    };
    reply.service_schema_built = true;

    let parsed = match ParsedPolicySet::parse(&request.source, &service) {
        Ok(parsed) => parsed,
        Err(Error::Parse(errors)) => {
            fail_runtime(&mut reply, "policy parsing failed");
            reply.parse_diagnostics = errors
                .iter()
                .map(|error| {
                    diagnostic(
                        "syntax",
                        error.to_string(),
                        error.location(),
                        &request.source,
                    )
                })
                .collect();
            return Ok(reply);
        }
        Err(Error::Macro(error)) => {
            fail_runtime(&mut reply, "policy macro expansion failed");
            reply.parse_diagnostics = vec![diagnostic(
                "macro",
                error.to_string(),
                error.location(),
                &request.source,
            )];
            return Ok(reply);
        }
        Err(_) => return Err(Status::InternalError),
    };
    reply.parsed = true;
    reply.policy_count = Some(parsed.policy_count());

    // This Dogwood constructor enforces schema nesting. Cedar schema
    // resolution and type checking are part of upstream lowering.
    let schema = match PolicySchema::from_cedarschema_str(&request.policy_schema) {
        Ok(schema) => schema,
        Err(error) => {
            fail_runtime(
                &mut reply,
                format!("action schema construction failed: {error}"),
            );
            reply.stage_error = Some(stage_error("action-schema", &error, &request.source));
            return Ok(reply);
        }
    };
    reply.action_schema_built = true;

    let lowered = match request.distincter.as_deref() {
        Some(distincter) => parsed.lower_with_distincter(&schema, distincter),
        None => parsed.lower(&schema),
    };
    let lowered = match lowered {
        Ok(lowered) => lowered,
        Err(error) => {
            fail_runtime(&mut reply, format!("policy lowering failed: {error}"));
            reply.stage_error = Some(stage_error("lower", &error, &request.source));
            return Ok(reply);
        }
    };
    reply.lowered = true;
    reply.rule_count = lowered.rules().count();
    reply.self_contained_cedar = lowered.is_self_contained_cedar();

    let collision_error = match collision::find(&request.policy_schema, &lowered) {
        Ok(None) => None,
        Ok(Some(collision)) => {
            reply.collision = Some(collision);
            Some((
                "generated-context-field-collision",
                "A generated temporal field is already declared in the original action context."
                    .to_owned(),
            ))
        }
        Err(message) => Some(("internal", message)),
    };
    if let Some((category, message)) = collision_error {
        fail_runtime(&mut reply, message.clone());
        reply.stage_error = Some(StageError {
            stage: "collision-check",
            category,
            message,
            location: None,
        });
        return Ok(reply);
    }
    reply.collision_check_passed = true;

    let validation = Validator::new().validate(&lowered);
    reply.validation_passed = validation.validation_passed();
    reply.validation_errors = validation
        .validation_errors()
        .map(|error| finding(error, &request.source))
        .collect();
    reply.validation_warnings = validation
        .validation_warnings()
        .map(finding_warning)
        .collect();
    if !reply.validation_passed {
        fail_runtime(&mut reply, "policy validation failed");
        return Ok(reply);
    }

    match export {
        Export::Validation => {
            reply.rules = lowered
                .rules()
                .map(|rule| Rule {
                    rule_index: rule.rule_index,
                    cedar_policy_id: rule.cedar_policy_id,
                    source_text: None,
                    cedar_body: None,
                })
                .collect();
        }
        // Commit the three artifact strings and the source mapping together. The
        // D3 export additionally commits each rule's exact Cedar body and the
        // trusted generated context-field names in the same all-or-nothing step.
        Export::Lowering => match export_artifacts(&lowered, &parsed, export) {
            Ok((artifacts, rules)) => {
                reply.artifacts = Some(artifacts);
                reply.rules = rules;
                reply.artifacts_exported = true;
            }
            Err(message) => {
                reply.stage_error = Some(StageError {
                    stage: "artifact-export",
                    category: "internal",
                    message,
                    location: None,
                });
            }
        },
        Export::D3 => {
            match export_artifacts(&lowered, &parsed, export) {
                Ok((artifacts, rules)) => {
                    reply.artifacts = Some(artifacts);
                    reply.rules = rules;
                    reply.artifacts_exported = true;
                }
                Err(message) => {
                    fail_runtime(&mut reply, format!("artifact export failed: {message}"));
                    reply.stage_error = Some(StageError {
                        stage: "artifact-export",
                        category: "internal",
                        message,
                        location: None,
                    });
                    return Ok(reply);
                }
            }

            // The export is now committed. Consume this exact validated lowered
            // set into the upstream runtime; no policy set is rebuilt or cloned.
            if let Some(input) = runtime_input {
                match runtime::bind(lowered, input.clone()) {
                    Ok(binding) => reply.runtime_binding = Some(binding),
                    Err(error) => {
                        reply.runtime_binding = Some(runtime::BindingReply::failed(error));
                    }
                }
            }
        }
    }
    Ok(reply)
}

/// Mark a requested runtime binding as failed without changing any export
/// artifact that was already committed.
fn fail_runtime(reply: &mut Reply, message: impl Into<String>) {
    if let Some(binding) = &mut reply.runtime_binding {
        *binding = runtime::BindingReply::failed(message.into());
    }
}

fn export_artifacts(
    lowered: &LoweredPolicySet,
    parsed: &ParsedPolicySet,
    export: Export,
) -> Result<(Artifacts, Vec<Rule>), String> {
    // The exact per-rule Cedar bodies, keyed by the id the lowered Cedar set
    // stores each policy under. Each body is rendered from the compiled
    // `cedar_policy::Policy` (via its `Display`), never split from the
    // concatenated `cedar_policy_text`, so the pair `(id, body)` is a complete
    // identity-bearing export independent of source order. Only the D3 export
    // renders them; the frozen lowering export keeps its documented shape.
    let bodies = (export == Export::D3).then(|| per_rule_bodies(lowered));

    let mut policies = parsed.policies();
    let mut rules = Vec::with_capacity(policies.len());
    for rule in lowered.rules() {
        let policy = policies
            .next()
            .filter(|policy| policy.index() == rule.rule_index)
            .ok_or("The lowered rule mapping does not match the parsed set.")?;
        let body = match &bodies {
            Some(bodies) => Some(
                bodies
                    .get(&rule.cedar_policy_id)
                    .ok_or_else(|| {
                        "The lowered set does not store the exported policy id.".to_owned()
                    })?
                    .clone(),
            ),
            None => None,
        };
        rules.push(Rule {
            rule_index: rule.rule_index,
            cedar_policy_id: rule.cedar_policy_id,
            source_text: Some(policy.expanded_source()),
            cedar_body: body,
        });
    }
    if policies.len() != 0 {
        return Err("The lowered rule mapping is incomplete.".to_owned());
    }
    let artifacts = Artifacts {
        cedar_policy_text: lowered.as_cedar().to_string(),
        cedar_schema_json: lowered
            .cedar_schema_json()
            .map_err(|error| error.to_string())?,
        cedar_schema_text: lowered
            .cedar_schema_str()
            .map_err(|error| error.to_string())?,
        generated_context_fields: (export == Export::D3).then(|| generated_context_fields(lowered)),
    };
    Ok((artifacts, rules))
}

/// The exact Cedar body for every policy id the lowered Cedar set stores, keyed
/// by that id. Rendered from the compiled policies, never from the concatenated
/// text.
fn per_rule_bodies(lowered: &LoweredPolicySet) -> std::collections::HashMap<String, String> {
    lowered
        .as_cedar()
        .policies()
        .map(|policy| (policy.id().to_string(), policy.to_string()))
        .collect()
}

/// The trusted-pipeline-owned reserved context field names for this lowering:
/// each hoisted temporal boolean is a top-level `context.<id>` attribute, and
/// every provider value is bound under the single reserved `providers` record.
/// Names come from the lowered artifact itself, never from a caller request.
fn generated_context_fields(lowered: &LoweredPolicySet) -> Vec<String> {
    let mut fields: Vec<String> = lowered
        .temporal_fields()
        .map(|field| field.id.clone())
        .collect();
    if lowered.provider_fields().next().is_some() {
        fields.push("providers".to_owned());
    }
    fields
}

fn build_service(request: &Request) -> Result<ServiceSchema, Error> {
    let mut builder = ServiceSchema::builder();
    if let Some(text) = &request.event_schema {
        builder = builder.event_schema_str(text);
    }
    if let Some(text) = &request.macros {
        builder = builder.macros_str(text);
    }
    if let Some(text) = &request.provider_declarations {
        let declarations = ProviderDeclarations::from_json(text).map_err(Error::Providers)?;
        builder = builder.providers(declarations);
    }
    builder.build()
}

/// Map a fatal pipeline error to a stable wrapper category and message.
///
/// Upstream reports every schema problem through `Error::EventSchema` because it
/// parses the action schema while deriving the event schema, so a single
/// `schema` category covers all of them. The stage name is what tells the caller
/// which schema half was being built.
fn stage_error(stage: &'static str, error: &Error, source: &str) -> StageError {
    let (category, location) = match error {
        Error::Parse(errors) => ("policy", errors.iter().next().map(|e| e.location())),
        Error::Macro(error) => ("policy", Some(error.location())),
        Error::Cedarify(error) => ("policy", error.location()),
        Error::Leaf { .. } => ("policy", None),
        Error::ActionSchema(_)
        | Error::CedarSchema(_)
        | Error::PolicySet(_)
        | Error::SchemaSerialize(_)
        | Error::EventSchema(_)
        | Error::Providers(_) => ("schema", None),
        Error::InvalidDistincter(_) | Error::DuplicateDistincter(_) => ("distincter", None),
        _ => ("internal", None),
    };
    StageError {
        stage,
        category,
        message: error.to_string(),
        location: location.and_then(|loc| super::project(loc, source)),
    }
}

/// Project a validation span onto the caller's source. Upstream reports spans
/// against the source it parsed but does not rebase a stripped leading UTF-8
/// BOM, so restore the original coordinates and drop a span that does not
/// address this source.
fn locate(offset: usize, length: usize, parsed_source: &str, source: &str) -> Option<Location> {
    let rebase = if parsed_source == source {
        0
    } else if source.strip_prefix('\u{feff}') == Some(parsed_source) {
        3
    } else {
        return None;
    };
    let offset = offset + rebase;
    (offset <= source.len()
        && (length <= source.len() - offset || (offset == source.len() && length == 1)))
        .then_some(Location { offset, length })
}

fn finding(error: &ValidationError, source: &str) -> Finding {
    let (category, location) = match error {
        ValidationError::Cedar { span, src, .. } => {
            ("cedar", locate(span.offset(), span.len(), src, source))
        }
        ValidationError::Extension {
            code, span, src, ..
        } => (*code, locate(span.offset(), span.len(), src, source)),
        _ => ("other", None),
    };
    Finding {
        category,
        message: error.to_string(),
        location,
    }
}

/// D2 projects warning locations as null. Upstream labels require verification
/// of source provenance and offset rebasing before projection.
fn finding_warning(warning: &ValidationWarning) -> Finding {
    let (category, location) = match warning {
        ValidationWarning::Cedar(_) => ("cedar", None),
        ValidationWarning::Extension { code, .. } => (*code, None),
        _ => ("other", None),
    };
    Finding {
        category,
        message: warning.to_string(),
        location,
    }
}
