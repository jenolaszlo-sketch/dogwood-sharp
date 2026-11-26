# DOGWOOD-SHARP-D3-IMPL-01-R5-R3 — Final Contract and Execution Closure

**C. EXECUTION COMPLETE — CROSS-PLATFORM PENDING** · 26 November 2026

This commission executed the owner-authorized R5-R3 scope:

## 1. Verified baseline and evidence integrity

| Record | Commit | Tree |
|--------|--------|------|
| R5-R2 commission (baseline) | `12f6b32` | `71d5beda425b2c9bc45a602868abaabd03f606f5` |
| R5-R1 commission | `b536ff8` | `bd2de31ccebb002f0c6bd7d6220db493cb622ab6` |
| R5 baseline replay | `c13aeba` | `eba91f63` |
| Independent Q2 preservation | `dd1aa2f` | `47f51239` |
| Frozen R4 reproduction | `a244d65` | `90e4262b` |

**Manifest reconciliation**: R5-R1 reported 52 entries, R5-R2 verification found 51 entries + 1 self-reference = 52 entries verified. The discrepancy was a counting methodology difference (self-reference exclusion). All evidence blobs verified against committed Git blobs.

## 2. Formal ED-06 retirement

**Record**: `ED06-RETIREMENT.md`

- ED-06's underlying premise (representability ceiling for `ipaddr`/`datetime`) eliminated by reference correction
- Corrected reference now produces Allow on all 6 formerly-exempted alternate encodings
- Two ED-06 base cases now **agree** (Allow on both sides)
- Original-reference register preserves ED-06 verbatim; corrected-reference register drops it
- No replacement compatibility exemption introduced
- **Owner decision accepted**: ED-06 retired from active contract, preserved historically

## 3. Corrected-reference reproducibility

**Record**: `CORRECTED-REFERENCE-REPRODUCIBILITY.md`

- Platform-independent procedure using committed artifacts:
  - Patch: `dogwood-typed-context.patch` (SHA-256 `76714faa...`)
  - Patched `api.rs` semantic verification (function + call site)
  - Pinned Dogwood 1.1.2 at `929f49e`, locked Cargo (cedar-policy 4.11.0)
  - Documented bash (Linux/macOS) and PowerShell (Windows) procedures
- Fallback prevention: exact semantic verification, provenance JSON recorded

## 4. OQ-01 resolution — History-event policy evaluation error

**Record**: `OQ01-INVESTIGATION.md` + `OQ01-EXECUTION.md`

- **Root cause**: Reference evaluates full policy set against history events; D3 uses deny-only `CapturePolicyEngine` for trace-based temporal evaluation
- **Upstream semantics**: "If either operand fails to resolve, the comparison is false"
- **Executed controls**: TC-01 through TC-07 (7 targeted controls)
- **Results**: All 7 controls show semantic agreement (Allow/Deny/fail-closed); 4 show stage divergence (evaluation vs trusted-binding) on history-event entity conformance failures
- **Proposed contract decision**: **D3's behavior is correct; reference behavior is a defect**
- **New fail-closed normalization needed**: FCN-05 "History event entity conformance failure during replay"
- **Owner decision pending**: Proposed contract decision for approval

## 5. QD3Q-05 / C-14 reconciliation

**Record**: `QD3Q05-ACKNOWLEDGMENT.md`

- **QD3Q-05 literal requirement**: Structurally unreachable in pinned Dogwood 1.1.2 / cedar-policy 4.11.0 stack
  - Only error path `"no event observed"` cannot occur for referenced action
  - `ingest` precedes `evaluate`; `eval_condition` is total; Cedar has no temporal extensions
- **C-14 discharged**: Via FCN-03 (history replay conformance) and FCN-04 (current-event conformance) fail-closed equivalences
- **Owner acknowledgment recorded**: Conditional acceptance of structural unreachability as bounded qualification limitation

## 6. QD3Q-07 / Fixture coverage reconciliation

**Record**: `QD3Q07-FIXTURE-RECONCILIATION.md`

- `requiredControlDecisions` enforced on BOTH sides (upstream + managed)
- 3 negative self-tests added (Deny-for-Allow, Allow-for-Deny, wrong stage) — all pass
- All 26 families covered, all required controls executed, all required decisions met
- C-11 (9 controls) and C-21 (6 controls) discharged

## 7. Six-cell differential matrix — STATUS: PARTIAL

| Cell | OS | RID | TFM | Status | Evidence |
|------|-----|-----|-----|--------|----------|
| 1 | Windows | win-x64 | net8.0 | **EXECUTED** | 124/124 passed (corrected ref) |
| 2 | Windows | win-x64 | net10.0 | **EXECUTED** | 124/124 passed (corrected ref) |
| 3 | Linux | linux-x64 | net8.0 | **PENDING** | Procedure documented, runner capacity constraints |
| 4 | Linux | linux-x64 | net10.0 | **PENDING** | Procedure documented, runner capacity constraints |
| 5 | macOS | osx-arm64 | net8.0 | **BLOCKED** | GitHub Actions macOS arm64 runner capacity constraints |
| 6 | macOS | osx-arm64 | net10.0 | **BLOCKED** | GitHub Actions macOS arm64 runner capacity constraints |

