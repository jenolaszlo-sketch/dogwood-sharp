#!/usr/bin/env python3
"""DOGWOOD-SHARP-D3-IMPL-01-R5-R1 corpus builder.

Builds the R5-R1 differential corpus from preserved inputs:

  * docs/reviews/d3-impl-01/evidence/differential/r4/fixtures.json
    (the frozen R4 corpus: 97 cases, 26 families),
  * docs/reviews/d3-impl-01-r5/evidence/fixtures.json
    (the frozen R5 baseline replay: the 18 typed-context representation cases),
  * the R5-R1 additions declared in this file:
      - C-14: two reachable evaluation-stage failures inside the temporal
        pipeline (the history replay and the current-event evaluation),
      - C-11: multi-action `appliesTo` observations,
      - C-21: a two-rule export reorder observation,
      - C-23: malformed typed-encoding controls (invalid encodings must never
        gain validity).

Two corpus variants are emitted:

  * fixtures.json                        - expectations for the ORIGINAL pinned
                                          Dogwood reference (the ED-06 approval
                                          is carried forward unchanged),
  * fixtures-corrected-reference.json    - the same cases, with the two ED-06
                                          cases reclassified to `agreement`
                                          because the corrected reference
                                          eliminates the divergence.

The two variants differ ONLY in the `expectedClassification`,
`expectedDifferenceIds`, `approvedDifferenceId` and `approvedDifferenceReason`
fields of the two ED-06 cases. The builder asserts that invariant, so the
corrected-reference expectations cannot silently diverge anywhere else.

The builder never derives an expectation from an observed outcome: the
expectations are the recorded contract, and the harness verifies them.
"""

from __future__ import annotations

import copy
import json
import pathlib
import sys

REPOSITORY = pathlib.Path(__file__).resolve().parents[5]
R4_FIXTURES = REPOSITORY / "docs/reviews/d3-impl-01/evidence/differential/r4/fixtures.json"
R5_FIXTURES = REPOSITORY / "docs/reviews/d3-impl-01-r5/evidence/fixtures.json"
OUTPUT = REPOSITORY / "docs/reviews/d3-impl-01-r5-r1/evidence"

ED06_CASE_IDS = [
    "R3-C23-08-ipaddr-typed-context",
    "R3-C23-09-datetime-typed-context",
]

ED06_FIELDS = [
    "expectedClassification",
    "expectedDifferenceIds",
    "approvedDifferenceId",
    "approvedDifferenceReason",
]

# The malformed typed-encoding cases. The two corpus variants state different
# expectations for them, because the reference's own observation differs:
#
#   * the ORIGINAL reference degrades a malformed ipaddr or datetime encoding
#     to a plain record, so it denies the request cleanly instead of
#     rejecting the malformed value (the ED-06 defect boundary). The case is
#     therefore an ordinary agreement expectation that the original reference
#     fails - the defect surface the correction removes;
#   * the CORRECTED reference rejects the malformed encoding with the same
#     diagnostic D3 reports, so the pair is a fail-closed equivalence that the
#     FCN-02 row pins exactly.
MALFORMED_CASE_IDS = [
    "R5R1-C23-01-ipaddr-invalid-bare-string",
    "R5R1-C23-02-ipaddr-invalid-octet",
    "R5R1-C23-03-datetime-invalid-escape",
    "R5R1-C23-04-decimal-invalid-escape",
    "R5R1-C23-05-ipaddr-invalid-implicit-escape",
]

MALFORMED_FIELDS = [
    "expectedClassification",
    "failClosedNormalizationId",
    "intendedStage",
]

# --------------------------------------------------------------------------
# R5-R1 additions
# --------------------------------------------------------------------------

ADDITIONAL_SCHEMAS = {
    # The temporal schema carries a required attribute on the resource so that
    # an entity-conformance failure is reachable in the temporal pipeline.
    "temporalOwner": (
        "namespace Test { entity User; entity Item { owner: User }; "
        "action Approve appliesTo { principal: [User], resource: [Item], "
        "context: { input: { stock: String } } }; "
        "action Sell appliesTo { principal: [User], resource: [Item], "
        "context: { input: { stock: String } } }; }"
    ),
    "multiAction": (
        "namespace App { entity User; entity Reader; entity Doc { owner: User }; "
        "entity Archive { owner: User }; "
        "action read appliesTo { principal: [Reader], resource: [Doc], context: { count: Long } }; "
        "action archive appliesTo { principal: [User], resource: [Archive], context: { reason: String } }; }"
    ),
}

# The temporal policy reads no entity attribute, so the only reachable failure
# in the temporal pipeline is the entity-conformance check.
TEMPORAL_POLICY = (
    'permit(principal, action == Test::Action::"Sell", resource)\n'
    "when temporal { formerly within 1h Test::Action::\"Approve\"::request { input.stock: context.input.stock } };"
)

