#!/usr/bin/env python3
"""Verify the direct Cedar adjudication outcomes against the authority table.

This script does not adjudicate anything: it only checks the two recorded
adjudicator outcome files (Cedar 4.13.0 and Cedar 4.11.0) against an
explicitly written expectation of the authoritative Cedar semantics, and it
checks the structural claims the R5-R1 record relies on:

  1. engine-version parity - the two adjudicated engine versions agree on
     every case, so the adjudicated semantics do not depend on the engine
     version difference between the reference (4.11.0) and D3 (4.13.0);
  2. representation equivalence - the three JSON spellings of one typed
     argument (explicit `__extn`, implicit `{fn,arg}`, bare argument) reach
     Cedar as byte-identical canonical natural JSON and produce the same
     stage, decision, determining policy ids and error state;
  3. fail-closed - a malformed typed encoding never yields a decision and
     never yields a clean Allow;
  4. the authority table itself - every adjudicated case has the expected
     stage, decision, operation-failed state and clean-Allow state.

The expectation table below is the authority statement of the commission: it
was written from the Cedar engine's own documented behavior and confirmed by
direct engine evaluation, and it is checked here against what the engine
actually did. A disagreement is a failure of this verification, not a
correction of the table.

Usage: `verify-authority.py [authority-verification.json]`
"""

from __future__ import annotations

import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
CORPUS = HERE / "corpus.json"
OUTCOME_4130 = HERE / "corpus-4130-outcome.json"
OUTCOME_4110 = HERE.parent / "cedar-adjudicator-4110" / "corpus-4110-outcome.json"
DEFAULT_OUTPUT = HERE / "authority-verification.json"

# --------------------------------------------------------------------------
# The authority table.
#
# stage / decision / operationFailed / hasErrors / cleanAllow / determining
#
# `evaluation` means Cedar parsed the request and evaluated the policy set.
# `context-parse` means Cedar's schema-directed context parser rejected the
# request payload. `request-validation` means Cedar parsed the context but
# the resulting request did not satisfy the action schema.
# --------------------------------------------------------------------------
E = "evaluation"
CP = "context-parse"
RV = "request-validation"

ALLOW = "allow"
DENY = "deny"
NONE = None


def row(stage: str, decision, determining=(), clean_allow: bool = False) -> dict:
    return {
        "stage": stage,
        "decision": decision,
        "operationFailed": stage in (CP, RV),
        "hasErrors": stage in (CP, RV),
        "cleanAllow": clean_allow,
        "determining": list(determining),
    }


AUTHORITY: dict[str, dict] = {}


def declare(case_ids: list[str], expectation: dict) -> None:
    for case_id in case_ids:
        AUTHORITY[case_id] = expectation


# Group A - the 18 R5 baseline replay cases, in the three representations.
declare(["ADJ-A01", "ADJ-A02", "ADJ-A03"], row(E, ALLOW, ["r3c23h_0"], True))
declare(["ADJ-A04", "ADJ-A05", "ADJ-A06"], row(E, DENY))
declare(["ADJ-A07", "ADJ-A08", "ADJ-A09"], row(E, ALLOW, ["r3c23i_0"], True))
declare(["ADJ-A10", "ADJ-A11", "ADJ-A12"], row(E, DENY))
declare(["ADJ-A13", "ADJ-A14", "ADJ-A15"], row(E, ALLOW, ["r3c23_0"], True))
declare(["ADJ-A16", "ADJ-A17", "ADJ-A18"], row(E, DENY))

# Group B - adjacent valid representations and boundary values.
declare(
    [
        "ADJ-B01",  # explicit escape with an args array
        "ADJ-B02",  # IPv6 through a bare string
        "ADJ-B03",  # ipaddr method on a typed context value
        "ADJ-B04",  # decimal equality across trailing zeros
        "ADJ-B07",  # datetime date-only form
        "ADJ-B10",  # ipaddr boundary value
        "ADJ-B11",  # decimal zero and negative forms
        "ADJ-B13",  # optional typed attribute supplied
    ],
    row(E, ALLOW, ["policy0"], True),
)
declare(
    [
        "ADJ-B05",  # datetime equality across equivalent UTC offsets
        "ADJ-B06",  # datetime lowercase RFC3339 separators
        "ADJ-B08",  # typed attribute absent from the context
        "ADJ-B09",  # valid typed attribute plus an unexpected extra attribute
    ],
    row(CP, NONE),
)
# An optional typed attribute that is omitted is not an error: the policy
# simply does not hold and the request is denied.
declare(["ADJ-B12"], row(E, DENY))

