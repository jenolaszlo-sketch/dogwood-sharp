#!/usr/bin/env python3
"""Build OQ-01 targeted controls fixtures (TC-01 through TC-07).

These controls test the history-event policy evaluation error scenario
from multiple angles to establish the correct contract behavior.
"""

from __future__ import annotations

import copy
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
OUTPUT = HERE

SCHEMA = (
    "namespace Test { entity User; entity Item { owner: User }; "
    "action Approve appliesTo { principal: [User], resource: [Item], "
    "context: { input: { stock: String } } }; "
    "action Sell appliesTo { principal: [User], resource: [Item], "
    "context: { input: { stock: String } } }; }"
)

# Base policy: permit on Sell when formerly Approve with matching stock
# forbid on Approve when resource.owner == principal
BASE_POLICY = (
    'permit(principal, action == Test::Action::"Sell", resource)\n'
    "when temporal { formerly within 1h Test::Action::\"Approve\"::request { input.stock: context.input.stock } };\n"
    'forbid(principal, action == Test::Action::"Approve", resource) when { resource.owner == principal };'
)

# Valid current entity (with owner as entity reference)
VALID_CURRENT_ENTITY = {
    "uid": {"type": "Test::Item", "id": "item-1"},
    "attrs": {"owner": {"__entity": {"type": "Test::User", "id": "alice"}}},
    "parents": [],
}

# Invalid current entity (owner as plain record - fails conformance)
INVALID_CURRENT_ENTITY = {
    "uid": {"type": "Test::Item", "id": "item-1"},
    "attrs": {"owner": {"type": "Test::User", "id": "alice"}},
    "parents": [],
}

# Valid history entity (with owner)
VALID_HISTORY_ENTITY = {
    "uid": {"type": "Test::Item", "id": "item-1"},
    "attrs": {"owner": {"__entity": {"type": "Test::User", "id": "alice"}}},
    "parents": [],
}

# Invalid history entity (missing owner entirely)
INVALID_HISTORY_ENTITY_MISSING_OWNER = {
    "uid": {"type": "Test::Item", "id": "item-1"},
    "attrs": {},
    "parents": [],
}

# Invalid history entity (owner as plain record)
INVALID_HISTORY_ENTITY_PLAIN_RECORD = {
    "uid": {"type": "Test::Item", "id": "item-1"},
    "attrs": {"owner": {"type": "Test::User", "id": "alice"}},
    "parents": [],
}

def history_event(identity: str, timestamp: int, entities: list, context_stock: str = "AMZN") -> dict:
    return {
        "identity": identity,
        "timestamp": timestamp,
        "actionNamespace": ["Test", "Action"],
        "actionId": "Approve",
        "kind": "request",
        "principal": {"type": "Test::User", "id": "alice"},
        "resource": {"type": "Test::Item", "id": "item-1"},
        "context": {"input": {"stock": context_stock}},
        "logged": {"input": {"stock": context_stock}},
        "entities": entities,
    }

def current_event(timestamp: int, entities: list, context_stock: str = "AMZN") -> dict:
    return {
        "timestamp": timestamp,
        "actionNamespace": ["Test", "Action"],
        "actionId": "Sell",
        "principal": {"type": "Test::User", "id": "alice"},
        "resource": {"type": "Test::Item", "id": "item-1"},
        "entities": entities,
        "context": {"input": {"stock": context_stock}},
    }

# TC-01: Valid history event, valid current entity
TC01 = {
    "id": "R5R3-TC-01-valid-history-valid-current",
    "kind": "differential",
    "control": "valid-history-valid-current",
    "families": ["OQ-01"],
    "question": "Valid history and current entities - both sides should allow",
    "schema": "temporalOwner",
    "distincter": "oq01tc",
    "source": BASE_POLICY,
    "expectedClassification": "agreement",
    "expectedDifferenceIds": [],
    "intendedStage": "evaluation",
    "trustedRuntime": True,
    "event": current_event(100, [copy.deepcopy(VALID_CURRENT_ENTITY)]),
    "history": [history_event("h-1", 10, [copy.deepcopy(VALID_HISTORY_ENTITY)])],
}

# TC-02: History event missing temporal attribute (stock), valid current entity
TC02 = {
    "id": "R5R3-TC-02-history-missing-temporal-attr",
    "kind": "differential",
    "control": "history-missing-temporal-attr",
    "families": ["OQ-01"],
    "question": "History event missing temporal attribute (stock) - temporal leaf false, current entity valid",
    "schema": "temporalOwner",
    "distincter": "oq01tc",
    "source": BASE_POLICY,
    "expectedClassification": "agreement",
    "expectedDifferenceIds": [],
    "intendedStage": "evaluation",
    "trustedRuntime": True,
    "event": current_event(100, [copy.deepcopy(VALID_CURRENT_ENTITY)]),
    "history": [history_event("h-1", 10, [copy.deepcopy(VALID_HISTORY_ENTITY)], context_stock="GOOG")],  # Different stock
}

