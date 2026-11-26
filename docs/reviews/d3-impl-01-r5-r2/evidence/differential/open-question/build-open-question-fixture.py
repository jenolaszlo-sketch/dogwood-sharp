#!/usr/bin/env python3
"""Build the dedicated R5-R1 open-question evidence fixtures.

The case in this manifest is NOT part of the R5-R1 differential corpus. It is
a divergence that the commission could not resolve without an owner decision,
so it is executed on its own, against both references and D3, and recorded as
an open question rather than normalized or approved.

The divergence
--------------
A history event whose resource entity does not carry the `owner` attribute
makes the pinned reference's history-event policy evaluation fail. The
reference therefore aborts the whole authorization with no decision:

    history event `h-1` failed upstream evaluation: error while evaluating
    policy `expc14_1`: `Test::Item::"item-1"` does not have the attribute
    `owner`

D3 does not evaluate the policy set against the history event. The native
trusted-runtime binding computes the temporal leaf from the recorded trace
and D3's bridge evaluates the history events with a deny-only capture engine,
so a history-event policy error is never surfaced and D3 authorizes the
current request.

The upstream temporal guide states that a failed operand resolves to a false
comparison, which is the behavior D3 implements. The reference's abort is
therefore not evidence of a D3 defect, but the two sides do not agree, and
the contract question - must a history-event policy evaluation error abort the
current authorization, or may it be confined to the history event - belongs
to the owner. The commission records the question and the executed evidence;
it does not decide it and it does not normalize the divergence.

The dedicated run is expected to exit non-zero: the divergence is real and
unnormalized by design.
"""

from __future__ import annotations

import copy
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent

SCHEMA = (
    "namespace Test { entity User; entity Item { owner: User }; "
    "action Approve appliesTo { principal: [User], resource: [Item], "
    "context: { input: { stock: String } } }; "
    "action Sell appliesTo { principal: [User], resource: [Item], "
    "context: { input: { stock: String } } }; }"
)

POLICY = (
    'permit(principal, action == Test::Action::"Sell", resource)\n'
    "when temporal { formerly within 1h Test::Action::\"Approve\"::request "
    "{ input.stock: context.input.stock } };\n"
    'forbid(principal, action == Test::Action::"Approve", resource) '
    "when { resource.owner == principal };"
)

# The current event carries a conforming entity: `owner` is a real entity
# reference, so the current event itself is fine.
CURRENT_ENTITY = {
    "uid": {"type": "Test::Item", "id": "item-1"},
    "attrs": {
        "owner": {"__entity": {"type": "Test::User", "id": "alice"}},
    },
    "parents": [],
}

# The history event carries no entity at all, so the resource entity that the
# reference evaluates the `forbid` rule against does not have the `owner`
# attribute. That is the history-event policy evaluation error.
HISTORY_EVENT = {
    "identity": "h-1",
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

CURRENT_EVENT = {
    "timestamp": 100,
    "actionNamespace": ["Test", "Action"],
    "actionId": "Sell",
    "principal": {"type": "Test::User", "id": "alice"},
    "resource": {"type": "Test::Item", "id": "item-1"},
    "entities": [copy.deepcopy(CURRENT_ENTITY)],
    "context": {"input": {"stock": "AMZN"}},
}

FIXTURES = {
    "schemaVersion": "3",
    "schemas": {"temporalOwner": SCHEMA},
    "cases": [
        {
            "id": "R5R1-OQ-01-temporal-history-event-policy-evaluation-error",
            "kind": "differential",
            "control": "temporal-history-event-policy-evaluation-error",
            "families": ["OQ-01"],
            "question": (
                "A trusted history event whose resource entity lacks the "
                "`owner` attribute makes the reference abort the whole "
                "authorization while evaluating the policy set against the "
                "history event. D3 computes the temporal leaf from the trace "
                "and never surfaces a history-event policy error, so D3 "
                "authorizes. Must a history-event policy evaluation error "
                "abort the current authorization?"
            ),
            "schema": "temporalOwner",
            "distincter": "expc14",
            "source": POLICY,
            "expectedClassification": "unexpected-difference",
            "expectedDifferenceIds": [],
            "intendedStage": "evaluation",
            "trustedRuntime": True,
            "event": copy.deepcopy(CURRENT_EVENT),
            "history": [copy.deepcopy(HISTORY_EVENT)],
        }
    ],
    "familyRequirements": {
        "OQ-01": {
            "requiredControls": ["temporal-history-event-policy-evaluation-error"],
            # The family is deliberately exempt from decision coverage: the
            # whole point of the dedicated run is that the two sides do not
            # agree and the divergence is not approved.
            "decisionCoverage": "exempt",
        }
    },
}

REGISTER = {
    "schemaVersion": "3",
    "commission": "DOGWOOD-SHARP-D3-IMPL-01-R5-R1",
    "differences": [],
    "failClosedNormalizations": [],
    "notes": [
        "The dedicated open-question run registers no approved difference and "
        "no fail-closed normalization. The divergence it records is an owner "
        "decision, not an approval: registering either would silently decide "
        "the question.",
        "The R5-R1 differential corpus and its registers are unaffected by "
        "this manifest and keep their own approvals.",
    ],
}


def main() -> int:
    (HERE / "fixtures.json").write_text(
        json.dumps(FIXTURES, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (HERE / "register.json").write_text(
        json.dumps(REGISTER, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"cases: {len(FIXTURES['cases'])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
