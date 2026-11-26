#!/usr/bin/env python3
"""DOGWOOD-SHARP-D3-IMPL-01-R5-R1 register builder.

Builds the two R5-R1 difference registers from the preserved R4 register and
the new R5-R1 fail-closed normalizations:

  * register.json                     - the ORIGINAL pinned Dogwood reference.
                                       ED-01 through ED-06 are carried forward
                                       verbatim from the frozen R4 register
                                       (the ED-06 approval is preserved
                                       unchanged and is exercised again), and
                                       the R4 fail-closed normalization FCN-01
                                       is carried forward verbatim. The new
                                       FCN-02 row records the malformed
                                       typed-encoding controls, FCN-03 the
                                       temporal history-replay conformance
                                       failure and FCN-04 the temporal
                                       current-event conformance failure.
  * register-corrected-reference.json - the CORRECTED reference. The ED-06 row
                                       is NOT carried forward: the corrected
                                       reference eliminates that divergence, so
                                       the approval would have nothing to
                                       absorb, and carrying it forward would
                                       require an owner decision this
                                       commission does not have. ED-01 through
                                       ED-05 and FCN-01 are carried forward
                                       verbatim and are re-verified against
                                       the corrected reference. FCN-02 is
                                       re-declared against the corrected
                                       reference's own observation, because
                                       the malformed typed encoding is
                                       rejected there instead of being
                                       degraded to a record. FCN-03 and FCN-04
                                       are unchanged: the typed-context
                                       correction does not touch the temporal
                                       pipeline. The ED-06 disposition is
                                       recorded as a note, not as an active
                                       approval.

The builder asserts that every carried-forward row is byte-identical to the
frozen R4 row, so the historical approvals cannot drift.
"""

from __future__ import annotations

import copy
import json
import pathlib
import sys

REPOSITORY = pathlib.Path(__file__).resolve().parents[5]
R4_REGISTER = REPOSITORY / "docs/reviews/d3-impl-01/evidence/differential/r4/differences.json"
OUTPUT = REPOSITORY / "docs/reviews/d3-impl-01-r5-r1/evidence"

# The malformed typed-encoding controls. Each entry records the exact
# diagnostic the managed side reports; the reference side either degrades the
# malformed value to a record (the ORIGINAL reference) or rejects it with the
# same diagnostic (the CORRECTED reference).
INVALID_ENCODING_CASES = [
    (
        "R5R1-C23-01-ipaddr-invalid-bare-string",
        "error while evaluating `ipaddr` extension function: invalid IP address: not-an-ip",
    ),
    (
        "R5R1-C23-02-ipaddr-invalid-octet",
        "error while evaluating `ipaddr` extension function: invalid IP address: 999.1.1.1",
    ),
    (
        "R5R1-C23-03-datetime-invalid-escape",
        "error while evaluating `datetime` extension function: invalid date pattern",
    ),
    (
        "R5R1-C23-04-decimal-invalid-escape",
        "error while evaluating `decimal` extension function: `not-a-number` is not a well-formed decimal value",
    ),
    (
        "R5R1-C23-05-ipaddr-invalid-implicit-escape",
        "error while evaluating `ipaddr` extension function: invalid IP address: not-an-ip",
    ),
]

MANAGED_INVALID_ENCODING = {
    "stage": "native-transport",
    "operationFailed": True,
    "decision": None,
    "hasErrors": True,
    "errorCategories": ["operation-error"],
    "cleanAllow": False,
}


def managed_invalid_encoding(message: str) -> dict:
    observation = copy.deepcopy(MANAGED_INVALID_ENCODING)
    observation["errors"] = [
        {
            "category": "operation-error",
            "message": message,
        }
    ]
    return observation