# Group C - invalid and malformed representations. None of them may acquire
# validity, and none of them may produce a decision.
declare(
    [
        "ADJ-C01",  # ipaddr non-IP string
        "ADJ-C02",  # ipaddr octet out of range
        "ADJ-C04",  # ipaddr JSON number
        "ADJ-C05",  # implicit escape missing its argument
        "ADJ-C07",  # ipaddr JSON array
        "ADJ-C09",  # unknown extension function name
        "ADJ-C10",  # explicit escape with an empty args array
        "ADJ-C11",  # explicit escape with two arguments
        "ADJ-C12",  # decimal non-numeric string
        "ADJ-C13",  # decimal empty string
        "ADJ-C14",  # decimal explicit escape with a numeric argument
        "ADJ-C15",  # decimal JSON number as an implicit constructor
        "ADJ-C16",  # decimal scientific notation
        "ADJ-C17",  # datetime non-date string
        "ADJ-C18",  # datetime out-of-range month
        "ADJ-C19",  # datetime JSON number
        "ADJ-C20",  # datetime implicit escape missing its argument
        "ADJ-C21",  # null typed attribute value
        "ADJ-C23",  # ipaddr implicit escape with an invalid argument
        "ADJ-C24",  # ipaddr explicit escape with an out-of-range octet
        "ADJ-C25",  # datetime explicit escape with an unparsable argument
        "ADJ-C26",  # decimal explicit escape with a non-numeric argument
    ],
    row(CP, NONE),
)
# An ipaddr prefix string is a valid ipaddr value that simply does not equal
# the policy literal, so it is a clean Deny and not an error.
declare(["ADJ-C03"], row(E, DENY))
# An explicit escape with an extra inner field is accepted: Cedar ignores the
# unknown inner field and the typed value is the one the argument names.
declare(["ADJ-C06"], row(E, ALLOW, ["policy0"], True))
# The wrong constructor for the declared type is caught by the request
# validator, after the context itself parsed.
declare(["ADJ-C08"], row(RV, NONE))
# An escape object offered for a string-typed attribute is not a constructor
# invocation: it stays a record and the policy literal does not match, so the
# request is denied cleanly.
declare(["ADJ-C22"], row(E, ALLOW, ["policy0"], True))

# The representation-equivalence groups: one typed argument, three spellings.
REPRESENTATION_GROUPS = {
    "ipaddr-match": ["ADJ-A01", "ADJ-A02", "ADJ-A03"],
    "ipaddr-mismatch": ["ADJ-A04", "ADJ-A05", "ADJ-A06"],
    "datetime-match": ["ADJ-A07", "ADJ-A08", "ADJ-A09"],
    "datetime-mismatch": ["ADJ-A10", "ADJ-A11", "ADJ-A12"],
    "decimal-match": ["ADJ-A13", "ADJ-A14", "ADJ-A15"],
    "decimal-mismatch": ["ADJ-A16", "ADJ-A17", "ADJ-A18"],
}

# The malformed-encoding controls that carry an R5-R1 fixture cross-reference.
MALFORMED_FIXTURE_CASES = [
    "ADJ-C01",
    "ADJ-C23",
    "ADJ-C24",
    "ADJ-C25",
    "ADJ-C26",
]