# TC-03: History event missing owner, valid current entity, forbid on history action
TC03 = {
    "id": "R5R3-TC-03-history-missing-owner-forbid-on-history",
    "kind": "differential",
    "control": "history-missing-owner-forbid-on-history",
    "families": ["OQ-01"],
    "question": "History event missing owner attribute, forbid on Approve action - reference should not evaluate forbid against history",
    "schema": "temporalOwner",
    "distincter": "oq01tc",
    "source": BASE_POLICY,
    "expectedClassification": "agreement",
    "expectedDifferenceIds": [],
    "intendedStage": "evaluation",
    "trustedRuntime": True,
    "event": current_event(100, [copy.deepcopy(VALID_CURRENT_ENTITY)]),
    "history": [history_event("h-1", 10, [copy.deepcopy(INVALID_HISTORY_ENTITY_MISSING_OWNER)])],
}

# TC-04: History event missing owner, current event missing owner
TC04 = {
    "id": "R5R3-TC-04-both-missing-owner",
    "kind": "differential",
    "control": "both-missing-owner",
    "families": ["OQ-01"],
    "question": "Both history and current entities missing owner - current event conformance failure should deny",
    "schema": "temporalOwner",
    "distincter": "oq01tc",
    "source": BASE_POLICY,
    "expectedClassification": "agreement",
    "expectedDifferenceIds": [],
    "intendedStage": "evaluation",
    "trustedRuntime": True,
    "event": current_event(100, [copy.deepcopy(INVALID_CURRENT_ENTITY)]),
    "history": [history_event("h-1", 10, [copy.deepcopy(INVALID_HISTORY_ENTITY_MISSING_OWNER)])],
}

# TC-05: Mixed history - one valid, one missing owner
TC05 = {
    "id": "R5R3-TC-05-mixed-history-one-valid",
    "kind": "differential",
    "control": "mixed-history-one-valid",
    "families": ["OQ-01"],
    "question": "Two history events, one valid one missing owner - valid one should satisfy temporal",
    "schema": "temporalOwner",
    "distincter": "oq01tc",
    "source": BASE_POLICY,
    "expectedClassification": "agreement",
    "expectedDifferenceIds": [],
    "intendedStage": "evaluation",
    "trustedRuntime": True,
    "event": current_event(100, [copy.deepcopy(VALID_CURRENT_ENTITY)]),
    "history": [
        history_event("h-1", 10, [copy.deepcopy(VALID_HISTORY_ENTITY)]),
        history_event("h-2", 20, [copy.deepcopy(INVALID_HISTORY_ENTITY_MISSING_OWNER)]),
    ],
}

# TC-06: Permit + forbid on same history action, history missing owner
# This is essentially the same as TC-03 but with the policy structure emphasized
TC06 = {
    "id": "R5R3-TC-06-permit-forbid-same-action-history-missing-owner",
    "kind": "differential",
    "control": "permit-forbid-same-action-history-missing-owner",
    "families": ["OQ-01"],
    "question": "Permit and forbid on same Approve action, history missing owner - forbid should not be evaluated against history",
    "schema": "temporalOwner",
    "distincter": "oq01tc",
    "source": BASE_POLICY,
    "expectedClassification": "agreement",
    "expectedDifferenceIds": [],
    "intendedStage": "evaluation",
    "trustedRuntime": True,
    "event": current_event(100, [copy.deepcopy(VALID_CURRENT_ENTITY)]),
    "history": [history_event("h-1", 10, [copy.deepcopy(INVALID_HISTORY_ENTITY_PLAIN_RECORD)])],
}

# TC-07: No valid authorization basis (temporal false, no permit)
TC07 = {
    "id": "R5R3-TC-07-no-valid-basis",
    "kind": "differential",
    "control": "no-valid-basis",
    "families": ["OQ-01"],
    "question": "History event stock doesn't match, no permit applies - should deny",
    "schema": "temporalOwner",
    "distincter": "oq01tc",
    "source": BASE_POLICY,
    "expectedClassification": "agreement",
    "expectedDifferenceIds": [],
    "intendedStage": "evaluation",
    "trustedRuntime": True,
    "event": current_event(100, [copy.deepcopy(VALID_CURRENT_ENTITY)], context_stock="GOOG"),  # Different stock
    "history": [history_event("h-1", 10, [copy.deepcopy(VALID_HISTORY_ENTITY)], context_stock="AMZN")],  # Different stock
}

ALL_CONTROLS = [TC01, TC02, TC03, TC04, TC05, TC06, TC07]

FIXTURES = {
    "schemaVersion": "3",
    "schemas": {"temporalOwner": SCHEMA},
    "cases": ALL_CONTROLS,
    "familyRequirements": {
        "OQ-01": {
            "requiredControls": [c["control"] for c in ALL_CONTROLS],
            "decisionCoverage": "exempt",
        }
    },
}

REGISTER = {
    "schemaVersion": "3",
    "commission": "DOGWOOD-SHARP-D3-IMPL-01-R5-R3",
    "differences": [],
    "failClosedNormalizations": [],
    "notes": [
        "OQ-01 targeted controls TC-01 through TC-07. These test the history-event",
        "policy evaluation error scenario from multiple angles. The register carries",
        "no approved differences or fail-closed normalizations - these are ordinary",
        "agreement expectations that verify the proposed contract decision.",
    ],
}


def main() -> int:
    (OUTPUT / "fixtures.json").write_text(
        json.dumps(FIXTURES, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (OUTPUT / "register.json").write_text(
        json.dumps(REGISTER, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"Built {len(ALL_CONTROLS)} OQ-01 targeted controls")
    for c in ALL_CONTROLS:
        print(f"  {c['id']}: {c['control']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())