def fcn02(reference_observations: dict) -> dict:
    return {
        "id": "FCN-02",
        "title": "Malformed typed-context encoding",
        "cases": [case_id for case_id, _ in INVALID_ENCODING_CASES],
        "upstreamObservation": (
            "The reference side does not authorize the request. The ORIGINAL "
            "reference degrades the malformed ipaddr and datetime encodings to "
            "plain records and therefore denies cleanly; the malformed decimal "
            "escape is rejected while the request context is built and the "
            "reference denies with that diagnostic. The CORRECTED reference "
            "rejects every malformed typed encoding while the request context "
            "is built and produces no decision."
        ),
        "d3Observation": (
            "D3 rejects the malformed typed encoding at the request boundary "
            "and produces no decision. The diagnostic is the Cedar extension "
            "function's own error and is identical to the corrected "
            "reference's diagnostic."
        ),
        "basis": (
            "A malformed typed encoding must never gain validity. Cedar's "
            "reserved extension encoding is schema-directed: the `ipaddr`, "
            "`datetime` and `decimal` constructors are applied by the engine "
            "that owns the schema, and an invalid argument is an error, not a "
            "value. The pinned reference adapter has no schema-directed "
            "parsing at its JSON boundary and the pinned Dogwood runtime value "
            "model has no ipaddr or datetime variant, so the ORIGINAL "
            "reference degrades a malformed ipaddr or datetime encoding to a "
            "plain record and denies the request as if the value had simply "
            "not matched. That degradation is the same defect boundary as "
            "ED-06: the value never reaches Cedar as a typed value. The "
            "corrected reference keeps the typed value until Cedar applies the "
            "constructor, so the malformed value is rejected with the "
            "extension function's own error, exactly as D3 rejects it. Both "
            "sides authorize nothing, so the pair is a fail-closed "
            "equivalence in the authorization sense; the per-side observations "
            "below are machine-verified and record where each side refuses "
            "the request."
        ),
        "failClosedEffect": (
            "Neither side authorizes: the malformed encoding is never treated "
            "as a valid typed value, neither side produces an Allow, and the "
            "malformed input is rejected rather than coerced."
        ),
        "caseObservations": {
            case_id: {
                "upstream": reference_observations[case_id],
                "d3": managed_invalid_encoding(message),
            }
            for case_id, message in INVALID_ENCODING_CASES
        },
    }


FCN02_ORIGINAL = fcn02(
    {
        case_id: {
            "stage": "evaluation",
            "operationFailed": False,
            "decision": "deny",
            "hasErrors": False,
            "errorCategories": [],
            "cleanAllow": False,
        }
        if case_id != "R5R1-C23-04-decimal-invalid-escape"
        else {
            "stage": "evaluation",
            "operationFailed": False,
            "decision": "deny",
            "hasErrors": True,
            "errorCategories": ["policy-evaluation-error"],
            "cleanAllow": False,
            "errors": [
                "context build: error while evaluating `decimal` extension function: "
                "`not-a-number` is not a well-formed decimal value"
            ],
        }
        for case_id, _ in INVALID_ENCODING_CASES
    }
)

FCN02_CORRECTED = fcn02(
    {
        case_id: {
            "stage": "request-validation",
            "operationFailed": True,
            "decision": None,
            "hasErrors": True,
            "errorCategories": [],
            "cleanAllow": False,
            "error": f"current event is malformed: typed context: {message}",
        }
        for case_id, message in INVALID_ENCODING_CASES
    }
)

