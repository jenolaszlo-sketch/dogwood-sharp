# DOGWOOD-SHARP-D3-IMPL-01-R5-R2 — Contract Reconciliation and Cross-Platform Qualification

**C. REMEDIATION COMPLETE — OWNER DECISIONS REQUIRED** · 26 November 2026

This commission executes the owner-authorized R5-R2 scope:
- Verify and preserve R5-R1 evidence
- Formalize ED-06 retirement
- Establish corrected-reference reproducibility
- Resolve OQ-01 (history-event policy evaluation error)
- Reconcile QD3Q-05 / C-14
- Reconcile QD3Q-07 / fixture coverage
- Document six-cell differential matrix (Windows executed, cross-platform procedure)
- Reconcile QD3Q-12 retained gaps
- Produce R5-R2 successor record

**D3 qualification remains open. Q3 entry is not authorized.**

---

## 1. Verified baseline identities

| Record | Commit | Tree |
|--------|--------|------|
| R5-R1 commission (this baseline) | `b536ff8` | `bd2de31ccebb002f0c6bd7d6220db493cb622ab6` |
| R5 baseline replay | `c13aeba` | `eba91f63` |
| Independent Q2 preservation | `dd1aa2f` | `47f51239` |
| Frozen R4 reproduction | `a244d65` | `90e4262b` |
| Parent of R4 | `554edc1` | `1aa73fd2` |
| Frozen Q1 subject | `ee870e9` | `6a5a0d39` |
| Pinned upstream Dogwood | (upstream) `929f49e` | (upstream) `929f49e` |

All R5-R1 manifest entries (51 verified + 1 self-reference) confirmed against working tree.

---

## 2. R5-R1 evidence verification summary

| Claim | Verification |
|-------|--------------|
| 57-case Cedar adjudication (4.13.0 = 4.11.0) | ✓ 0 engine-version divergences; `authority-verification.json` machine-checked |
| 124-case differential classification | ✓ 76 agreement, 37 intentional deviation, 11 reference defect, 0 D3 defect |
| 40 representation-invariance regression tests | ✓ 16 xUnit test cases (5 theories × 3 types + 1 fact), each verifying 3 representations internally; all pass on `net8.0` and `net10.0` |

---

## 3. Formal ED-06 retirement

**Record**: `ED06-RETIREMENT.md`

**Owner decision**: Retire ED-06 from active qualification contract; preserve as historical decision.

**Technical justification**:
- ED-06 authorized Deny (reference) vs Allow (D3) on explicit `__extn` escape for `ipaddr`/`datetime`
- R5-R1 correction fixes the reference defect: `reserved_extn_expr` in `dogwood-language/src/api.rs`
- Direct Cedar adjudication proves all three JSON spellings are semantically equivalent
- Corrected reference now produces Allow on all six formerly-exempted alternate encodings
- The two ED-06 base cases now **agree** (Allow on both sides)

**Impact**:
- Original-reference register: ED-06 carried verbatim (historical preservation)
- Corrected-reference register: ED-06 removed; formerly-exempted cases now ordinary `agreement`
- No replacement compatibility exemption introduced

---

## 4. Corrected-reference reproducibility

**Record**: `CORRECTED-REFERENCE-REPRODUCIBILITY.md`

**Procedure**: Platform-independent, using only committed artifacts:
- Patch: `dogwood-typed-context.patch` (SHA-256 `76714faa4cbbc12866565736f1a6d7b8d4ef02ca53bd90c111eb035a3ee46b32`)
- Patched `api.rs` SHA-256: `4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3`
- Pinned revision: `929f49e7305534ac61ef06cf8e47269c596dc851` (Dogwood 1.1.2)
- Lock file: `Cargo.lock` (cedar-policy 4.11.0)
- Preparation: `prepare-dogwood.ps1` (Windows) or documented bash equivalent (Linux/macOS)
- Build: `cargo build --locked --release --offline`
- Verification: Exact `api.rs` hash, exactly one file changed, provenance JSON recorded

**No fallback to uncorrected reference possible** — procedure rejects unexpected versions.

---

## 5. OQ-01 resolution — History-event policy evaluation error

**Record**: `OQ01-INVESTIGATION.md`

**Divergence**: Reference aborts on history-event `forbid` entity conformance failure; D3 allows.