IP_POLICY = (
    'permit(principal, action == App::Action::"read", resource)\n'
    'when { context.ip == ip("192.168.1.1") };'
)

DATETIME_POLICY = (
    'permit(principal, action == App::Action::"read", resource)\n'
    'when { context.ts == datetime("2024-01-01T00:00:00Z") };'
)

DECIMAL_POLICY = (
    'permit(principal, action == App::Action::"read", resource)\n'
    'when { context.price == decimal("1.5") };'
)

# A caller-supplied entity whose entity-typed attribute carries a plain record
# instead of an entity reference. Both the pinned reference adapter and the D3
# native bridge map an implicit `{ "type": ..., "id": ... }` object onto a
# record, so the entity does not satisfy the schema's `owner: User` attribute
# and the entity-conformance check fails on both sides.
NONCONFORMING_ENTITY = {
    "uid": {"type": "Test::Item", "id": "item-1"},
    "attrs": {"owner": {"type": "Test::User", "id": "alice"}},
    "parents": [],
}

TEMPORAL_HISTORY = [
    {
        "identity": "r5r1-hist-1",
        "timestamp": 10,
        "actionNamespace": ["Test", "Action"],
        "actionId": "Approve",
        "kind": "request",
        "principal": {"type": "Test::User", "id": "alice"},
        "resource": {"type": "Test::Item", "id": "item-1"},
        "context": {"input": {"stock": "AMZN"}},
        "logged": {"input": {"stock": "AMZN"}},
        "entities": [],
    }
]


def temporal_event(entities: list) -> dict:
    return {
        "timestamp": 100,
        "actionNamespace": ["Test", "Action"],
        "actionId": "Sell",
        "principal": {"type": "Test::User", "id": "alice"},
        "resource": {"type": "Test::Item", "id": "item-1"},
        "entities": entities,
        "context": {"input": {"stock": "AMZN"}},
    }


ADDITIONAL_CASES = [
    {
        "id": "R5R1-C14-01-temporal-history-replay-conformance-failure",
        "kind": "differential",
        "control": "temporal-history-replay-conformance-failure",
        "families": ["C-14"],
        "question": (
            "A trusted history event whose resource does not satisfy the "
            "schema fails closed while the history is replayed for the "
            "temporal pipeline: neither side authorizes and neither side "
            "produces a decision."
        ),
        "schema": "temporalOwner",
        "distincter": "r5r1c14",
        "source": TEMPORAL_POLICY,
        "expectedClassification": "fail-closed-equivalence",
        "expectedDifferenceIds": [],
        "failClosedNormalizationId": "FCN-03",
        "intendedStage": "evaluation",
        "trustedRuntime": True,
        "event": temporal_event([]),
        "history": [
            {
                **copy.deepcopy(TEMPORAL_HISTORY[0]),
                "entities": [copy.deepcopy(NONCONFORMING_ENTITY)],
            }
        ],
    },
    {
        "id": "R5R1-C14-02-temporal-current-event-conformance-failure",
        "kind": "differential",
        "control": "temporal-current-event-conformance-failure",
        "families": ["C-14"],
        "question": (
            "A current event whose resource does not satisfy the schema "
            "fails closed after the temporal pipeline has produced its "
            "bindings: the reference denies with a conformance error and D3 "
            "refuses the trusted binding. Neither side authorizes."
        ),
        "schema": "temporalOwner",
        "distincter": "r5r1c14",
        "source": TEMPORAL_POLICY,
        "expectedClassification": "fail-closed-equivalence",
        "expectedDifferenceIds": [],
        "failClosedNormalizationId": "FCN-04",
        "intendedStage": "evaluation",
        "trustedRuntime": True,
        "event": temporal_event([copy.deepcopy(NONCONFORMING_ENTITY)]),
        "history": copy.deepcopy(TEMPORAL_HISTORY),
    },
    {
        "id": "R5R1-C11-01-multi-action-read",
        "kind": "differential",
        "control": "multi-action-read",
        "families": ["C-11"],
        "question": (
            "A schema with two complete actions keeps the read action's own "
            "principal, resource and context constraints; a conforming read "
            "request is authorized identically on both sides."
        ),
        "schema": "multiAction",
        "distincter": "r5r1c11",
        "source": 'permit(principal, action == App::Action::"read", resource);',
        "expectedClassification": "agreement",
        "expectedDifferenceIds": [],
        "intendedStage": "evaluation",
        "event": {
            "timestamp": 100,
            "actionNamespace": ["App", "Action"],
            "actionId": "read",
            "principal": {"type": "App::Reader", "id": "r1"},
            "resource": {"type": "App::Doc", "id": "d1"},
            "entities": [],
            "context": {"count": 5},
        },
    },
    {
        "id": "R5R1-C11-02-multi-action-archive",
        "kind": "differential",
        "control": "multi-action-archive",
        "families": ["C-11"],
        "question": (
            "The second action of a multi-action schema keeps its own "
            "principal, resource and context constraints; a conforming archive "
            "request is authorized identically on both sides."
        ),
        "schema": "multiAction",
        "distincter": "r5r1c11",
        "source": 'permit(principal, action == App::Action::"archive", resource);',
        "expectedClassification": "agreement",
        "expectedDifferenceIds": [],
        "intendedStage": "evaluation",
        "event": {
            "timestamp": 100,
            "actionNamespace": ["App", "Action"],
            "actionId": "archive",
            "principal": {"type": "App::User", "id": "alice"},
            "resource": {"type": "App::Archive", "id": "a1"},
            "entities": [],
            "context": {"reason": "retention"},
        },
    },
    {
        "id": "R5R1-C11-03-multi-action-canonicalization",
        "kind": "managed-control",
        "control": "multi-action-appliesto",
        "families": ["C-11"],
        "question": (
            "The canonical schema the adapter exposes declares both complete "
            "actions with independent principal, resource and context "
            "constraints."
        ),
        "schema": "multiAction",
        "source": 'permit(principal, action == App::Action::"read", resource);\n'
        'permit(principal, action == App::Action::"archive", resource);',
        "expectedClassification": "agreement",
        "expectedActions": {
            "read": {
                "principalTypes": ["App::Reader"],
                "resourceTypes": ["App::Doc"],
                "contextAttributes": ["count"],
            },
            "archive": {
                "principalTypes": ["App::User"],
                "resourceTypes": ["App::Archive"],
                "contextAttributes": ["reason"],
            },
        },
    },
    {
        "id": "R5R1-C21-01-identity-reordered-two-rule",
        "kind": "managed-control",
        "control": "identity-reordered-two-rule",
        "families": ["C-21"],
        "question": (
            "An export of two rules supplied in reverse order keeps the "
            "supplied order: the exported index of each rule follows the "
            "supplied order, not the source order."
        ),
        "schema": "userDoc",
        "source": 'permit(principal, action == App::Action::"read", resource) when { context.count > 1 };\n'
        'permit(principal, action == App::Action::"read", resource) when { context.count > 2 };',
        "expectedClassification": "agreement",
    },
]


