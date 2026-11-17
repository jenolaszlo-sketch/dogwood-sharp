//! Independent expectations derived from the pinned upstream Dogwood crate.
//!
//! The oracle calls the upstream crate directly, exactly as the bridge does,
//! and reduces the result to the structural facts the pins record. It never
//! calls the bridge, so agreement between the two is real evidence.

use dogwood_language::{
    Error, ParsedPolicySet, PolicySchema, ServiceSchema, Validator, ValidationError,
    ValidationWarning,
};
use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Deserialize, Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub(super) struct Case {
    pub(super) id: String,
    pub(super) source: String,
    #[serde(default)]
    pub(super) policy_schema: Option<String>,
    #[serde(default)]
    pub(super) event_schema: Option<String>,
    #[serde(default)]
    pub(super) macros: Option<String>,
    #[serde(default)]
    pub(super) distincter: Option<String>,
    #[serde(default)]
    pub(super) expect: Expect,
}

impl Case {
    /// The action schema text, defaulting to the manifest's shared schema.
    pub(super) fn policy_schema<'a>(&'a self, manifest: &'a Manifest) -> &'a str {
        self.policy_schema
            .as_deref()
            .unwrap_or(manifest.default_policy_schema.as_str())
    }
}

#[derive(Deserialize, Serialize, Clone, Default, PartialEq)]
pub(super) struct Expect {
    pub(super) parsed: bool,
    #[serde(rename = "policyCount")]
    pub(super) policy_count: Option<usize>,
    #[serde(rename = "parseDiagnostics")]
    pub(super) parse_diagnostics: Vec<Diag>,
    pub(super) lowered: bool,
    #[serde(rename = "ruleCount")]
    pub(super) rule_count: usize,
    #[serde(rename = "selfContainedCedar")]
    pub(super) self_contained_cedar: bool,
    pub(super) rules: Vec<RuleExpect>,
    #[serde(rename = "cedarPolicyText")]
    pub(super) cedar_policy_text: String,
    #[serde(rename = "cedarSchemaJson")]
    pub(super) cedar_schema_json: Value,
    #[serde(rename = "cedarSchemaText")]
    pub(super) cedar_schema_text: String,
    #[serde(rename = "validationPassed")]
    pub(super) validation_passed: bool,
    #[serde(rename = "validationErrors")]
    pub(super) validation_errors: Vec<Finding>,
    #[serde(rename = "validationWarnings")]
    pub(super) validation_warnings: Vec<Finding>,
    #[serde(rename = "stageError")]
    pub(super) stage_error: Option<StageExpect>,
}

#[derive(Deserialize, Serialize, Clone, PartialEq, Debug)]
pub(super) struct Diag {
    pub(super) category: String,
    pub(super) location: Option<Loc>,
}

#[derive(Deserialize, Serialize, Clone, PartialEq, Debug)]
pub(super) struct Loc {
    pub(super) offset: usize,
    pub(super) length: usize,
}

#[derive(Deserialize, Serialize, Clone, PartialEq, Debug)]
pub(super) struct RuleExpect {
    #[serde(rename = "ruleIndex")]
    pub(super) rule_index: usize,
    #[serde(rename = "cedarPolicyId")]
    pub(super) cedar_policy_id: String,
    #[serde(rename = "sourceText")]
    pub(super) source_text: String,
}

#[derive(Deserialize, Serialize, Clone, PartialEq, Debug)]
pub(super) struct Finding {
    pub(super) category: String,
    pub(super) location: Option<Loc>,
}

#[derive(Deserialize, Serialize, Clone, PartialEq, Debug)]
pub(super) struct StageExpect {
    pub(super) stage: String,
    pub(super) category: String,
}

#[derive(Deserialize, Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub(super) struct Manifest {
    pub(super) fixture_version: u32,
    pub(super) dogwood_revision: String,
    pub(super) default_policy_schema: String,
    pub(super) cases: Vec<Case>,
}

