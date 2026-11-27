# Owner decision — RD-01 acceptance (append-only)

**Commission**: DOGWOOD-SHARP-D3-QUAL-01-Q3-R1
**Decision date**: 27 November 2026
**Status**: **ACCEPTED AND BINDING** within the scope below
**Record type**: append-only owner decision; does not rewrite any prior record

---

## 1. Bound identities

| Object | Full identity |
|--------|---------------|
| Q3 reviewer baseline | `e980ee2e91d96b314b1564a9794725fa7214fb33` / tree `d902e50011691b5b689b86fe7c13d7dffb173573` |
| Remediation record | `d2d9ad72e129a04e9d3502a3382103240aaa1c94` / tree `1692b33d0fff5648dcabc23ddd80e173512ad76d` |
| Q3 reviewer manifest | 71 entries |
| Remediation manifest | 74 entries |
| Shipped-subject campaign | run `46039052322`, commit `2366f7ff39eaba6a03afc229939a4f67ec2be6eb` |
| Cross-platform verifier campaign | run `46033663477`, commit `0894b209477ca7fc63eb57a1366590e485acd075` |
| RD-01 proposal | `docs/reviews/d3-q3-remed-01/OQ01-SEMANTIC-ADJUDICATION.md` |
| Supplemental register | `docs/reviews/d3-q3-remed-01/evidence/oq01-supplemental-register.json` |

These are the exact reviewed identities. No other revision, dependency,
native asset or qualification subject is substituted by this decision.

---

## 2. Decision

The owner accepts the proposed **S-A** semantic adjudication and the
supplemental reference-defect record **RD-01**, strictly within the documented
OQ-01 scope.

The binding interpretation is:

1. Historical authorization events are evidence consumed by the temporal
   evaluation contract.
2. Historical events must not be independently re-authorized in a manner that
   introduces an authorization requirement not present in the approved D3
   contract.
3. A reference-oracle policy evaluation failure caused solely by such
   re-authorization must not invalidate an otherwise valid current
   authorization request.
4. Current-request validation, fail-closed behavior, historical evidence
   validation and all existing security guarantees remain binding.
5. RD-01 does not authorize a general exception for historical evaluation
   errors.

---

## 3. Scope and non-effects

- RD-01 covers exactly
  `R5R1-OQ-01-temporal-history-event-policy-evaluation-error`.
- FCN-05 remains restricted to **TC-03 through TC-06**.
- The original signed 124-case differential register is unchanged
  (`register-corrected-reference.json` SHA-256
  `5962c4dc7191444e4dedb66db87e74d816b0e5f7cb7cf60af7ae5be11da65e3d`).
- The corrected fixture is unchanged
  (`568267e0b31e233505ce322e54d4f9262d83647d7cc7a307c3c1500c598ff9f6`).
- Historical qualification records, manifests and evidence are unchanged.
- This decision does not grant D3 qualification, formal closure, release
  authorization, release tagging, NuGet publication or any later development
  stage.

---

## 4. Effective disposition of the original case

The original OQ-01 case is a **reference-defect** case, not a fail-closed
normalization and not an approved authorization difference:

- The reference oracle re-authorizes the history event and aborts on its
  policy error.
- The history event has no entities; the current request is valid.
- DogwoodSharp's Allow is contract-correct.
- The current-invalid control fails closed, so the decision does not create a
  general continuation-after-invalid-replay exception.

RD-01 is binding only for the exact case above and only as a reference-defect
record. It does not become an FCN, does not expand the signed corpus and does
not modify the FCN-05 scope.

---

## 5. Preservation statement

This record is added after the remediation record. It does not edit, replace or
regenerate the Q3 reviewer records, the remediation record, the R5-R3
manifests, the signed registers or any historical artifact. The Q3-R1 reviewer
report records the independent verification of this decision.