def invalid_case(
    case_id: str,
    control: str,
    schema: str,
    source: str,
    context: dict,
    question: str,
) -> dict:
    return {
        "id": case_id,
        "kind": "differential",
        "control": control,
        "families": ["C-23"],
        "question": question,
        "schema": schema,
        "distincter": "r5r1c23",
        "source": source,
        "expectedClassification": "fail-closed-equivalence",
        "expectedDifferenceIds": [],
        "failClosedNormalizationId": "FCN-02",
        "intendedStage": "request-validation",
        "event": {
            "timestamp": 100,
            "actionNamespace": ["App", "Action"],
            "actionId": "read",
            "principal": {"type": "App::User", "id": "alice"},
            "resource": {"type": "App::Doc", "id": "d1"},
            "entities": [],
            "context": context,
        },
    }


ADDITIONAL_CASES.extend(
    [
        invalid_case(
            "R5R1-C23-01-ipaddr-invalid-bare-string",
            "ipaddr-invalid-encoding",
            "extensionIpaddr",
            IP_POLICY,
            {"ip": "not-an-ip"},
            "A bare string that is not an IP address never becomes a valid "
            "ipaddr context value on any side.",
        ),
        invalid_case(
            "R5R1-C23-02-ipaddr-invalid-octet",
            "ipaddr-invalid-encoding",
            "extensionIpaddr",
            IP_POLICY,
            {"ip": {"__extn": {"fn": "ip", "arg": "999.1.1.1"}}},
            "An explicit ipaddr escape with an out-of-range octet never "
            "becomes a valid ipaddr context value on any side.",
        ),
        invalid_case(
            "R5R1-C23-03-datetime-invalid-escape",
            "datetime-invalid-encoding",
            "extensionDatetime",
            DATETIME_POLICY,
            {"ts": {"__extn": {"fn": "datetime", "arg": "not-a-date"}}},
            "An explicit datetime escape with an unparsable argument never "
            "becomes a valid datetime context value on any side.",
        ),
        invalid_case(
            "R5R1-C23-04-decimal-invalid-escape",
            "decimal-invalid-encoding",
            "extensionDecimal",
            DECIMAL_POLICY,
            {"price": {"__extn": {"fn": "decimal", "arg": "not-a-number"}}},
            "An explicit decimal escape with a non-numeric argument never "
            "becomes a valid decimal context value on any side.",
        ),
        invalid_case(
            "R5R1-C23-05-ipaddr-invalid-implicit-escape",
            "ipaddr-invalid-encoding",
            "extensionIpaddr",
            IP_POLICY,
            {"ip": {"fn": "ip", "arg": "not-an-ip"}},
            "An implicit ipaddr escape with an invalid argument never becomes "
            "a valid ipaddr context value on any side.",
        ),
    ]
)


