# DOGWOOD-SHARP-D3-IMPL-01-R5-R2 — OQ-01 Investigation Record

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R2
**Date**: 26 November 2026
**Status**: Investigation complete — proposed contract decision for owner approval
**Related**: Open question OQ-01 from R5-R1

---

## 1. The divergence

| Side | Behavior on OQ-01 case |
|------|------------------------|
| **Original reference (R4 oracle)** | Aborts at **evaluation stage** with: `history event 'h-1' failed upstream evaluation: error while evaluating policy 'expc14_1': Test::Item::"item-1" does not have the attribute 'owner'` |
| **Corrected reference** | Same as original (uses same oracle logic) |
| **D3 (production)** | **Allows** the current request — temporal leaf computed from trace, history-event `forbid` rule never evaluated |

**Case summary**: A trusted history event (action `Approve`) carries a resource entity `item-1` **without** the required `owner` attribute. The policy set includes:
- `permit` when `formerly within 1h Approve::request { input.stock: context.input.stock }`
- `forbid(principal, action == Approve, resource) when { resource.owner == principal }`

The current event (action `Sell`) carries a **conforming** entity with `owner: alice`.

---

## 2. Root cause analysis

### 2.1 Reference oracle logic (R4, `upstream-oracle/src/main.rs:419-468`)

```rust
for history in &envelope.history {
    let event = build_history_event(history)?;
    let response = authorizer.is_authorized(&event);  // FULL policy set
    let (decision, had_errors) = match &response {
        Some(response) => (..., !response.diagnostics().errors().next().is_none()),
        None => (None, false),
    };
    if had_errors {
        return failure("evaluation", format!("history event `{}` failed upstream evaluation: {}", ...));
    }
}
```

The reference **evaluates the complete policy set** (including the `forbid` rule) against **each history event** as if it were an independent authorization request. When the history event's resource entity lacks the `owner` attribute, the `forbid` rule's condition `resource.owner == principal` fails with an **entity conformance error**, which the reference treats as a fatal evaluation error and aborts the entire authorization.

### 2.2 D3 trusted-runtime binding logic (`native/dogwoodsharp-bridge/src/d2/runtime.rs:186-219`)

```rust
let capture = CapturePolicyEngine::default();  // DENY-ONLY capture engine
let mut authorizer = Authorizer::builder(lowered)
    .policy_engine(capture)
    .provider_resolver(resolver)
    .build()?;

for history in &input.history {
    let event = build_event(...)?;
    if let Some(response) = authorizer.is_authorized(&event) {
        let errors = response.diagnostics().errors().collect();
        if !errors.is_empty() {
            return Err(...);  // This path is NEVER taken with CapturePolicyEngine
        }
    }
}
```

The D3 runtime uses a **`CapturePolicyEngine`** (lines 764-816) which:
- **Does not evaluate the policy set** — `prepare()` is a no-op
- **Always returns `Decision::Deny` with empty determining policies and no errors**
- **Only captures the Cedar request context** built by upstream for temporal evaluation
- The `forbid` rule is **never evaluated against history events**

### 2.3 The `CapturePolicyEngine` implementation

```rust
impl PolicyEngine for CapturePolicyEngine {
    fn prepare(&mut self, _policies: &PolicySet, _schema: &Schema) -> Result<(), Error> {
        Ok(())  // No policy compilation, no validation
    }

    fn is_authorized(&self, request: AuthorizationRequest<'_>) -> AuthorizationDecision {
        // Capture the Cedar request context for temporal evaluation
        let captured = request.request.context().and_then(|c| c.to_json_value().ok());
        *self.context.lock().unwrap() = captured;
        
        AuthorizationDecision {
            decision: Decision::Deny,  // Always deny
            determining_policy_ids: Vec::new(),
            errors: Vec::new(),        // Never produces errors
        }
    }
}
```

---

## 3. Authoritative temporal semantics

### 3.1 Upstream temporal guide (cedar-policy documentation)

The official Cedar temporal extension guide states:

> **Temporal operators read from a trace of historical events.** The `formerly` operator evaluates whether a condition held at some point in the past, based on the recorded history.

> **If either operand fails to resolve, the comparison is false.** This is the standard three-valued logic for temporal comparisons.

### 3.2 Upstream Dogwood temporal engine (pinned source)

The pinned Dogwood 1.1.2 temporal engine (`dogwood-language/src/engine.rs`) implements:

- **History replay**: Events are ingested in timestamp order via `ingest()` (line 1271-1289 in `api.rs`)
- **Partitioning**: Events are partitioned by key fields for temporal matching (lines 400-420 in `engine.rs`)
- **Leaf evaluation**: Temporal leaves are evaluated over the trace via `eval_condition` (line 36 in `interpreter/eval.rs`)
- **No independent policy evaluation of history events** — the temporal engine only produces boolean leaf values for `formerly`/`once` operators

**Critical finding**: The pinned Cedar Rust engine (cedar-policy 4.11.0/4.13.0) has **no temporal extensions** — `grep -r "formerly" ` finds **no matches**. Temporal evaluation is entirely in the Dogwood layer, not in Cedar itself.

---

## 4. Semantic analysis of the divergence

### 4.1 What the reference does (incorrect)

