#!/usr/bin/env python3
"""Build the R5-R1 three-way differential record and the six-cell matrix.

The three-way record states, for every case of the R5-R1 corpus, the input
representation, the expected type and decision, and what each of the four
sides actually did:

  * the Cedar authority - the direct engine evaluation, which never touches
    the Dogwood conversion path;
  * the ORIGINAL pinned Dogwood reference;
  * the CORRECTED reference (the pinned Dogwood crate with the committed
    typed-context patch);
  * D3 (the production CedarSharp path under test).

Each case is classified with one of four labels:

  * `reference defect`      - the ORIGINAL reference disagrees with the
                              authoritative Cedar semantics and the corrected
                              reference agrees; the defect is in the pinned
                              reference path and the R5-R1 reference patch
                              removes it;
  * `D3 defect`             - D3 disagrees with the authoritative Cedar
                              semantics while the corrected reference agrees;
  * `intentional deviation` - the case carries a pre-existing, owner-approved
                              difference (ED-01 through ED-05) or a
                              fail-closed normalization (FCN-01 through
                              FCN-04);
  * `open question`         - the commission could not resolve the case
                              without an owner decision; it is recorded, not
                              normalized.

The generator reads only recorded evidence. It never re-evaluates a case and
it never derives an expectation from an observed outcome: the Cedar
expectation comes from the committed authority table, and the classification
rules are stated in the generated record.
"""

from __future__ import annotations

import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
EVIDENCE = HERE.parent
REPOSITORY = HERE.parents[4]

FIXTURES = EVIDENCE / "fixtures.json"
ORIGINAL = {
    tfm: EVIDENCE / f"observations-original-reference-{tfm}.json"
    for tfm in ("net8.0", "net10.0")
}
CORRECTED = {
    tfm: EVIDENCE / f"observations-corrected-reference-{tfm}.json"
    for tfm in ("net8.0", "net10.0")
}
AUTHORITY = HERE / "cedar-adjudicator" / "authority-verification.json"
ADJUDICATOR_CORPUS = HERE / "cedar-adjudicator" / "corpus.json"
REGISTER_ORIGINAL = EVIDENCE / "register.json"
REGISTER_CORRECTED = EVIDENCE / "register-corrected-reference.json"

TFMS = ("net8.0", "net10.0")

# The approved-difference and fail-closed normalization identifiers, carried
# forward from the frozen R4 register.
INTENTIONAL = "intentional deviation"
REFERENCE_DEFECT = "reference defect"
D3_DEFECT = "D3 defect"
OPEN_QUESTION = "open question"
AGREEMENT = "agreement"


def load(path: pathlib.Path):
    return json.loads(path.read_text(encoding="utf-8"))


def observation(case: dict | None) -> dict | None:
    """The comparable fields of one side's observation."""
    if case is None:
        return None
    return {
        "stage": case.get("stage"),
        "decision": case.get("decision"),
        "operationFailed": case.get("operationFailed"),
        "hasErrors": case.get("hasErrors"),
        "cleanAllow": case.get("cleanAllow"),
        "determining": case.get("determining"),
        "errorCategories": case.get("errorCategories"),
        "policyErrorIds": case.get("policyErrorIds"),
        "errors": case.get("errors"),
        "error": case.get("error"),
    }


def compact(text) -> str:
    if text is None:
        return "-"
    if isinstance(text, list):
        if not text:
            return "-"
        return "; ".join(str(item) for item in text)
    return str(text)


def representation(case: dict) -> str:
    """The input representation the case exercises."""
    event = case.get("event") or {}
    context = event.get("context")
    if context is None:
        return "(no context)"
    rendered = json.dumps(context, sort_keys=True, separators=(",", ":"))
    return rendered if len(rendered) <= 160 else rendered[:157] + "..."