**Root cause**: Reference evaluates **full policy set** against history events; D3 uses **`CapturePolicyEngine`** (deny-only) that only captures context for temporal leaves.

**Upstream semantics**: "If either operand fails to resolve, the comparison is false." Temporal operators read from trace, not re-authorize history events.

**Proposed contract decision**: **D3's behavior is correct; reference behavior is a defect.**

**Impact**: OQ-01 reclassified from `unexpected-difference` to `agreement`. Targeted controls TC-01..TC-07 specified for verification.

**No production change needed** — D3 already implements correct behavior.

---

## 6. QD3Q-05 / C-14 reconciliation

**Record**: `QD3Q05-C14-RECONCILIATION.md`

| Obligation | Status | Rationale |
|------------|--------|-----------|
| QD3Q-05 literal (genuine temporal-engine evaluation failure) | **Structurally unreachable** | Only error path `"no event observed"` cannot occur for referenced action; `ingest` precedes `evaluate`; `eval_condition` is total; Cedar has no temporal extensions |
| C-14 broader (fail-closed temporal failures) | **Discharged via FCN-03, FCN-04** | Two reachable evaluation-stage fail-closed cases: history replay conformance (FCN-03) and current-event conformance (FCN-04) |

**Residual limitation**: Explicitly documented for owner acknowledgment. Alternative acceptance criterion proposed.

---

## 7. QD3Q-07 / Fixture coverage reconciliation

**Record**: `QD3Q07-FIXTURE-RECONCILIATION.md`

- **`requiredControlDecisions` enforced on BOTH sides** (upstream + managed)
- **3 negative self-tests added**: Deny-for-Allow, Allow-for-Deny, wrong stage — all pass
- **All 26 families covered**, all required controls executed, all required decisions met
- **C-11**: 9 controls executed (5 multi-action, 4 existing) — all agree
- **C-21**: 6 controls executed (5 existing + 1 new reorder) — all agree
- **19 self-tests total** pass on both `net8.0` and `net10.0`

---

## 8. Six-cell differential matrix

**Record**: `SIX-CELL-MATRIX.md`

| Cell | OS | RID | TFM | Status | Evidence |
|------|-----|-----|-----|--------|----------|
| 1 | Windows | win-x64 | net8.0 | **EXECUTED** | 124/124 passed (corrected ref) |
| 2 | Windows | win-x64 | net10.0 | **EXECUTED** | 124/124 passed (corrected ref) |
| 3 | Linux | linux-x64 | net8.0 | **PENDING** | Procedure documented |
| 4 | Linux | linux-x64 | net10.0 | **PENDING** | Procedure documented |
| 5 | macOS | osx-arm64 | net8.0 | **PENDING** | Procedure documented |
| 6 | macOS | osx-arm64 | net10.0 | **PENDING** | Procedure documented |

**Package-consumer matrix (R4 `a244d65`)**: All 6 cells (JIT + NativeAOT) remain green — R5-R2 changes do not affect package-consumer paths.

---

## 9. QD3Q-12 retained gaps reconciliation

**Record**: `QD3Q12-RECONCILIATION.md`

| Gap | Original Risk | R5-R2 Impact | Current Risk |
|-----|---------------|--------------|--------------|
| 1. Unknown diagnostic ID | Low | No new diagnostic paths | Low |
| 2. Duplicate/mismatched IDs | Low | ID agreement on 124 cases | Low |
| 3. Caller action-entity conflict | Medium | C-08, C-10 agree on corrected ref | Medium |
| 4. Multi-action appliesTo canonicalization | Low | **Now covered by 5 C-11 controls** | Low |
| 5. CurrentLoggedJson suppression | Low | No temporal handling changes | Low |
| C-11 committed control | Pending | **Discharged** | — |

**No gap silently marked resolved** — all retain original ratings. Gap 4 now has executed evidence but retains conservative Low rating.

---

## 10. Evidence manifest (R5-R2)

All evidence files committed under `docs/reviews/d3-impl-01-r5-r2/`:

