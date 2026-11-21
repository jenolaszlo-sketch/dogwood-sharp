//! D2 lowering and validation operations (ABI v1 operations 2 and 3).
//!
//! Both operations run the same pipeline against the pinned Dogwood crate:
//! build the service schema, parse the source, build the action schema, lower
//! to Cedar, check the original schema for generated-field collisions, and
//! validate the exact lowered set before operation 3 exports its artifacts.
//!
//! See docs/architecture/native-interop.md and
//! docs/architecture/d2-upstream-mapping.md.

mod collision;

use dogwood_language::{
    Error, LoweredPolicySet, ParsedPolicySet, PolicySchema, ServiceSchema, ValidationError,
    ValidationWarning, Validator,
};
use serde::{Deserialize, Serialize};

use super::{
    D2_RESPONSE_LIMIT, DISTINCTER_LIMIT, Diagnostic, Location, OPERATION_LOWER, OPERATION_VALIDATE,
    SCHEMA_LIMIT, SOURCE_LIMIT, Status, diagnostic, encode_within,
};

/// The shared D2 request shape. Every D2 operation reads the same fields, so a
/// caller cannot construct a request that is meaningful for one operation and
/// meaningless for another.
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

/// One validated rule, in source order. Export also supplies its expanded
/// source and the exact Cedar body for its generated policy id.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Rule {
    rule_index: usize,
    cedar_policy_id: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    source_text: Option<String>,
    /// The exact Cedar body stored under <see cref="cedar_policy_id"/>, rendered
    /// from the compiled `cedar_policy::Policy`. It is never split from the
    /// concatenated `cedar_policy_text`, so a consumer can build an
    /// identity-bearing policy set from `(id, body)` pairs.
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
    generated_context_fields: Vec<String>,
}

pub(super) fn run(operation: u32, request: &Request) -> Result<Box<[u8]>, Status> {
    if !request.within_limits() {
        return Err(Status::Limit);
    }
    let export = match operation {
        OPERATION_VALIDATE => false,
        OPERATION_LOWER => true,
        _ => return Err(Status::UnsupportedOperation),
    };
    encode_within(&pipeline(request, export)?, D2_RESPONSE_LIMIT)
}

fn pipeline(request: &Request, export: bool) -> Result<Reply, Status> {
    let mut reply = Reply {
        artifacts: export.then(Artifacts::default),
        ..Reply::default()
    };

    let service = match build_service(request) {
        Ok(service) => service,
        Err(error) => {
            reply.stage_error = Some(stage_error("service-schema", &error, &request.source));
            return Ok(reply);
        }
    };
    reply.service_schema_built = true;

    let parsed = match ParsedPolicySet::parse(&request.source, &service) {
        Ok(parsed) => parsed,
        Err(Error::Parse(errors)) => {
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
        return Ok(reply);
    }

    if export {
        // Commit all three strings and the source mapping together.
        match export_artifacts(&lowered, &parsed) {
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
        }
    } else {
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
    Ok(reply)
}

fn export_artifacts(
    lowered: &LoweredPolicySet,
    parsed: &ParsedPolicySet,
) -> Result<(Artifacts, Vec<Rule>), String> {
    // The exact per-rule Cedar bodies, keyed by the id the lowered Cedar set
    // stores each policy under. Each body is rendered from the compiled
    // `cedar_policy::Policy` (via its `Display`), never split from the
    // concatenated `cedar_policy_text`, so the pair `(id, body)` is a complete
    // identity-bearing export independent of source order.
    let mut bodies = std::collections::HashMap::new();
    for policy in lowered.as_cedar().policies() {
        bodies.insert(policy.id().to_string(), policy.to_string());
    }

    let mut policies = parsed.policies();
    let mut rules = Vec::with_capacity(policies.len());
    for rule in lowered.rules() {
        let policy = policies
            .next()
            .filter(|policy| policy.index() == rule.rule_index)
            .ok_or("The lowered rule mapping does not match the parsed set.")?;
        let body = bodies
            .get(&rule.cedar_policy_id)
            .ok_or_else(|| "The lowered set does not store the exported policy id.".to_owned())?;
        rules.push(Rule {
            rule_index: rule.rule_index,
            cedar_policy_id: rule.cedar_policy_id,
            source_text: Some(policy.expanded_source()),
            cedar_body: Some(body.clone()),
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
        generated_context_fields: generated_context_fields(lowered),
    };
    Ok((artifacts, rules))
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
        | Error::EventSchema(_) => ("schema", None),
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