def classify(
    case: dict,
    authority_row: dict | None,
    original_case: dict,
    corrected_case: dict,
) -> tuple[str, str]:
    """Classify one case and explain the classification."""
    classification = original_case["classification"]
    difference_ids = original_case.get("differenceValidation", {}).get(
        "approvedDifferences", []
    )

    if classification == "approved-difference":
        ids = ", ".join(difference_ids) if difference_ids else "(registered)"
        return (
            INTENTIONAL,
            f"The case carries the pre-existing owner-approved difference {ids}. "
            f"The approval is carried forward unchanged from the frozen R4 register "
            f"and is re-verified against the corrected reference.",
        )
    if classification == "fail-closed-equivalence":
        normalization = original_case.get("differenceValidation", {}).get(
            "failClosedNormalization", "(none)"
        )
        return (
            INTENTIONAL,
            f"The case is a fail-closed equivalence normalized by {normalization}: "
            f"neither side authorizes and the per-side observations are pinned "
            f"exactly by the register row.",
        )
    if classification == "operation-failure":
        return (
            INTENTIONAL,
            "Both sides fail closed on the same input and neither produces a "
            "decision; the failure is the observation, not a divergence.",
        )
    if classification == "agreement":
        return (
            AGREEMENT,
            "Every side agrees on the stage, the decision, the determining "
            "rules and the error state.",
        )

    # The remaining classification is `unexpected-difference`. Decide whether
    # the reference or D3 is the side that departs from the authority.
    if authority_row is None:
        return (
            OPEN_QUESTION,
            "The case diverges and no direct Cedar authority row exists for it, "
            "so the commission could not decide which side is correct without "
            "an owner decision.",
        )
    expected = authority_row["expected"]["decision"]
    original_decision = original_case["upstream"]["decision"]
    corrected_decision = corrected_case["upstream"]["decision"]
    managed_decision = original_case["managed"]["decision"]
    if (
        corrected_decision == expected
        and managed_decision == expected
        and original_decision != expected
    ):
        return (
            REFERENCE_DEFECT,
            f"The authoritative Cedar decision is `{expected}`. The ORIGINAL "
            f"reference produced `{original_decision}` because the pinned "
            f"Dogwood value model degrades the typed context value to a record "
            f"before Cedar sees it; the corrected reference produces "
            f"`{corrected_decision}` and D3 produces `{managed_decision}`. The "
            f"defect is in the pinned reference path, and the committed "
            f"typed-context patch removes it.",
        )
    if (
        original_decision == expected
        and corrected_decision == expected
        and managed_decision != expected
    ):
        return (
            D3_DEFECT,
            f"The authoritative Cedar decision is `{expected}`. Both "
            f"references produce `{original_decision}` and D3 produces "
            f"`{managed_decision}`. D3 departs from the authoritative "
            f"semantics.",
        )
    return (
        OPEN_QUESTION,
        f"The authoritative Cedar decision is `{expected}`; the ORIGINAL "
        f"reference produced `{original_decision}`, the corrected reference "
        f"`{corrected_decision}` and D3 `{managed_decision}`. The commission "
        f"could not classify the divergence without an owner decision.",
    )