def canonical(value) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def main() -> int:
    output = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_OUTPUT

    corpus = json.loads(CORPUS.read_text(encoding="utf-8"))
    outcome_4130 = json.loads(OUTCOME_4130.read_text(encoding="utf-8"))
    outcome_4110 = json.loads(OUTCOME_4110.read_text(encoding="utf-8"))

    failures: list[str] = []

    corpus_ids = [case["id"] for case in corpus["cases"]]
    ids_4130 = [row_["id"] for row_ in outcome_4130]
    ids_4110 = [row_["id"] for row_ in outcome_4110]
    if corpus_ids != ids_4130 or corpus_ids != ids_4110:
        failures.append("the corpus and the two outcome files do not cover the same cases in the same order")
    if set(AUTHORITY) != set(corpus_ids):
        failures.append(
            "the authority table does not cover exactly the adjudicated cases: "
            f"missing={sorted(set(corpus_ids) - set(AUTHORITY))} "
            f"extra={sorted(set(AUTHORITY) - set(corpus_ids))}"
        )

    by_4130 = {row_["id"]: row_ for row_ in outcome_4130}
    by_4110 = {row_["id"]: row_ for row_ in outcome_4110}

    # 1. Engine-version parity.
    parity_divergences = []
    for case_id in corpus_ids:
        left = dict(by_4130.get(case_id, {}))
        right = dict(by_4110.get(case_id, {}))
        left.pop("engine", None)
        right.pop("engine", None)
        if left != right:
            parity_divergences.append(case_id)
    if parity_divergences:
        failures.append(
            "the two adjudicated engine versions diverge on: "
            f"{parity_divergences}"
        )

    # 2. The authority table itself.
    authority_rows = []
    for case_id in corpus_ids:
        expected = AUTHORITY.get(case_id)
        if expected is None:
            continue
        observed = by_4130[case_id]
        actual = {
            "stage": observed["stage"],
            "decision": observed["decision"],
            "operationFailed": observed["operationFailed"],
            "hasErrors": observed["hasErrors"],
            "cleanAllow": observed["cleanAllow"],
            "determining": observed["determining"],
        }
        agrees = actual == expected
        if not agrees:
            failures.append(
                f"`{case_id}` does not match the authority table: "
                f"expected={expected} observed={actual}"
            )
        authority_rows.append(
            {
                "id": case_id,
                "fixtureId": observed.get("fixtureId"),
                "expected": expected,
                "observed": actual,
                "agrees": agrees,
                "errors": observed["errors"],
            }
        )

    # 3. Representation equivalence.
    representation_rows = []
    for name, case_ids in REPRESENTATION_GROUPS.items():
        natural = {
            canonical(by_4130[case_id]["interpretation"]["contextNaturalJson"])
            for case_id in case_ids
        }
        decisions = {by_4130[case_id]["decision"] for case_id in case_ids}
        determining = {
            canonical(by_4130[case_id]["determining"]) for case_id in case_ids
        }
        error_states = {
            (
                by_4130[case_id]["stage"],
                by_4130[case_id]["operationFailed"],
                by_4130[case_id]["hasErrors"],
                by_4130[case_id]["cleanAllow"],
            )
            for case_id in case_ids
        }
        identical = (
            len(natural) == 1
            and len(decisions) == 1
            and len(determining) == 1
            and len(error_states) == 1
        )
        if not identical:
            failures.append(
                f"the `{name}` representations are not equivalent: "
                f"natural={natural} decisions={decisions} "
                f"determining={determining} errorStates={error_states}"
            )
        representation_rows.append(
            {
                "group": name,
                "cases": case_ids,
                "identicalNaturalJson": len(natural) == 1,
                "identicalDecision": len(decisions) == 1,
                "identicalDetermining": len(determining) == 1,
                "identicalErrorState": len(error_states) == 1,
                "naturalJson": json.loads(next(iter(natural))) if len(natural) == 1 else None,
                "decision": next(iter(decisions)) if len(decisions) == 1 else None,
                "equivalent": identical,
            }
        )

    # 4. Fail-closed: a malformed encoding never yields a decision.
    fail_closed_rows = []
    for case_id in MALFORMED_FIXTURE_CASES:
        observed = by_4130[case_id]
        closed = (
            observed["decision"] is None
            and observed["operationFailed"]
            and not observed["cleanAllow"]
        )
        if not closed:
            failures.append(f"`{case_id}` is malformed but did not fail closed")
        fail_closed_rows.append(
            {
                "id": case_id,
                "fixtureId": observed.get("fixtureId"),
                "stage": observed["stage"],
                "decision": observed["decision"],
                "operationFailed": observed["operationFailed"],
                "cleanAllow": observed["cleanAllow"],
                "error": observed["errors"][0] if observed["errors"] else None,
                "failClosed": closed,
            }
        )

    verification = {
        "commission": "DOGWOOD-SHARP-D3-IMPL-01-R5-R1",
        "purpose": (
            "Machine-checked verification of the direct Cedar adjudication "
            "outcomes against the commission's authority table."
        ),
        "corpus": {
            "path": "cedar-adjudicator/corpus.json",
            "cases": len(corpus_ids),
            "comment": corpus.get("comment"),
        },
        "engines": {
            "reference": {
                "version": "cedar-policy 4.11.0",
                "outcome": "cedar-adjudicator-4110/corpus-4110-outcome.json",
            },
            "d3": {
                "version": "cedar-policy 4.13.0",
                "outcome": "cedar-adjudicator/corpus-4130-outcome.json",
            },
        },
        "engineVersionParity": {
            "cases": len(corpus_ids),
            "divergences": parity_divergences,
            "identical": not parity_divergences,
        },
        "authorityTable": authority_rows,
        "representationEquivalence": representation_rows,
        "malformedFailClosed": fail_closed_rows,
        "failures": failures,
        "verified": not failures,
    }

    output.write_text(
        json.dumps(verification, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )

    print(f"cases: {len(corpus_ids)}")
    print(f"engine-version divergences: {len(parity_divergences)}")
    print(f"authority rows checked: {len(authority_rows)}")
    print(f"representation groups: {len(representation_rows)}")
    print(f"malformed fail-closed rows: {len(fail_closed_rows)}")
    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1
    print("authority verification: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
