# DOGWOOD-SHARP-D3-IMPL-01-R5-R4 — Final Cross-Platform Closure and Handoff

**A. EXECUTION COMPLETE — SIX-CELL MATRIX GREEN** · 27 November 2026

**Terminal execution**: commit `12a4555`, tree `f58951e4`, workflow run
`46005380010`.

This commission executed the four remaining mandatory differential cells,
resolved OQ-01 with a formal FCN-05 disposition, and closed the cross-platform
defects that blocked the earlier R5-R3 handoff.

---

## 1. Six-cell differential matrix — COMPLETE

| Cell | OS | RID | TFM | Status | Corrected reference |
|------|----|-----|-----|--------|---------------------|
| 1 | Windows | `win-x64` | `net8.0` | **EXECUTED** | 124/124 |
| 2 | Windows | `win-x64` | `net10.0` | **EXECUTED** | 124/124 |
| 3 | Linux | `linux-x64` | `net8.0` | **EXECUTED** | 124/124 |
| 4 | Linux | `linux-x64` | `net10.0` | **EXECUTED** | 124/124 |
| 5 | macOS | `osx-arm64` | `net8.0` | **EXECUTED** | 124/124 |
| 6 | macOS | `osx-arm64` | `net10.0` | **EXECUTED** | 124/124 |

All six cells report 124 cases, 124 passed, 0 failed, 0 pending, 26 families
covered, with identical classification totals (84 agreement, 22
approved-difference, 9 fail-closed-equivalence, 9 operation-failure).

Full details: `SIX-CELL-MATRIX.md` and `six-cell-matrix.json`.

The Windows cells are the unchanged R5-R2/R5-R3 executions. The four hosted
cells ran in workflow run `46005380010` and completed every step, including the
Cedar adjudicator and the differential-results verification.

---

## 2. Defects closed in this commission

The hosted matrix exposed and closed three real cross-platform defects:

1. **CedarSharp identity toolchain.** `native/build.rs` in the CedarSharp crate
   stamps the active rustc version into the identity response. The job-wide
   1.99.1 toolchain produced `rustVersion 1.99.1`, which the CedarSharp 1.0.0
   C# loader rejects (it requires 1.94.0). The CedarSharp native is now built
   with the package's pinned 1.94.0 toolchain; `build-cedarsharp.sh` refuses to
   build with any other compiler.
2. **Corrected fixture manifest.** The CI harness was using `fixtures.json`,
   whose five malformed typed-encoding cases declare
   `intendedStage: evaluation`. The corrected-reference run now uses
   `fixtures-corrected-reference.json`, matching the FCN-02 record
   (`request-validation`).
3. **macOS resident module discovery.** `Process.Modules` does not enumerate
   the dlopen'd bridge dylib on macOS, so the resident control reported a false
   negative. It now resolves the live image with `dladdr` on the exported ABI
   symbol; both macOS cells pass with the verified module path.

In addition, the adjudicator verification now uses the preserved 57-case
R5-R2 corpus, because the R5-R3 snapshot retained the adjudicator source but
not its corpus file.

---

## 3. OQ-01 / FCN-05 — RESOLVED

**Record**: `OQ01-FCN05-DISPOSITION.md` and `fcn-05.json`

- TC-01 through TC-07 show **7/7 semantic agreement**; four controls show only
  a stage difference on history-event entity conformance.
- The difference is formalized as **FCN-05 — History-event entity conformance
  failure during trusted replay**.
- FCN-05 covers exactly TC-03, TC-04, TC-05 and TC-06. It does not cover
  current-event conformance failures, provider failures, malformed encodings,
  or any Allow/Deny divergence.
- D3's behavior is accepted as correct; the reference behavior (evaluating a
  forbid rule against a recorded history event) is the defect.
- FCN-05 is recorded in R5-R4 rather than injected into the signed 124-case
  R5-R3 register: the four controls are not members of that corpus, and adding
  them would change the signed fixture identity.

The exact per-case observations are in `fcn-05.json`, derived from the
committed `6dd3053` OQ-01 evidence and the `OQ01-EXECUTION.md` record.

---

## 4. Q3 readiness assessment

**Q3 ENTRY: READY (A).**

| Entry obligation | Status |
|------------------|--------|
| Six-cell differential matrix | **COMPLETE** — 6/6 cells, 124/124 |
| OQ-01 owner contract decision | **RESOLVED** — FCN-05 formalized |
| QD3Q-05 / C-14 bounded limitation | **OWNER-ACKNOWLEDGED** (R5-R3) |
| QD3Q-07 / 08 / 11 / 21 reconciliations | **COMPLETE** (R5-R3) |
| QD3Q-12 retained gaps | **RECONCILED** — original risk ratings retained |
| Historical records preserved | **YES** — R4/R5/Q2/R5-R1/R5-R2/R5-R3 unchanged |
| Cross-platform defects | **CLOSED** — see section 2 |

This is a readiness assessment, not a qualification declaration. The commission
does **not** declare D3 qualified, accepted, released, or tagged. It states that
the mandatory entry evidence now exists and is green.

---

## 5. Historical preservation

- R4, R5, Q2, R5-R1, R5-R2 and R5-R3 records are unchanged.
- The R5-R3 signed evidence manifest is untouched; the probe change in this
  commission is under the probe source tree, which the manifest does not cover.
- ED-06 remains retired from the active contract and preserved historically.
- The OQ-01 seven-control observations remain retrievable from commit `6dd3053`;
  FCN-05 cites those committed blobs rather than rewriting the R5-R3 snapshot.
- The original-reference defect surface (113/124 on Windows) remains the
  historical R5-R3 record; the cross-platform cells execute the corrected
  reference, which is the acceptance oracle.

---

## 6. Evidence

| Artifact | Location |
|----------|----------|
| Six-cell matrix | `SIX-CELL-MATRIX.md`, `six-cell-matrix.json` |
| OQ-01 / FCN-05 | `OQ01-FCN05-DISPOSITION.md`, `fcn-05.json` |
| Hosted cell evidence | workflow artifacts `r5r3-differential-<rid>-<tfm>` |
| Windows evidence | `docs/reviews/d3-impl-01-r5-r3/evidence/` |
| Execution scripts | `scripts/` (cross-platform bash) |
| Workflow | `.github/workflows/d3-r5r3-cross-platform.yml` |

Each hosted artifact contains `observations-corrected-reference.json`,
`lifecycle-corrected-reference.json`, `adjudicator-outcome.json`, and
`console.log`.

---

## 7. Scope compliance

- ✅ Used the existing cross-platform workflow and scripts
- ✅ Owner's configured Git identity; no AI attribution or co-author trailers
- ✅ No unrelated dependency upgrades, refactoring, or cleanup
- ✅ No roadmap advancement, release tagging, or NuGet publication
- ✅ All historical records preserved
- ✅ No D3 qualification declaration
- ✅ Exact technical justifications and residual limitations recorded

---

## 8. Outstanding items

None for Q3 entry. The following remain explicitly out of scope:

- A formal D3 qualification/acceptance decision (owner action, not taken here).
- Re-running the original reference on Linux/macOS (the defect surface is
  established on Windows and the corrected reference is the acceptance oracle).
- Adding the four OQ-01 controls to the 124-case corpus (would require a new
  signed corpus identity; FCN-05 is ready for that insertion).

---

*This handoff record completes the DOGWOOD-SHARP-D3-IMPL-01-R5-R4 commission.
Terminal execution: commit `12a4555`, tree `f58951e4`, run `46005380010`.*
