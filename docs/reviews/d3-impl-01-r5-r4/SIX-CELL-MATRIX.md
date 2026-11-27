# DOGWOOD-SHARP-D3-IMPL-01-R5-R4 — Six-Cell Differential Matrix

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R4
**Date**: 27 November 2026
**Status**: **COMPLETE — 6/6 cells executed**
**Terminal run**: `46005380010` at `12a4555` (tree `f58951e4`)
**Machine record**: `six-cell-matrix.json`

---

## 1. Requirement

The pinned qualification plan's minimum D3 matrix requires the upstream oracle
and the D3 adapter to be built from their locked graphs for each of the six
cells, using the same fixture and expected-difference manifests on every cell.
A cell that cannot be run is pending, never passed.

| OS | RID | TFMs |
|----|-----|------|
| Windows | `win-x64` | `net8.0`, `net10.0` |
| Linux | `linux-x64` | `net8.0`, `net10.0` |
| macOS | `osx-arm64` | `net8.0`, `net10.0` |

---

## 2. Final matrix

| Cell | OS | RID | TFM | Status | Corrected reference |
|------|----|-----|-----|--------|---------------------|
| 1 | Windows | `win-x64` | `net8.0` | **EXECUTED** | 124/124 |
| 2 | Windows | `win-x64` | `net10.0` | **EXECUTED** | 124/124 |
| 3 | Linux | `linux-x64` | `net8.0` | **EXECUTED** | 124/124 |
| 4 | Linux | `linux-x64` | `net10.0` | **EXECUTED** | 124/124 |
| 5 | macOS | `osx-arm64` | `net8.0` | **EXECUTED** | 124/124 |
| 6 | macOS | `osx-arm64` | `net10.0` | **EXECUTED** | 124/124 |

Every cell reports `total 124`, `passed 124`, `failed 0`, `pending 0`, all 26
families covered. The classification totals are identical on all six cells:

| Classification | Count |
|----------------|-------|
| agreement | 84 |
| approved-difference | 22 |
| fail-closed-equivalence | 9 |
| operation-failure | 9 |

The Windows cells are the unchanged R5-R2/R5-R3 executions. The four hosted
cells were executed by workflow run `46005380010`:

| Cell | Job | Duration |
|------|-----|----------|
| Linux `net8.0` | `140527108760` | 7m57s |
| Linux `net10.0` | `140527108313` | 12m+ |
| macOS `net8.0` | `140527108625` | 13m41s |
| macOS `net10.0` | `140527108903` | 13m17s |

All four jobs ran every step to success: build oracle, build adjudicator, build
D2 bridge, build CedarSharp native, build probe, stage assets, run
differential, harness self-test, verify adjudicator, verify results, upload.

---

## 3. Native asset identities

Every hosted cell built the D2 bridge and the CedarSharp native from the locked
graphs. The asset hashes are deterministic per platform and identical across
the two TFMs:

| Platform | D2 bridge SHA-256 | CedarSharp SHA-256 |
|----------|-------------------|--------------------|
| `linux-x64` | `4b6c3409841f3c9e49f3cc2701d5db6346deea708f1d126855c2bfb4b1368dfd` | `9e8800eda6702a2d7edc121c441a9b3d190460627810bbc60cc84eb00ea80c8e` |
| `osx-arm64` | `73d29a7030e6fb7d40f6be1abd37823b24a7a616901a173736d5fc38f4bf049e` | `740e7410308d17b09694dc036d3c7fe9af5a26d766be47955cd22650a69e2d96` |

The staged CedarSharp native reported the required identity on every cell:

```json
{
  "abiVersion": 1,
  "sdkVersion": "4.13.0",
  "languageVersion": "4.5",
  "bridgeVersion": "0.1.0",
  "rustVersion": "1.94.0",
  "target": "x86_64-unknown-linux-gnu" | "aarch64-apple-darwin",
  "features": ["ipaddr", "decimal", "datetime"]
}
```

---

## 4. Defects found and closed in this commission

The first hosted attempt reached the differential but exposed three
cross-platform defects. All are fixed in the terminal run:

1. **CedarSharp identity toolchain** — the crate's `native/build.rs` stamps the
   active rustc version into the identity response. Building with the job-wide
   1.99.1 toolchain produced `rustVersion 1.99.1`, which the CedarSharp 1.0.0
   C# loader rejects while requiring 1.94.0. The asset is now built with the
   pinned 1.94.0 toolchain and fails early on any other compiler.
2. **Corrected fixture manifest** — the CI harness compared against
   `fixtures.json`, which declares `intendedStage: evaluation` for the five
   malformed typed-encoding cases. The corrected-reference run now uses
   `fixtures-corrected-reference.json`, whose `request-validation` intent
   matches the FCN-02 row.
3. **macOS resident module discovery** — `Process.Modules` does not enumerate
   the dlopen'd bridge dylib on macOS. The resident control now resolves the
   live image with `dladdr` on the exported ABI symbol; the observed module
   path matches the verified asset on both macOS cells.

The adjudicator verification also pointed at an R5-R3 corpus file that the
R5-R3 snapshot did not retain; it now uses the preserved 57-case R5-R2 corpus.

---

## 5. Evidence location

Hosted evidence is uploaded as the workflow artifacts:

- `r5r3-differential-linux-x64-net8.0`
- `r5r3-differential-linux-x64-net10.0`
- `r5r3-differential-osx-arm64-net8.0`
- `r5r3-differential-osx-arm64-net10.0`

Each contains `observations-corrected-reference.json`,
`lifecycle-corrected-reference.json`, `adjudicator-outcome.json`, and
`console.log`. Windows evidence remains under
`docs/reviews/d3-impl-01-r5-r3/evidence/`.

---

*This matrix record is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R4 commission
evidence package. It is a readiness record, not a D3 qualification
declaration.*