```
README.md                              (this record)
ED06-RETIREMENT.md
CORRECTED-REFERENCE-REPRODUCIBILITY.md
OQ01-INVESTIGATION.md
QD3Q05-C14-RECONCILIATION.md
QD3Q07-FIXTURE-RECONCILIATION.md
SIX-CELL-MATRIX.md
QD3Q12-RECONCILIATION.md
evidence/
  fixtures.json                        (124 cases, corrected-reference expectations)
  fixtures-corrected-reference.json    (same, ED-06 reclassified to agreement)
  register.json                        (original reference: ED-01..ED-06, FCN-01..FCN-04)
  register-corrected-reference.json    (corrected reference: ED-01..ED-05, FCN-01..FCN-04)
  manifest.sha256                      (52 entries, LF endings)
  authority-verification.json          (machine-checked adjudication authority)
  three-way-record.json                (124 cases × 4 sides)
  three-way-record.md                  (human-readable three-way record)
  six-cell-matrix.json                 (matrix with per-cell status)
  observations-corrected-reference-net8.0.json
  observations-corrected-reference-net10.0.json
  lifecycle-corrected-reference-net8.0.json
  lifecycle-corrected-reference-net10.0.json
  self-test-net8.0.json
  self-test-net10.0.json
  differential/
    build-r5r1-corpus.py
    build-r5r1-registers.py
    build-three-way-record.py
    cedar-adjudicator/ (corpus, outcomes, authority verification)
    cedar-adjudicator-4110/ (4.11.0 twin)
    corrected-oracle/ (patch, prep script, Cargo.toml.template, Cargo.lock, source)
    open-question/ (OQ-01 dedicated run)
```

---

## 11. Exact final commit and tree identities

| Record | Commit | Tree |
|--------|--------|------|
| **R5-R2 commission (this commit)** | `(this commit)` | `(this tree)` |
| R5-R1 commission | `b536ff8` | `bd2de31ccebb002f0c6bd7d6220db493cb622ab6` |
| R5 baseline replay | `c13aeba` | `eba91f63` |
| Independent Q2 preservation | `dd1aa2f` | `47f51239` |
| Frozen R4 reproduction | `a244d65` | `90e4262b` |

---

## 12. Hosted CI run identities

| Run | Workflow | Status | Notes |
|-----|----------|--------|-------|
| R4 terminal | `45490253791` | 12/12 jobs PASS | `a244d65` |
| Q2 independent | (separate) | PASS | `dd1aa2f` |
| R5-R2 cross-platform | **NOT YET RUN** | Pending | Requires Linux/macOS runners |

**No R5-R2 hosted CI runs executed** — cross-platform differential cells remain pending.

---

## 13. Outstanding owner decisions and blockers

| ID | Decision Required | Blocking |
|----|-------------------|----------|
| **ED-06** | Confirm retirement from active contract (already accepted in R5-R2 scope) | — |
| **OQ-01** | Approve proposed contract decision: D3 behavior correct, reference defect | OQ-01 reclassification |
| **QD3Q-05** | Acknowledge structural unreachability; accept alternative C-14 criterion | QD3Q-05 literal closure |
| **Cross-platform** | Authorize Linux/macOS differential execution (CI capacity) | 4 pending matrix cells |
| **Q3 readiness** | **NOT RECOMMENDED** — mandatory obligations and owner decisions remain | All above |

---

## 14. Q3 readiness assessment

**NOT RECOMMENDED**. The following remain outstanding:

1. **Four mandatory differential cells pending** (Linux x64 / .NET 8&10, macOS ARM64 / .NET 8&10)
2. **OQ-01 owner contract decision** pending
3. **QD3Q-05 owner acknowledgment** of structural unreachability pending
4. **No Q3 entry criteria satisfied** per qualification plan §9

---

## 15. Scope compliance

- ✅ Used existing main-branch workflow
- ✅ Owner's configured Git identity (no AI attribution/co-author trailers)
- ✅ No unrelated dependency upgrades, refactoring, or cleanup
- ✅ No roadmap advancement, release tagging, or NuGet publication
- ✅ All historical records preserved (finite CI evidence-chain termination rule)
- ✅ All independent obligations completed without further owner decisions
- ✅ Exact proposed decisions, technical justifications, alternatives, and authorization consequences provided
- ✅ D3 **not declared qualified or accepted**

---

*This handoff record completes the DOGWOOD-SHARP-D3-IMPL-01-R5-R2 commission.
All evidence is machine-checked and stored under `docs/reviews/d3-impl-01-r5-r2/evidence/`.*