//! D2 lowering and validation operations (ABI v1 operations 2 and 3).
//!
//! Both operations run the same pipeline against the pinned Dogwood crate:
//! build the service schema, parse the source, build the action schema, lower
//! to Cedar, and validate the lowered set. The wire record keeps every stage's
//! outcome separately so a caller cannot mistake a failed stage for a completed
//! one, and a fatal pipeline error is reported instead of a partial artifact.
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

/// The rule-index to Cedar-policy-id mapping, in source order.
#[derive(Serialize)]
struct RuleRef {
    #[serde(rename = "ruleIndex")]
    rule_index: usize,
    #[serde(rename = "cedarPolicyId")]
    cedar_policy_id: String,
}

/// One lowered rule with its macro-expanded Dogwood source.
#[derive(Serialize)]
struct LoweredRule {
    #[serde(rename = "ruleIndex")]
    rule_index: usize,
    #[serde(rename = "cedarPolicyId")]
    cedar_policy_id: String,
    #[serde(rename = "sourceText")]
    source_text: String,
}

/// One validation finding, in upstream order.
#[derive(Serialize)]
struct Finding {
    category: &'static str,
    message: String,
    location: Option<Location>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ValidateReply {
    parsed: bool,
    policy_count: Option<usize>,
    parse_diagnostics: Vec<Diagnostic>,
    lowered: bool,
    rule_count: usize,
    self_contained_cedar: bool,
    rules: Vec<RuleRef>,
    validation_passed: bool,
    validation_errors: Vec<Finding>,
    validation_warnings: Vec<Finding>,
    #[serde(skip_serializing_if = "Option::is_none")]
    collision: Option<collision::Collision>,
    stage_error: Option<StageError>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct LowerReply {
    parsed: bool,
    policy_count: Option<usize>,
    parse_diagnostics: Vec<Diagnostic>,
    lowered: bool,
    rule_count: usize,
    self_contained_cedar: bool,
    rules: Vec<LoweredRule>,
    cedar_policy_text: String,
    cedar_schema_json: String,
    cedar_schema_text: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    collision: Option<collision::Collision>,
    stage_error: Option<StageError>,
}

/// The stages either operation can reach. A stage that did not run keeps the
/// documented failure value, so `parsed`, `lowered` and `validationPassed` are
/// each meaningful only when the preceding stage succeeded.
struct Pipeline {
    parsed: bool,
    policy_count: Option<usize>,
    parse_diagnostics: Vec<Diagnostic>,
    lowered: bool,
    rule_count: usize,
    self_contained_cedar: bool,
    rules: Vec<RuleRef>,
    lowered_rules: Vec<LoweredRule>,
    cedar_policy_text: String,
    cedar_schema_json: String,
    cedar_schema_text: String,
    validation_passed: bool,
    validation_errors: Vec<Finding>,
    validation_warnings: Vec<Finding>,
    collision: Option<collision::Collision>,
    stage_error: Option<StageError>,
}

impl Pipeline {
    fn empty() -> Self {
        Self {
            parsed: false,
            policy_count: None,
            parse_diagnostics: Vec::new(),
            lowered: false,
            rule_count: 0,
            self_contained_cedar: false,
            rules: Vec::new(),
            lowered_rules: Vec::new(),
            cedar_policy_text: String::new(),
            cedar_schema_json: String::new(),
            cedar_schema_text: String::new(),
            validation_passed: false,
            validation_errors: Vec::new(),
            validation_warnings: Vec::new(),
            collision: None,
            stage_error: None,
        }
    }
}

pub(super) fn run(operation: u32, request: &Request) -> Result<Box<[u8]>, Status> {
    if !request.within_limits() {
        return Err(Status::Limit);
    }
    if operation == OPERATION_VALIDATE && request.distincter.is_some() {
        // Validation does not name hoisted fields, so a distincter has no
        // meaning here. Accepting and dropping it would silently discard a
        // caller's collision-avoidance intent, so reject it instead.
        return Err(Status::InvalidEnvelope);
    }

    let pipeline = pipeline(operation, request)?;
    match operation {
        OPERATION_VALIDATE => encode_within(
            &ValidateReply {
                parsed: pipeline.parsed,
                policy_count: pipeline.policy_count,
                parse_diagnostics: pipeline.parse_diagnostics,
                lowered: pipeline.lowered,
                rule_count: pipeline.rule_count,
                self_contained_cedar: pipeline.self_contained_cedar,
                rules: pipeline.rules,
                validation_passed: pipeline.validation_passed,
                validation_errors: pipeline.validation_errors,
                validation_warnings: pipeline.validation_warnings,
                collision: pipeline.collision,
                stage_error: pipeline.stage_error,
            },
            D2_RESPONSE_LIMIT,
        ),
        OPERATION_LOWER => encode_within(
            &LowerReply {
                parsed: pipeline.parsed,
                policy_count: pipeline.policy_count,
                parse_diagnostics: pipeline.parse_diagnostics,
                lowered: pipeline.lowered,
                rule_count: pipeline.rule_count,
                self_contained_cedar: pipeline.self_contained_cedar,
                rules: pipeline.lowered_rules,
                cedar_policy_text: pipeline.cedar_policy_text,
                cedar_schema_json: pipeline.cedar_schema_json,
                cedar_schema_text: pipeline.cedar_schema_text,
                collision: pipeline.collision,
                stage_error: pipeline.stage_error,
            },
            D2_RESPONSE_LIMIT,
        ),
        _ => Err(Status::UnsupportedOperation),
    }
}

fn pipeline(operation: u32, request: &Request) -> Result<Pipeline, Status> {
    let mut pipeline = Pipeline::empty();

    let service = match build_service(request) {
        Ok(service) => service,
        Err(error) => {
            pipeline.stage_error = Some(stage_error("service-schema", &error, &request.source));
            return Ok(pipeline);
        }
    };

    let parsed = match ParsedPolicySet::parse(&request.source, &service) {
        Ok(parsed) => parsed,
        Err(Error::Parse(errors)) => {
            pipeline.parse_diagnostics = errors
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
            return Ok(pipeline);
        }
        Err(Error::Macro(error)) => {
            pipeline.parse_diagnostics = vec![diagnostic(
                "macro",
                error.to_string(),
                error.location(),
                &request.source,
            )];
            return Ok(pipeline);
        }
        Err(_) => return Err(Status::InternalError),
    };

    pipeline.parsed = true;
    pipeline.policy_count = Some(parsed.policy_count());

    // Constructing the action schema does not typecheck it, so an unusable
    // schema surfaces here rather than as a valid bundle.
    let schema = match PolicySchema::from_cedarschema_str(&request.policy_schema) {
        Ok(schema) => schema,
        Err(error) => {
            pipeline.stage_error = Some(stage_error("lower", &error, &request.source));
            return Ok(pipeline);
        }
    };

    let lowered = match request.distincter.as_deref() {
        Some(distincter) => parsed.lower_with_distincter(&schema, distincter),
        None => parsed.lower(&schema),
    };
    let lowered = match lowered {
        Ok(lowered) => lowered,
        Err(error) => {
            pipeline.stage_error = Some(stage_error("lower", &error, &request.source));
            return Ok(pipeline);
        }
    };

    pipeline.lowered = true;
    let (collision, failure) = match collision::find(&request.policy_schema, &lowered) {
        Ok(None) => (None, None),
        Ok(Some(collision)) => (
            Some(collision),
            Some((
                "generated-context-field-collision",
                "A generated temporal field is already declared in the original action context."
                    .to_owned(),
            )),
        ),
        Err(message) => (None, Some(("internal", message))),
    };
    if let Some((category, message)) = failure {
        pipeline.collision = collision;
        pipeline.stage_error = Some(StageError {
            stage: "collision-check",
            category,
            message,
            location: None,
        });
        return Ok(pipeline);
    }

    pipeline.rule_count = lowered.rules().count();
    pipeline.self_contained_cedar = lowered.is_self_contained_cedar();
    pipeline.rules = lowered
        .rules()
        .map(|rule| RuleRef {
            rule_index: rule.rule_index,
            cedar_policy_id: rule.cedar_policy_id,
        })
        .collect();
    pipeline.lowered_rules = map_rules(&parsed, &lowered).ok_or(Status::InternalError)?;

    let validation = Validator::new().validate(&lowered);
    pipeline.validation_passed = validation.validation_passed();
    pipeline.validation_errors = validation
        .validation_errors()
        .map(|error| finding(error, &request.source))
        .collect();
    pipeline.validation_warnings = validation
        .validation_warnings()
        .map(finding_warning)
        .collect();

    if operation == OPERATION_LOWER {
        // Serialize every artifact before any of them is returned: a
        // serialization failure must not produce partial authoritative output.
        let policy_text = lowered.as_cedar().to_string();
        let schema_json = lowered
            .cedar_schema_json()
            .map_err(|_| Status::InternalError)?;
        let schema_text = lowered
            .cedar_schema_str()
            .map_err(|_| Status::InternalError)?;
        pipeline.cedar_policy_text = policy_text;
        pipeline.cedar_schema_json = schema_json;
        pipeline.cedar_schema_text = schema_text;
    }

    Ok(pipeline)
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

/// Pair each parsed Dogwood policy with the Cedar policy id it lowered to.
fn map_rules(parsed: &ParsedPolicySet, lowered: &LoweredPolicySet) -> Option<Vec<LoweredRule>> {
    let mut rules: Vec<LoweredRule> = Vec::new();
    for rule in lowered.rules() {
        let policy = parsed
            .policies()
            .find(|policy| policy.index() == rule.rule_index)?;
        rules.push(LoweredRule {
            rule_index: rule.rule_index,
            cedar_policy_id: rule.cedar_policy_id,
            source_text: policy.expanded_source(),
        });
    }
    Some(rules)
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

/// Cedar publishes a warning's text but not its source span through the public
/// surface, so only the message is exported.
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