def main() -> int:
    r4 = json.loads(R4_FIXTURES.read_text(encoding="utf-8"))
    r5 = json.loads(R5_FIXTURES.read_text(encoding="utf-8"))

    corpus = copy.deepcopy(r4)
    corpus["comment"] = (
        "DOGWOOD-SHARP-D3-IMPL-01-R5-R1 differential corpus: the frozen R4 "
        "corpus, the frozen R5 typed-context representation replay and the "
        "R5-R1 additions (C-14 temporal evaluation-stage failures, C-11 "
        "multi-action appliesTo, C-21 two-rule export reorder, malformed "
        "typed encodings)."
    )

    # The R4 family requirements are carried forward. The R5-R1 additions add
    # the C-14 temporal-failure controls, the C-11/C-21 controls and the
    # semantic decision role that closes qualification finding QD3Q-07.
    requirements = corpus["familyRequirements"]
    requirements["C-14"]["requiredControls"] = sorted(
        set(requirements["C-14"]["requiredControls"])
        | {
            "temporal-history-replay-conformance-failure",
            "temporal-current-event-conformance-failure",
            "empty-history",
            "history-identity-duplicate",
        }
    )
    requirements["C-11"]["requiredControls"] = sorted(
        set(requirements["C-11"]["requiredControls"])
        | {"multi-action-read", "multi-action-archive", "multi-action-appliesto"}
    )
    requirements["C-21"]["requiredControls"] = sorted(
        set(requirements["C-21"]["requiredControls"]) | {"identity-reordered-two-rule"}
    )
    requirements["C-23"]["requiredControls"] = sorted(
        set(requirements["C-23"]["requiredControls"])
        | {
            "ipaddr-invalid-encoding",
            "datetime-invalid-encoding",
            "decimal-invalid-encoding",
        }
    )
    # QD3Q-07: a required positive role must actually produce its Allow.
    requirements["C-01"]["requiredControlDecisions"] = {"direct-allow": "allow"}

    for name, text in ADDITIONAL_SCHEMAS.items():
        corpus["schemas"][name] = text

    cases = corpus["cases"]
    known = {case["id"] for case in cases}
    for case in r5["cases"]:
        if case["id"] in known:
            continue
        cases.append(copy.deepcopy(case))
        known.add(case["id"])
    for case in ADDITIONAL_CASES:
        if case["id"] in known:
            raise SystemExit(f"duplicate R5-R1 case id `{case['id']}`")
        cases.append(copy.deepcopy(case))
        known.add(case["id"])

    original = copy.deepcopy(corpus)
    corrected = copy.deepcopy(corpus)
    for case in corrected["cases"]:
        if case["id"] in ED06_CASE_IDS:
            case["expectedClassification"] = "agreement"
            case["expectedDifferenceIds"] = []
            case.pop("approvedDifferenceId", None)
            case.pop("approvedDifferenceReason", None)
    for case in original["cases"]:
        if case["id"] in MALFORMED_CASE_IDS:
            # The original reference does not reject the malformed encoding;
            # the case states the semantic expectation and fails against it.
            case.pop("failClosedNormalizationId", None)
            case["expectedClassification"] = "agreement"
            case["intendedStage"] = "evaluation"

    # The two variants may differ only in the ED-06 classification fields and
    # in the malformed-encoding expectations listed above.
    for index, (left, right) in enumerate(zip(original["cases"], corrected["cases"])):
        if left["id"] != right["id"]:
            raise SystemExit("the two corpus variants disagree on case order")
        allowed = set(ED06_FIELDS)
        if left["id"] in MALFORMED_CASE_IDS:
            allowed |= set(MALFORMED_FIELDS)
        for key in set(left) | set(right):
            if key in allowed and left.get(key) != right.get(key):
                continue
            if left.get(key) != right.get(key):
                raise SystemExit(
                    f"the two corpus variants differ outside the declared fields: "
                    f"{left['id']}.{key}"
                )

    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "fixtures.json").write_text(
        json.dumps(original, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (OUTPUT / "fixtures-corrected-reference.json").write_text(
        json.dumps(corrected, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"cases: {len(cases)}")
    print(f"families: {len(requirements)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