FCN03 = {
    "id": "FCN-03",
    "title": "Temporal history replay fails closed on a non-conforming entity",
    "cases": ["R5R1-C14-01-temporal-history-replay-conformance-failure"],
    "upstreamObservation": (
        "Evaluation-stage operation failure with no decision. The trusted "
        "history replay evaluates the history event inside the temporal "
        "pipeline; the history event carries a caller-supplied entity whose "
        "entity-typed attribute is a plain record instead of an entity "
        "reference, the entity does not satisfy the schema, and the replay "
        "fails before the current event is decided."
    ),
    "d3Observation": (
        "Trusted-binding operation failure with no decision. The native "
        "trusted-runtime binding replays the same history through the same "
        "pinned upstream, observes the same entity-conformance failure and "
        "refuses the binding, so no trusted context is produced."
    ),
    "basis": (
        "Qualification finding QD3Q-05 requires a C-14 observation that "
        "establishes a reachable evaluation-stage failure inside the temporal "
        "pipeline, as distinct from the trusted-binding rejections the R3/R4 "
        "corpus already carried and from a false temporal binding. This case "
        "makes the trusted history itself fail the entity-conformance check "
        "while the history is replayed for the temporal decision. Both sides "
        "report the same underlying upstream diagnostic and both refuse the "
        "request, so the pair is a fail-closed normalization, not an approved "
        "authorization difference. The residual limitation - that the pinned "
        "temporal engine's own error channel (`temporal evaluation: ...`) is "
        "not reachable from caller input in this stack - is recorded in the "
        "R5-R1 review record."
    ),
    "failClosedEffect": (
        "Neither side authorizes: the reference fails closed during the "
        "history replay and D3 fails closed at the trusted-binding stage. "
        "Neither side reports a decision, neither reports a clean Allow, and "
        "the temporal bindings are never produced."
    ),
    "caseObservations": {
        "R5R1-C14-01-temporal-history-replay-conformance-failure": {
            "upstream": {
                "stage": "evaluation",
                "operationFailed": True,
                "decision": None,
                "hasErrors": True,
                "errorCategories": [],
                "cleanAllow": False,
                "error": (
                    "history event `r5r1-hist-1` failed upstream evaluation: "
                    "entity conformance: entity does not conform to the schema"
                ),
            },
            "d3": {
                "stage": "trusted-binding",
                "operationFailed": True,
                "decision": None,
                "hasErrors": True,
                "errorCategories": ["trusted-binding-unavailable"],
                "cleanAllow": False,
                "errors": [
                    {
                        "category": "trusted-binding-unavailable",
                        "message": (
                            "The trusted temporal/provider binding failed closed: "
                            "history event `r5r1-hist-1` failed upstream evaluation: "
                            "entity conformance: entity does not conform to the schema"
                        ),
                    }
                ],
            },
        }
    },
}

FCN04 = {
    "id": "FCN-04",
    "title": "Temporal current-event evaluation fails closed on a non-conforming entity",
    "cases": ["R5R1-C14-02-temporal-current-event-conformance-failure"],
    "upstreamObservation": (
        "Evaluation-stage Deny with an entity-conformance diagnostic. The "
        "temporal pipeline has produced its bindings, the current event "
        "carries a caller-supplied entity whose entity-typed attribute is a "
        "plain record instead of an entity reference, and the entity-store "
        "construction fails closed with that diagnostic."
    ),
    "d3Observation": (
        "Trusted-binding operation failure with no decision. The native "
        "trusted-runtime binding observes the same entity-conformance failure "
        "on the current event and refuses the binding."
    ),
    "basis": (
        "This is the second reachable evaluation-stage failure inside the "
        "temporal pipeline: the history is well formed, the temporal bindings "
        "are computed, and the current event's entity store fails the "
        "schema-conformance check. The reference fails closed with a Deny "
        "that carries the conformance diagnostic; D3's trusted-runtime "
        "contract refuses the binding and produces no decision at all. "
        "Neither side authorizes, so the pair is a fail-closed normalization "
        "with machine-verified per-side observations, matching the FCN-01 "
        "precedent for the provider contract."
    ),
    "failClosedEffect": (
        "Neither side authorizes: the reference denies with the conformance "
        "diagnostic and D3 refuses the trusted binding. Neither side reports "
        "a clean Allow."
    ),
    "caseObservations": {
        "R5R1-C14-02-temporal-current-event-conformance-failure": {
            "upstream": {
                "stage": "evaluation",
                "operationFailed": False,
                "decision": "deny",
                "hasErrors": True,
                "errorCategories": ["entity-conformance"],
                "cleanAllow": False,
                "errors": ["entity conformance: entity does not conform to the schema"],
            },
            "d3": {
                "stage": "trusted-binding",
                "operationFailed": True,
                "decision": None,
                "hasErrors": True,
                "errorCategories": ["trusted-binding-unavailable"],
                "cleanAllow": False,
                "errors": [
                    {
                        "category": "trusted-binding-unavailable",
                        "message": (
                            "The trusted temporal/provider binding failed closed: "
                            "current event failed upstream evaluation: entity conformance: "
                            "entity does not conform to the schema"
                        ),
                    }
                ],
            },
        }
    },
}

ED06_NOTE = (
    "ED-06 is preserved in the R4 and R5 registers and in the frozen review "
    "records; it is not carried forward into the corrected-reference "
    "register. The corrected reference eliminates the divergence ED-06 "
    "approved, so the approval has nothing left to absorb and the two ED-06 "
    "cases are expected to agree. Retiring, superseding or retaining ED-06 "
    "is an owner decision: this commission records the recommendation and the "
    "executed evidence only, and it does not activate a revised contract."
)