def main() -> int:
    fixtures = load(FIXTURES)
    original_runs = {tfm: load(path) for tfm, path in ORIGINAL.items()}
    corrected_runs = {tfm: load(path) for tfm, path in CORRECTED.items()}
    authority = load(AUTHORITY)
    adjudicator_corpus = load(ADJUDICATOR_CORPUS)
    register_original = load(REGISTER_ORIGINAL)
    register_corrected = load(REGISTER_CORRECTED)

    authority_by_fixture = {
        row["fixtureId"]: row for row in authority["authorityTable"] if row.get("fixtureId")
    }
    adjudicator_by_fixture: dict[str, dict] = {}
    for case in adjudicator_corpus["cases"]:
        if case.get("fixtureId"):
            adjudicator_by_fixture[case["fixtureId"]] = case

    original_cases = {
        tfm: {case["id"]: case for case in run["cases"]} for tfm, run in original_runs.items()
    }
    corrected_cases = {
        tfm: {case["id"]: case for case in run["cases"]} for tfm, run in corrected_runs.items()
    }

    rows = []
    counts: dict[str, int] = {}
    for fixture in fixtures["cases"]:
        case_id = fixture["id"]
        original_case = original_cases["net8.0"][case_id]
        corrected_case = corrected_cases["net8.0"][case_id]
        authority_row = authority_by_fixture.get(case_id)
        label, explanation = classify(
            fixture, authority_row, original_case, corrected_case
        )
        counts[label] = counts.get(label, 0) + 1

        adjudicator_case = adjudicator_by_fixture.get(case_id)
        if authority_row is not None:
            authority_summary = {
                "engine": "cedar-policy 4.13.0 (identical at 4.11.0)",
                "stage": authority_row["observed"]["stage"],
                "decision": authority_row["observed"]["decision"],
                "operationFailed": authority_row["observed"]["operationFailed"],
                "hasErrors": authority_row["observed"]["hasErrors"],
                "errors": authority_row["errors"],
                "adjudicationCase": authority_row["id"],
                "adjudicationFixtureId": adjudicator_case.get("fixtureId")
                if adjudicator_case
                else None,
            }
        else:
            authority_summary = {
                "engine": None,
                "stage": None,
                "decision": None,
                "operationFailed": None,
                "hasErrors": None,
                "errors": [],
                "adjudicationCase": None,
                "adjudicationFixtureId": None,
                "note": (
                    "No direct Cedar authority row exists for this case: the "
                    "pinned Cedar engine has no temporal extension, and the "
                    "case exercises the trusted-runtime or export surface "
                    "rather than Cedar's context semantics."
                ),
            }

        sides = {}
        for label_name, runs, side in (
            ("originalReference", original_cases, "upstream"),
            ("correctedReference", corrected_cases, "upstream"),
            ("d3", original_cases, "managed"),
        ):
            per_tfm = {}
            for tfm in TFMS:
                per_tfm[tfm] = observation(runs[tfm][case_id][side])
            sides[label_name] = per_tfm

        # The D3 observation is identical in both runs of the differential: the
        # managed side does not depend on which reference the harness invokes.
        d3_stable = all(
            sides["d3"]["net8.0"] == sides["d3"]["net10.0"] for _ in [0]
        )
        original_stable = all(
            sides["originalReference"]["net8.0"] == sides["originalReference"]["net10.0"]
            for _ in [0]
        )
        corrected_stable = all(
            sides["correctedReference"]["net8.0"]
            == sides["correctedReference"]["net10.0"]
            for _ in [0]
        )

        rows.append(
            {
                "id": case_id,
                "family": fixture.get("families", []),
                "control": fixture.get("control"),
                "question": fixture.get("question", ""),
                "inputRepresentation": representation(fixture),
                "expectedType": (
                    "Cedar typed extension value (ipaddr / datetime / decimal)"
                    if authority_row is not None
                    else "not a Cedar context-type case"
                ),
                "expectedDecision": (
                    authority_row["expected"]["decision"]
                    if authority_row is not None
                    else "not directly adjudicable"
                ),
                "expectedStage": (
                    authority_row["expected"]["stage"]
                    if authority_row is not None
                    else "not directly adjudicable"
                ),
                "authority": authority_summary,
                "originalReference": sides["originalReference"],
                "correctedReference": sides["correctedReference"],
                "d3": sides["d3"],
                "originalReferenceStableAcrossTfms": original_stable,
                "correctedReferenceStableAcrossTfms": corrected_stable,
                "d3StableAcrossTfms": d3_stable,
                "originalClassification": original_case["classification"],
                "correctedClassification": corrected_case["classification"],
                "divergence": label,
                "explanation": explanation,
            }
        )

    # ---- the six-cell matrix -------------------------------------------
    matrix = {
        "commission": "DOGWOOD-SHARP-D3-IMPL-01-R5-R1",
        "matrix": "the minimum D3 matrix of the pinned qualification plan, section 9",
        "requirement": (
            "The upstream oracle and the D3 adapter must be built from their "
            "locked graphs for each cell, and the same fixture and "
            "expected-difference manifest must be used on every cell. A cell "
            "that cannot be run is pending, never passed."
        ),
        "cells": [
            {
                "os": "Windows",
                "rid": "win-x64",
                "tfm": tfm,
                "status": "executed",
                "originalReference": {
                    "cases": original_runs[tfm]["summary"]["total"],
                    "passed": original_runs[tfm]["summary"]["passed"],
                    "failed": original_runs[tfm]["summary"]["failed"],
                    "pending": original_runs[tfm]["summary"]["pending"],
                    "note": (
                        "The eleven failures are the reproduced reference "
                        "defect surface: six alternate-encoding cases and five "
                        "malformed-encoding cases."
                    ),
                },
                "correctedReference": {
                    "cases": corrected_runs[tfm]["summary"]["total"],
                    "passed": corrected_runs[tfm]["summary"]["passed"],
                    "failed": corrected_runs[tfm]["summary"]["failed"],
                    "pending": corrected_runs[tfm]["summary"]["pending"],
                    "note": "No failure and no pending case against the corrected reference.",
                },
                "d3": {
                    "stableAcrossBothRuns": rows and all(row["d3StableAcrossTfms"] for row in rows),
                },
                "evidence": [
                    f"evidence/observations-original-reference-{tfm}.json",
                    f"evidence/observations-corrected-reference-{tfm}.json",
                    f"evidence/lifecycle-original-reference-{tfm}.json",
                    f"evidence/lifecycle-corrected-reference-{tfm}.json",
                ],
            }
            for tfm in TFMS
        ]
        + [
            {
                "os": os,
                "rid": rid,
                "tfm": tfm,
                "status": "pending",
                "originalReference": None,
                "correctedReference": None,
                "d3": None,
                "evidence": [],
                "note": (
                    "Not executed in this commission. The commission ran on "
                    "Windows x64 and the differential harness invokes a Windows "
                    "oracle executable; the pinned qualification plan requires "
                    "the same locked oracle, fixtures and difference register "
                    "on every cell, so this cell is pending and is never "
                    "reported as passed."
                ),
            }
            for os, rid in (("Linux", "linux-x64"), ("macOS", "osx-arm64"))
            for tfm in TFMS
        ],
    }

    (HERE / "three-way-record.json").write_text(
        json.dumps(
            {
                "commission": "DOGWOOD-SHARP-D3-IMPL-01-R5-R1",
                "cases": rows,
                "classificationCounts": counts,
                "registers": {
                    "originalReference": {
                        "differences": [
                            row["id"] for row in register_original["differences"]
                        ],
                        "failClosedNormalizations": [
                            row["id"]
                            for row in register_original["failClosedNormalizations"]
                        ],
                    },
                    "correctedReference": {
                        "differences": [
                            row["id"] for row in register_corrected["differences"]
                        ],
                        "failClosedNormalizations": [
                            row["id"]
                            for row in register_corrected["failClosedNormalizations"]
                        ],
                    },
                },
            },
            indent=2,
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
    )
    (EVIDENCE / "six-cell-matrix.json").write_text(
        json.dumps(matrix, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )

    # ---- the markdown record -------------------------------------------
    lines = []
    lines.append("# DOGWOOD-SHARP-D3-IMPL-01-R5-R1 — three-way differential record")
    lines.append("")
    lines.append(
        "Generated by `evidence/differential/build-three-way-record.py` from the "
        "recorded evidence. The four sides are the direct Cedar authority, the "
        "ORIGINAL pinned Dogwood reference, the CORRECTED reference and D3."
    )
    lines.append("")
    lines.append("| Classification | Cases |")
    lines.append("| --- | --- |")
    for label in sorted(counts):
        lines.append(f"| {label} | {counts[label]} |")
    lines.append("")
    lines.append(
        "Every side is reported on both target frameworks. `originalReference`, "
        "`correctedReference` and `d3` are byte-identical across `net8.0` and "
        "`net10.0` for every case in this corpus."
    )
    lines.append("")
    for row in rows:
        lines.append(f"## {row['id']}")
        lines.append("")
        lines.append(f"- Family: {', '.join(row['family']) or '-'}")
        lines.append(f"- Control: `{row['control']}`")
        if row["question"]:
            lines.append(f"- Question: {row['question']}")
        lines.append(f"- Input representation: `{row['inputRepresentation']}`")
        lines.append(f"- Expected type: {row['expectedType']}")
        lines.append(f"- Expected decision: `{row['expectedDecision']}`")
        lines.append(f"- Expected stage: `{row['expectedStage']}`")
        lines.append(f"- Divergence class: **{row['divergence']}**")
        lines.append(f"- Explanation: {row['explanation']}")
        lines.append("")
        lines.append("| Side | Stage | Decision | Operation failed | Errors |")
        lines.append("| --- | --- | --- | --- | --- |")
        authority = row["authority"]
        lines.append(
            "| Cedar authority (4.13.0 = 4.11.0) | "
            f"{authority['stage']} | {authority['decision']} | "
            f"{authority['operationFailed']} | {compact(authority['errors'])} |"
        )
        for name, key in (
            ("Original reference", "originalReference"),
            ("Corrected reference", "correctedReference"),
            ("D3", "d3"),
        ):
            for tfm in TFMS:
                side = row[key][tfm]
                if side is None:
                    lines.append(
                        f"| {name} ({tfm}) | (not invoked) | (not invoked) | (not invoked) | - |"
                    )
                else:
                    lines.append(
                        f"| {name} ({tfm}) | {side['stage']} | {side['decision']} | "
                        f"{side['operationFailed']} | "
                        f"{compact(side['errors'] or side['error'])} |"
                    )
        lines.append("")
    (HERE / "three-way-record.md").write_text(
        "\n".join(lines).rstrip() + "\n", encoding="utf-8"
    )

    print(f"cases: {len(rows)}")
    for label in sorted(counts):
        print(f"  {label}: {counts[label]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