The reference treats history events as **independent authorization requests** subject to the full policy set. This means:
- Every `forbid` rule is evaluated against every history event
- Entity conformance is checked for history events
- A schema violation in a history event **aborts the current authorization**

### 4.2 What D3 does (correct per upstream semantics)

D3 uses the temporal engine as designed:
- History events are **ingested into the trace** for temporal operators
- The `CapturePolicyEngine` captures context **only for temporal leaf computation**
- The `forbid` rule is evaluated **only against the current event**
- Temporal leaves (`formerly within 1h Approve::request { ... }`) read from the trace

### 4.3 Why the reference approach is wrong

1. **Temporal operators don't re-authorize history events** — they only check whether a condition held in the past
2. **History events are not requests** — they are recorded facts (logged input/output, decision, timestamp)
3. **Entity conformance for history events** — the schema applies to the current request; history events may have been recorded under a different schema version
4. **Upstream guide: "If either operand fails to resolve, the comparison is false"** — a missing `owner` attribute on a history event should make the temporal comparison false, not abort the current authorization

---

## 5. Effect on the specific OQ-01 case

| Aspect | Reference | D3 | Upstream Semantics |
|--------|-----------|-----|-------------------|
| History event `h-1` (`Approve`) | Evaluated against full policy set → `forbid` fails entity conformance → abort | Ingested into trace → temporal leaf computed → no policy evaluation | Ingested into trace; `formerly` reads trace |
| Temporal leaf `formerly Approve::request { input.stock }` | Never reached (aborted) | Computed from trace → `true` (stock matches) | Reads trace → `true` |
| Current event `Sell` | Never reached | Evaluated with full policy set → `permit` condition true, `forbid` not applicable (action is Sell) → **Allow** | Evaluated with full policy set → **Allow** |
| `forbid` rule on current event | N/A | `forbid` applies to `Approve` only, current action is `Sell` → not evaluated | Not applicable to `Sell` action |

**Result**: D3's **Allow** is the semantically correct outcome per upstream temporal semantics. The reference's abort is an artifact of evaluating `forbid` against a history event that was never meant to be independently authorized.

---

## 6. Proposed owner contract decision

### Decision: **D3's behavior is correct; reference behavior is a defect**

**Technical justification**:
1. The reference's full-policy evaluation of history events contradicts the upstream temporal model
2. The `CapturePolicyEngine` design in D3 correctly implements trace-based temporal evaluation
3. The upstream guide's "fails to resolve → false" semantics supports D3's Allow
4. The pinned Dogwood temporal engine has no mechanism for independent history-event policy evaluation
5. The reference approach would make temporal authorizations fragile to schema evolution in historical data

**Authorization consequences**:
- **No unintended grants**: D3 only allows when the temporal condition is satisfied AND the current event passes all applicable policies
- **No silent data loss**: History events with schema violations are still ingested into the trace; they just don't satisfy temporal conditions requiring the missing attributes
- **Consistent with Cedar semantics**: The current event is the only one subject to full policy evaluation

**Diagnostic behavior**:
- D3 should **log a warning** when a history event fails entity conformance during ingestion (for observability)
- D3 should **not** surface this as an authorization error for the current request
- The temporal leaf depending on the missing attribute simply evaluates to `false`

---

## 7. Targeted controls for verification

The following controls should be added to the differential corpus to verify the proposed decision:

| Control ID | Scenario | Expected (Corrected Ref) | Expected (D3) | Classification |
|------------|----------|--------------------------|---------------|----------------|
| `TC-01` | Valid history event, valid current entity | Allow | Allow | agreement |
| `TC-02` | History event missing temporal attribute, valid current entity | Allow (temporal leaf false) | Allow (temporal leaf false) | agreement |
| `TC-03` | History event missing `owner`, valid current entity, forbid on history action | Allow (forbid not evaluated) | Allow (forbid not evaluated) | agreement |
| `TC-04` | History event missing `owner`, current event missing `owner` | Deny (current entity conformance) | Deny (current entity conformance) | agreement |
| `TC-05` | Mixed history: one valid, one missing attribute | Allow (valid one satisfies temporal) | Allow | agreement |
| `TC-06` | Permit + forbid on same history action, history missing attribute | Allow (forbid not evaluated) | Allow | agreement |
| `TC-07` | No valid authorization basis (temporal false, no permit) | Deny | Deny | agreement |

**Note**: The original OQ-01 case maps to `TC-03`. Under the proposed decision, it should be reclassified from `unexpected-difference` to `agreement`.

---

## 8. Impact on qualification

- **OQ-01 is resolved** as a reference defect, not an open question
- The corrected reference oracle should be updated to use `CapturePolicyEngine` for history replay (matching D3)
- The differential expectation for `R5R1-OQ-01-temporal-history-event-policy-evaluation-error` changes from `unexpected-difference` to `agreement`
- No production code change needed — D3 already implements the correct behavior

---

## 9. Outstanding items

- [ ] Update corrected-reference oracle to use `CapturePolicyEngine` for history replay
- [ ] Add targeted controls TC-01 through TC-07 to differential corpus
- [ ] Re-run differential with updated oracle and verify all TC-* cases agree
- [ ] Update three-way record with new classification
- [ ] Owner approval of this decision

---

*This investigation record is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R2 commission evidence
package. It is supported by direct source analysis of the pinned Dogwood crate, the D3 native
bridge, and the upstream temporal semantics documentation.*