MALFORMED_NOTE = (
    "The five malformed typed-encoding cases are not normalized against the "
    "ORIGINAL reference. That reference degrades a malformed ipaddr or "
    "datetime encoding to a plain record, so it denies the request cleanly "
    "instead of rejecting the malformed value (the ED-06 defect boundary), "
    "and it rejects a malformed decimal escape while the request context is "
    "built and denies with that diagnostic. The observations are materially "
    "different from D3's, which rejects every malformed typed encoding at the "
    "request boundary and produces no decision, so the cases are ordinary "
    "agreement expectations that the original reference fails. The corrected "
    "reference rejects the malformed encoding with the same diagnostic D3 "
    "reports, and the corrected-reference register carries the FCN-02 row "
    "that pins that fail-closed equivalence exactly."
)


def main() -> int:
    r4 = json.loads(R4_REGISTER.read_text(encoding="utf-8"))
    rows = {row["id"]: row for row in r4["differences"]}
    normalizations = {row["id"]: row for row in r4["failClosedNormalizations"]}

    original = {
        "schemaVersion": "3",
        "commission": "DOGWOOD-SHARP-D3-IMPL-01-R5-R1",
        "differences": [copy.deepcopy(rows[key]) for key in sorted(rows)],
        "failClosedNormalizations": [
            copy.deepcopy(normalizations["FCN-01"]),
            copy.deepcopy(FCN03),
            copy.deepcopy(FCN04),
        ],
        "notes": [
            "This register is the expectation model of the ORIGINAL pinned "
            "Dogwood reference. ED-01 through ED-06 and FCN-01 are carried "
            "forward verbatim from the frozen R4 register, so the historical "
            "ED-06 approval is exercised again unchanged.",
            MALFORMED_NOTE,
        ],
    }

    corrected = {
        "schemaVersion": "3",
        "commission": "DOGWOOD-SHARP-D3-IMPL-01-R5-R1",
        "differences": [
            copy.deepcopy(rows[key]) for key in sorted(rows) if key != "ED-06"
        ],
        "failClosedNormalizations": [
            copy.deepcopy(normalizations["FCN-01"]),
            copy.deepcopy(FCN02_CORRECTED),
            copy.deepcopy(FCN03),
            copy.deepcopy(FCN04),
        ],
        "notes": [ED06_NOTE],
    }

    # The carried-forward rows must be byte-identical to the frozen R4 rows.
    for register, excluded in ((original, set()), (corrected, {"ED-06"})):
        for row in register["differences"]:
            if row["id"] in excluded:
                continue
            if row != rows[row["id"]]:
                raise SystemExit(f"row `{row['id']}` drifted from the frozen R4 row")
        for row in register["failClosedNormalizations"]:
            if row["id"] in {"FCN-02", "FCN-03", "FCN-04"}:
                continue
            if row != normalizations[row["id"]]:
                raise SystemExit(f"row `{row['id']}` drifted from the frozen R4 row")

    # Every normalization row must be referenced by a case and every case must
    # reference a known row; the harness enforces the same rule at run time.
    referenced = {
        case.get("failClosedNormalizationId")
        for case in json.loads(
            (OUTPUT / "fixtures.json").read_text(encoding="utf-8")
        )["cases"]
    }
    declared = {row["id"] for row in original["failClosedNormalizations"]}
    if declared - referenced - {None}:
        raise SystemExit(
            f"the original register declares unreferenced rows: {sorted(declared - referenced)}"
        )

    (OUTPUT / "register.json").write_text(
        json.dumps(original, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (OUTPUT / "register-corrected-reference.json").write_text(
        json.dumps(corrected, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(
        "original-reference rows: "
        f"{[row['id'] for row in original['differences']]} + "
        f"{[row['id'] for row in original['failClosedNormalizations']]}"
    )
    print(
        "corrected-reference rows: "
        f"{[row['id'] for row in corrected['differences']]} + "
        f"{[row['id'] for row in corrected['failClosedNormalizations']]}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
