# OQ-01 original case — independent semantic adjudication (Q3-F05)

**Commission**: DOGWOOD-SHARP-D3-Q3-REMED-01
**Date**: 27 November 2026
**Finding**: Q3-F05
**Proposed disposition**: **S-A — DogwoodSharp behavior is correct; the reference oracle is defective**
**Owner status**: acceptance of RD-01 remains an explicit outstanding gate

---

## 1. Exact reproducer

The original OQ-01 case is preserved verbatim in
`evidence/oq01/fixtures-original.json` and was independently re-executed with
the **shipped Windows D2 bridge** (`78f64c4b`) and the published CedarSharp
1.0.0 native (`77de6976`), against the corrected reference oracle.

Input summary:

- Schema: `Test::Item { owner: User }`, actions `Approve` and `Sell` with
  `input.stock` context.
- Policy: permit `Sell` when `formerly within 1h Approve::request { input.stock:
  context.input.stock }`; forbid `Approve` when `resource.owner == principal`.
- History: one `Approve` event `h-1`, timestamp 10, **no entities**, stock
  `AMZN`.
- Current event: `Sell`, timestamp 100, `item-1` with `owner = alice`, stock
  `AMZN`.

Observed on net8.0 and net10.0 with identical results:

| Side | Stage | Operation failed | Decision |
|------|-------|------------------|----------|
| Reference oracle | evaluation | yes | null |
| DogwoodSharp | evaluation | no | **allow** |

The reference error is:

```text
history event `h-1` failed upstream evaluation:
error while evaluating policy `expc14_1`:
`Test::Item::"item-1"` does not have the attribute `owner`
```

The reproducer was not simplified: it retains the exact history event, the
empty history entity set, the forbid rule, the current entity, and the trusted
replay.

---

## 2. Required behavior

The binding contract establishes:

1. **History is a trace, not a request sequence.** Temporal operators read the
   recorded history. The D3 trusted runtime uses the upstream temporal engine's
   `observe`/`evaluate` path; its capture policy engine deliberately does not
   re-authorize history events.
2. **The current event is the only authorization request.** The permit's
   temporal leaf reads `input.stock` from the trace; the forbid applies to
   `Approve` and is not applicable to the current `Sell` request.
3. **Entity conformance applies to entities that are present.** The history
   event has no entities, so there is no non-conforming history entity. The
   current entity conforms.
4. **Fail-closed behavior applies to invalid current requests and failures in
   the trusted path.** It does not require a history event to be re-authorized
   as if it were the current request.
5. **The reference oracle's own loop** calls `authorizer.is_authorized` on each
   history event and treats any policy error as a fatal evaluation failure.
   That is an oracle-harness decision, not the upstream temporal contract.

The authoritative Cedar layer has no temporal extension; the temporal
evaluation lives in the pinned Dogwood engine, which is total
(`eval_condition -> bool`) and has no policy-error channel for a history
event.

---

## 3. Targeted controls

| Control | History condition | Current condition | Reference | DogwoodSharp | Distinguishes |
|---------|-------------------|-------------------|-----------|--------------|---------------|
| TC-01 | valid entity | valid | Allow | Allow | legitimate authorization after valid replay |
| TC-02 | missing temporal attribute | valid | Deny | Deny | false temporal leaf |
| TC-03 | entity missing `owner` | valid | fail (evaluation) | fail (trusted-binding) | non-conforming history entity |
| TC-04 | missing `owner` | missing `owner` | fail (evaluation) | fail (trusted-binding) | history and current conformance |
| TC-05 | one valid, one missing | valid | fail (evaluation) | fail (trusted-binding) | mixed history |
| TC-06 | `owner` as plain record | valid | fail (evaluation) | fail (trusted-binding) | alternate non-conformance |
| TC-07 | false temporal leaf | valid | Deny | Deny | no authorization basis |
| Original OQ-01 | **no entities** | valid | fail (evaluation) | **Allow** | history re-authorization defect |
| New current-invalid control | no entities | entity missing `owner` | fail (evaluation) | fail (trusted-binding) | D3 does not continue after an invalid current request |

The new current-invalid control was constructed only for this adjudication and
is preserved in `current-invalid-fixture.json` /
`current-invalid-observations-net8.json`. It proves that the original Allow is
not a general "ignore validation" behavior: when the **current** request is
invalid, D3 fails closed with no decision.

The seven-control results are in `observations-7-shipped-net8.json`. The
original case results are in `observations-original-shipped-net8.json` and
`observations-original-shipped-net10.json`.

---

## 4. Disposition S-A

**DogwoodSharp is correct; the reference oracle is defective.**

The reference defect is narrow: a policy-evaluation error raised while the
oracle re-authorizes a **history event** aborts the current authorization.
The current request is valid, the temporal leaf resolves from the trace, and
the forbid does not apply to the current action. D3's Allow is the
contract-correct result.

No DogwoodSharp production change is proposed. FCN-05 remains exactly TC-03
through TC-06; this case is not a fail-closed equivalence and must not be
folded into FCN-05.

### Proposed reference-defect record RD-01

| Field | Value |
|-------|-------|
| ID | RD-01 |
| Class | reference-defect (not FCN, not an approved authorization difference) |
| Case | `R5R1-OQ-01-temporal-history-event-policy-evaluation-error` |
| Trigger | The reference oracle evaluates the full policy set against a history event and treats a policy error on that event as fatal to the current authorization. |
| Contract basis | History is replayed as a trace; the current event is the only authorization request; entity conformance applies to present entities. |
| Effect | Reference fails closed; D3 returns the current, valid decision. No error relevant to the current request is suppressed. |
| Scope | This one case only. It does not cover non-conforming history entities (FCN-05), current-event validation, provider failures, or malformed input. |
| Binding status | **Proposed; requires owner acceptance.** |

The supplemental register `evidence/oq01-supplemental-register.json` records
the case and RD-01 as proposed. The signed 124-case register is unchanged.

---

## 5. Why this is not a fail-open generalization

- The new current-invalid control fails closed on D3.
- TC-03 through TC-06 fail closed on D3 for non-conforming history entities.
- TC-02 and TC-07 deny on both sides.
- Provider failures (C-17) and malformed input (C-20/C-23) remain distinct and
  fail closed.
- The original case's history contains no entity and no error relevant to the
  current decision. The only error is the reference's decision to evaluate a
  forbid against a history record.

RD-01 is an allowed reference defect, not a permission for D3 to ignore
current-request validation or trusted-replay conformance.

---

## 6. Owner gate

Under the commission, no new reference-defect classification is binding
without owner acceptance. The remediation can close Q3-F05's **evidence and
reproducer** obligations, but not the **contract registration** obligation.

Owner decision required:

- accept RD-01 and the supplemental register, or
- direct a different disposition (for example, a bounded reference-oracle
  change and re-execution).

Until that decision, Q3-F05 remains a bounded blocker and the remediation
disposition cannot be A.
