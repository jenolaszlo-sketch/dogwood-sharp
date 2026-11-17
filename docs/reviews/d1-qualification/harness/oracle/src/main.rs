//! Independent oracle for the D1 parser differential.
//!
//! Calls the pinned `dogwood-language` frontend directly and applies only the
//! documented D1 diagnostic projection (category, location, BOM rebasing).
//! It also projects the documented operation-1 JSON shape so the comparator
//! can derive the expected response status when the encoded reply would cross
//! the documented 2 MiB response ceiling. Diagnostic message strings are
//! recorded for information but the comparator does not require them to match.

use dogwood_language::{DEFAULT_MAX_DEPTH, DEFAULT_MAX_NODES, Error, ParsedPolicySet, ServiceSchema, SourceLoc};
use serde::{Deserialize, Serialize};
use std::process::ExitCode;

const RESPONSE_LIMIT: usize = 2 * 1024 * 1024;

#[derive(Deserialize)]
struct Cases {
    cases: Vec<Case>,
}

#[derive(Deserialize)]
struct Case {
    id: String,
    source: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Report {
    dogwood_revision: &'static str,
    default_max_depth: usize,
    default_max_nodes: usize,
    cases: Vec<OracleCase>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct OracleCase {
    id: String,
    outcome: &'static str,
    policy_count: Option<usize>,
    diagnostics: Vec<Diagnostic>,
    wire_bytes: usize,
    wire_status: u32,
}

#[derive(Serialize, Clone)]
struct Diagnostic {
    category: &'static str,
    message: String,
    location: Option<Location>,
}

#[derive(Serialize, Clone, PartialEq, Eq)]
struct Location {
    offset: usize,
    length: usize,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct WireReply {
    success: bool,
    policy_count: Option<usize>,
    diagnostics: Vec<Diagnostic>,
}

fn project(category: &'static str, message: String, loc: &SourceLoc, source: &str) -> Diagnostic {
    let span = loc.span();
    let offset = span.offset() + if source.starts_with('\u{feff}') { 3 } else { 0 };
    Diagnostic {
        category,
        message,
        location: (loc.source() == source
            && offset <= source.len()
            && (span.len() <= source.len() - offset || (offset == source.len() && span.len() == 1)))
            .then_some(Location {
                offset,
                length: span.len(),
            }),
    }
}

fn wire(outcome: &'static str, policy_count: Option<usize>, diagnostics: Vec<Diagnostic>) -> (usize, u32) {
    if outcome == "internal" {
        return (0, 8);
    }
    let reply = WireReply {
        success: outcome == "ok",
        policy_count,
        diagnostics,
    };
    match serde_json::to_vec(&reply) {
        Ok(bytes) => {
            let status = if bytes.len() > RESPONSE_LIMIT { 6 } else { 0 };
            (bytes.len(), status)
        }
        Err(_) => (0, 8),
    }
}

fn evaluate(case: Case) -> OracleCase {
    let (outcome, policy_count, diagnostics) =
        match ParsedPolicySet::parse(&case.source, &ServiceSchema::defaults()) {
            Ok(policies) => ("ok", Some(policies.policy_count()), Vec::new()),
            Err(Error::Parse(errors)) => (
                "parse",
                None,
                errors
                    .iter()
                    .map(|error| project("syntax", error.to_string(), error.location(), &case.source))
                    .collect(),
            ),
            Err(Error::Macro(error)) => (
                "macro",
                None,
                vec![project("macro", error.to_string(), error.location(), &case.source)],
            ),
            Err(_) => ("internal", None, Vec::new()),
        };
    let (wire_bytes, wire_status) = wire(outcome, policy_count, diagnostics.clone());
    OracleCase {
        id: case.id,
        outcome,
        policy_count,
        diagnostics,
        wire_bytes,
        wire_status,
    }
}

fn main() -> ExitCode {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    if arguments.len() != 2 {
        eprintln!("usage: oracle <cases.json> <oracle-report.json>");
        return ExitCode::from(2);
    }
    if DEFAULT_MAX_DEPTH != 32 || DEFAULT_MAX_NODES != 1_000_000 {
        eprintln!(
            "unexpected pinned defaults: depth={DEFAULT_MAX_DEPTH} nodes={DEFAULT_MAX_NODES}"
        );
        return ExitCode::from(1);
    }
    let input = match std::fs::read(&arguments[0]) {
        Ok(bytes) => bytes,
        Err(error) => {
            eprintln!("cannot read {}: {error}", arguments[0]);
            return ExitCode::from(2);
        }
    };
    let cases: Cases = match serde_json::from_slice(&input) {
        Ok(cases) => cases,
        Err(error) => {
            eprintln!("invalid cases file: {error}");
            return ExitCode::from(2);
        }
    };
    let report = Report {
        dogwood_revision: "929f49e7305534ac61ef06cf8e47269c596dc851",
        default_max_depth: DEFAULT_MAX_DEPTH,
        default_max_nodes: DEFAULT_MAX_NODES,
        cases: cases.cases.into_iter().map(evaluate).collect(),
    };
    let output = match serde_json::to_vec_pretty(&report) {
        Ok(bytes) => bytes,
        Err(error) => {
            eprintln!("cannot encode report: {error}");
            return ExitCode::from(2);
        }
    };
    if let Err(error) = std::fs::write(&arguments[1], output) {
        eprintln!("cannot write {}: {error}", arguments[1]);
        return ExitCode::from(2);
    }
    println!(
        "ORACLE cases={} depth={} nodes={}",
        report.cases.len(),
        report.default_max_depth,
        report.default_max_nodes
    );
    ExitCode::SUCCESS
}
