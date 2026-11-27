# OQ-01 / FCN-05 — Formal Fail-Closed Normalization Disposition

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R4
**Date**: 27 November 2026
**Status**: **FORMALIZED** — owner-authorized after TC-01 through TC-07 verification
**Machine record**: `fcn-05.json` (this directory)

---

## 1. Decision

FCN-05 is formalized as a **fail-closed normalization**, not as an approved
authorization difference. It covers exactly one residual difference: a trusted
history event whose entity set fails schema conformance is refused at the
trusted-binding stage by D3, while the corrected reference refuses the same
request at the evaluation stage.

Both sides produce **no decision** and **no Allow**. The difference is the
stage and diagnostic shape of the refusal, not the authorization outcome.

This disposition accepts the OQ-01 contract decision recorded in
`docs/reviews/d3-impl-01-r5-r3/OQ01-EXECUTION.md`:

> D3's behavior is correct; the reference behavior is a defect.

---

## 2. Exact triggering condition

FCN-05 applies only when **all** of the following hold:

1. The case is a trusted-runtime temporal case (`trustedRuntime: true`) whose
   policy uses `formerly` / `within` over a recorded history.
2. At least one **history** event carries a caller-supplied entity that does
   not conform to the schema — specifically, an entity-typed attribute is
   absent or is encoded as a plain record instead of a Cedar entity reference.
3. The reference evaluates the full policy set against that history event and
   fails at the **evaluation** stage with
   `history event \`<id>\` failed upstream evaluation: entity conformance:
   entity does not conform to the schema` and no decision.
4. D3 validates the same history entity during trusted-runtime construction and
   fails at the **trusted-binding** stage with no decision and the
   `trusted-binding-unavailable` category.
5. No Allow is produced on either side and no trusted temporal binding is
   produced on either side.

If any condition fails, the case is not covered by FCN-05.

---

## 3. Scope

**In scope** (the four verified controls):

| Case | Triggering history event |
|------|--------------------------|
| `R5R3-TC-03-history-missing-owner-forbid-on-history` | `h-1`, entity missing `owner` |
| `R5R3-TC-04-both-missing-owner` | `h-1`, entity missing `owner` (current event is also non-conforming) |
| `R5R3-TC-05-mixed-history-one-valid` | `h-2`, entity missing `owner` (one valid history event present) |
| `R5R3-TC-06-permit-forbid-same-action-history-missing-owner` | `h-1`, `owner` encoded as a plain record |

**Out of scope**:

- Current-event conformance failures (covered by FCN-04, and by the ordinary
  `entity-conformance` expectation outside the temporal path).
- History events that simply fail the temporal leaf or omit the temporal
  attribute (TC-02 and TC-07; both sides agree on Deny).
- Provider resolver omission or failure (FCN-01).
- Malformed typed-context encodings (FCN-02).
- Any case where the two sides differ on the decision itself. FCN-05 never
  converts an Allow/Deny divergence into an agreement.

---

## 4. Machine-verified observations

The four normalized cases were executed on Windows x64 against the original
reference, the corrected reference, and D3 on **.NET 8.0 and .NET 10.0**. The
two frameworks produced identical observations for these fields.

| Case | Reference stage | Reference decision | D3 stage | D3 decision | Clean Allow |
|------|-----------------|--------------------|----------|-------------|-------------|
| TC-03 | `evaluation` | `null` | `trusted-binding` | `null` | no / no |
| TC-04 | `evaluation` | `null` | `trusted-binding` | `null` | no / no |
| TC-05 | `evaluation` | `null` | `trusted-binding` | `null` | no / no |
| TC-06 | `evaluation` | `null` | `trusted-binding` | `null` | no / no |

Both sides report `operationFailed: true` and `hasErrors: true`. The reference
carries the conformance diagnostic in `error`; D3 carries the same underlying
diagnostic inside a `trusted-binding-unavailable` finding. The full
per-observation records are in `fcn-05.json`.

Control summary (TC-01 through TC-07):

- **7/7 semantic agreement** on authorization outcome.
- **3 agreements** (TC-01, TC-02, TC-07): Allow, Deny, Deny.
- **4 stage-only divergences** (TC-03 through TC-06), normalized here.
- No control produced a divergent Allow/Deny decision.

---

## 5. Basis

A recorded history event is a fact, not a new authorization request. Re-running
the full policy set — including a `forbid` rule — against a history event is
the reference defect: it applies an authorization decision to data that is
supposed to be replayed as a trace. D3 validates history entities while it
constructs the trusted runtime, which is earlier and stricter, and then refuses
to bind.

Because both sides refuse the request, the difference is a fail-closed
equivalence in the same sense as FCN-01, FCN-03 and FCN-04. FCN-05 therefore
records the exact stage and diagnostic difference rather than silently
classifying the four cases as unexpected differences.

FCN-05 does **not** overlap FCN-03/FCN-04:

- FCN-03 covers a history event evaluated *inside the temporal pipeline* with a
  non-conforming entity under the 124-case corpus.
- FCN-04 covers a non-conforming **current** event after successful bindings.
- FCN-05 covers the four OQ-01 controls whose history entities are
  non-conforming and whose differing stage is the specific OQ-01 defect.

---

## 6. Why this is not injected into the R5-R3 corpus register

The 124-case differential corpus does not contain TC-03 through TC-06. Adding
them, or adding a register row that names cases outside the corpus, would
change the signed R5-R3 fixture/corpus identity and the manifest hash. The
R5-R3 evidence is historical and is preserved unchanged.

FCN-05 is therefore recorded here, in the R5-R4 commission record, with:

- machine-readable case observations (`fcn-05.json`),
- the committed 6dd3053 observations it derives from, and
- the OQ01-EXECUTION record.

If a future commission adds these four controls to a differential corpus, the
`failClosedNormalization` object in `fcn-05.json` can be inserted verbatim as a
`failClosedNormalizations` entry, with `failClosedNormalizationId: "FCN-05"` on
each fixture case.

---

## 7. Provenance

The seven controls were executed and committed at `6dd3053`
(`docs/reviews/d3-impl-01-r5-r3/evidence/differential/oq01-controls/`). The
R5-R3 closure snapshot later stored a single-case open-question fixture in the
same directory; the seven-control observations remain retrievable from
`6dd3053` and are cited by `fcn-05.json`. No historical artifact is rewritten.

---

## 8. Effect on OQ-01

- OQ-01 is **RESOLVED** for the active contract: D3 behavior is correct; the
  reference behavior is a defect; the residual difference is normalized as
  FCN-05.
- The four formerly unclassified controls become
  **fail-closed-equivalence**, not unexpected differences.
- No new compatibility exemption is introduced and no authorization difference
  is approved.

---

## 9. Qualification status

This disposition is a contract decision. It is **not** a D3 qualification
declaration. Q3 entry remains governed by the full six-cell matrix and the
remaining open items in the R5-R4 handoff.