/// Re-derive a case's expectations from the pinned upstream crate.
pub(super) fn derive(case: &Case, manifest: &Manifest) -> Expect {
    let service = match ServiceSchema::builder()
        .event_schema_opt(case.event_schema.as_deref())
        .macros_opt(case.macros.as_deref())
        .build()
    {
        Ok(service) => service,
        Err(error) => {
            return Expect {
                stage_error: Some(StageExpect {
                    stage: "service-schema".to_string(),
                    category: stage_category(&error),
                }),
                ..Expect::default()
            };
        }
    };

    let parsed = match ParsedPolicySet::parse(&case.source, &service) {
        Ok(parsed) => parsed,
        Err(Error::Parse(errors)) => {
            return Expect {
                parsed: false,
                parse_diagnostics: errors
                    .iter()
                    .map(|error| Diag {
                        category: "syntax".to_string(),
                        location: locate(error.location(), &case.source),
                    })
                    .collect(),
                ..Expect::default()
            };
        }
        Err(Error::Macro(error)) => {
            return Expect {
                parsed: false,
                parse_diagnostics: vec![Diag {
                    category: "macro".to_string(),
                    location: locate(error.location(), &case.source),
                }],
                ..Expect::default()
            };
        }
        Err(_) => panic!("{}: unexpected upstream parse error", case.id),
    };

    let schema = match PolicySchema::from_cedarschema_str(case.policy_schema(manifest)) {
        Ok(schema) => schema,
        Err(error) => {
            return Expect {
                parsed: true,
                policy_count: Some(parsed.policy_count()),
                stage_error: Some(StageExpect {
                    stage: "lower".to_string(),
                    category: stage_category(&error),
                }),
                ..Expect::default()
            };
        }
    };

    let lowered = match case.distincter.as_deref() {
        Some(distincter) => parsed.lower_with_distincter(&schema, distincter),
        None => parsed.lower(&schema),
    };
    let lowered = match lowered {
        Ok(lowered) => lowered,
        Err(error) => {
            return Expect {
                parsed: true,
                policy_count: Some(parsed.policy_count()),
                stage_error: Some(StageExpect {
                    stage: "lower".to_string(),
                    category: stage_category(&error),
                }),
                ..Expect::default()
            };
        }
    };

    let validation = Validator::new().validate(&lowered);
    Expect {
        parsed: true,
        policy_count: Some(parsed.policy_count()),
        lowered: true,
        rule_count: lowered.rules().count(),
        self_contained_cedar: lowered.is_self_contained_cedar(),
        rules: lowered
            .rules()
            .map(|rule| RuleExpect {
                rule_index: rule.rule_index,
                cedar_policy_id: rule.cedar_policy_id,
                source_text: parsed
                    .policies()
                    .find(|policy| policy.index() == rule.rule_index)
                    .map(|policy| policy.expanded_source())
                    .unwrap_or_default(),
            })
            .collect(),
        cedar_policy_text: lowered.as_cedar().to_string(),
        cedar_schema_json: serde_json::from_str(&lowered.cedar_schema_json().unwrap_or_default())
            .unwrap_or(Value::Null),
        cedar_schema_text: lowered.cedar_schema_str().unwrap_or_default(),
        validation_passed: validation.validation_passed(),
        validation_errors: validation
            .validation_errors()
            .map(|error| finding(error, &case.source))
            .collect(),
        validation_warnings: validation
            .validation_warnings()
            .map(finding_warning)
            .collect(),
        stage_error: None,
        parse_diagnostics: Vec::new(),
    }
}

/// Upstream reports every schema problem through `Error::EventSchema`, so the
/// oracle mirrors the bridge's single `schema` category for all of them.
fn stage_category(error: &Error) -> String {
    let category = match error {
        Error::ActionSchema(_)
        | Error::CedarSchema(_)
        | Error::PolicySet(_)
        | Error::SchemaSerialize(_)
        | Error::EventSchema(_) => "schema",
        Error::InvalidDistincter(_) | Error::DuplicateDistincter(_) => "distincter",
        Error::Parse(_) | Error::Macro(_) | Error::Cedarify(_) | Error::Leaf { .. } => "policy",
        _ => "internal",
    };
    category.to_string()
}

/// Project a span onto the case source, rebasing a stripped leading UTF-8 BOM.
fn locate(loc: &dogwood_language::SourceLoc, source: &str) -> Option<Loc> {
    let span = loc.span();
    let offset = span.offset() + if source.starts_with('\u{feff}') { 3 } else { 0 };
    (loc.source() == source
        && offset <= source.len()
        && (span.len() <= source.len() - offset
            || (offset == source.len() && span.len() == 1)))
        .then_some(Loc {
            offset,
            length: span.len(),
        })
}

fn locating(offset: usize, length: usize, parsed_source: &str, source: &str) -> Option<Loc> {
    let rebase = if parsed_source == source {
        0
    } else if source.strip_prefix('\u{feff}') == Some(parsed_source) {
        3
    } else {
        return None;
    };
    let offset = offset + rebase;
    (offset <= source.len()
        && (length <= source.len() - offset
            || (offset == source.len() && length == 1)))
        .then_some(Loc { offset, length })
}

fn finding(error: &ValidationError, source: &str) -> Finding {
    let (category, location) = match error {
        ValidationError::Cedar { span, src, .. } => (
            "cedar",
            locating(span.offset(), span.len(), src, source),
        ),
        ValidationError::Extension { code, span, src, .. } => (
            *code,
            locating(span.offset(), span.len(), src, source),
        ),
        _ => ("other", None),
    };
    Finding {
        category: category.to_string(),
        location,
    }
}

fn finding_warning(warning: &ValidationWarning) -> Finding {
    let category = match warning {
        ValidationWarning::Cedar(_) => "cedar",
        ValidationWarning::Extension { code, .. } => *code,
        _ => "other",
    };
    Finding {
        category: category.to_string(),
        location: None,
    }
}

trait ServiceSchemaOptions {
    fn event_schema_opt(self, source: Option<&str>) -> Self;
    fn macros_opt(self, source: Option<&str>) -> Self;
}

impl ServiceSchemaOptions for dogwood_language::ServiceSchemaBuilder {
    fn event_schema_opt(self, source: Option<&str>) -> Self {
        match source {
            Some(text) => self.event_schema_str(text),
            None => self,
        }
    }

    fn macros_opt(self, source: Option<&str>) -> Self {
        match source {
            Some(text) => self.macros_str(text),
            None => self,
        }
    }
}