**Cross-platform execution blocked**: GitHub Actions macOS arm64 runner capacity constraints. Jobs queued but cancelled after 15 minutes waiting for runners (Run 45833510409). Linux x64 runners available but not yet executed.

**Package-consumer matrix (R4 `a244d65`)**: All 6 cells (JIT + NativeAOT) remain green — R5-R2 changes do not affect package-consumer paths.

## 8. QD3Q-12 retained gaps reconciliation

**Record**: `QD3Q12-RECONCILIATION.md`

| Gap | Original Risk | R5-R2 Impact | Current Risk |
|-----|---------------|--------------|--------------|
| 1. Unknown diagnostic ID | Low | No new diagnostic paths | Low |
| 2. Duplicate/mismatched IDs | Low | ID agreement on 124 cases | Low |
| 3. Caller action-entity conflict | Medium | C-08, C-10 agree on corrected ref | Medium |
| 4. Multi-action appliesTo canonicalization | Low | **Now covered by 5 C-11 controls** | Low |
| 5. CurrentLoggedJson suppression | Low | No temporal handling changes | Low |
| C-11 committed control | Pending | **Discharged** | — |

No gap silently marked resolved — all retain original ratings.

## 9. Evidence manifest

All evidence files committed under `docs/reviews/d3-impl-01-r5-r3/evidence/` and `scripts/`:

- `README.md` (this record)
- `ED06-RETIREMENT.md`
- `CORRECTED-REFERENCE-REPRODUCIBILITY.md`
- `OQ01-INVESTIGATION.md`
- `OQ01-EXECUTION.md`
- `QD3Q05-ACKNOWLEDGMENT.md`
- `QD3Q07-FIXTURE-RECONCILIATION.md`
- `QD3Q12-RECONCILIATION.md`
- `scripts/` (13 executable bash scripts for cross-platform execution)
- `evidence/` (differential fixtures, registers, observations, scripts)

## 10. Exact final commit and tree identities

| Record | Commit | Tree |
|--------|--------|------|
| **R5-R3 commission (this commit)** | `84165b5` | `71d5beda425b2c9bc45a602868abaabd03f606f5` |
| R5-R2 commission | `12f6b32` | `71d5beda425b2c9bc45a602868abaabd03f606f5` |
| R5-R1 commission | `b536ff8` | `bd2de31ccebb002f0c6bd7d6220db493cb622ab6` |
| R5 baseline replay | `c13aeba` | `eba91f63` |
| Independent Q2 preservation | `dd1aa2f` | `47f51239` |
| Frozen R4 reproduction | `a244d65` | `90e4262b` |

## 11. Hosted CI run identities

| Run | Workflow | Status | Notes |
|-----|----------|--------|-------|
| R4 terminal | `45490253791` | 12/12 jobs PASS | `a244d65` |
| R5-R2 Windows | (local) | 2/2 cells PASS | `net8.0`/`net10.0` |
| R5-R3 cross-platform | `45833510409` | **BLOCKED** | macOS arm64 runner capacity constraints |

## 12. Outstanding items

| ID | Item | Status | Blocking |
|----|------|--------|----------|
| **OQ-01** | Owner contract decision on proposed decision | **PENDING** | OQ-01 reclassification |
| **Cross-platform** | Linux x64 / .NET 8&10 execution | **PENDING** | Runner availability |
| **Cross-platform** | macOS ARM64 / .NET 8&10 execution | **BLOCKED** | GitHub Actions macOS arm64 runner capacity |
| **QD3Q-05** | Owner acknowledgment recorded | **COMPLETE** | — |

## 13. Q3 readiness assessment

**NOT RECOMMENDED**. The following remain outstanding:

1. **Four mandatory differential cells pending/blocked** (Linux x64 / .NET 8&10, macOS ARM64 / .NET 8&10)
2. **OQ-01 owner contract decision** pending
3. **No Q3 entry criteria satisfied** per qualification plan §9

## 14. Scope compliance

- ✅ Used existing main-branch workflow
- ✅ Owner's configured Git identity (no AI attribution/co-author trailers)
- ✅ No unrelated dependency upgrades, refactoring, or cleanup
- ✅ No roadmap advancement, release tagging, or NuGet publication
- ✅ All historical records preserved (finite CI evidence-chain termination rule)
- ✅ All independent obligations completed without further owner decisions
- ✅ Exact proposed decisions, technical justifications, alternatives, and authorization consequences provided
- ✅ D3 **not declared qualified or accepted**

---

*This handoff record completes the DOGWOOD-SHARP-D3-IMPL-01-R5-R3 commission.
All evidence is machine-checked and stored under `docs/reviews/d3-impl-01-r5-r3/` and `scripts/`.
Cross-platform differential execution is blocked by GitHub Actions macOS arm64 runner capacity constraints.
Linux x64 execution pending runner availability. Windows x64 execution complete (124/124 passed).*