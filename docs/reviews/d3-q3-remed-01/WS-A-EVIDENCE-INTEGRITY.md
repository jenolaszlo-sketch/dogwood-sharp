# WS-A — Evidence integrity and historical preservation (Q3-F01, Q3-F10)

**Commission**: DOGWOOD-SHARP-D3-Q3-REMED-01
**Date**: 27 November 2026
**Status**: remediation evidence complete; independent closure belongs to Q3-R1

---

## 1. Original historical identities

| Object | Identity |
|--------|----------|
| R5-R3 baseline commit | `b23f6a9b38eceb5753883073f071dc9068b9db82` |
| R5-R3 top-level manifest blob | `319fff50b4ab4d41613eeb82a872905d201d8394a266271d2bce60928784d19a` |
| R5-R3 evidence manifest blob | `30a90c1f2a749763c6fdbb0716bbfa215b6a8898d33502d8cd768cc39a367fb8` |
| Manifest entries | 47 |
| Manifest format | UTF-16LE, `<sha256>  <path>` |
| Manifest scope | R5-R3 root documents, scripts, evidence root and `evidence/differential/oq01-controls/` |

The original manifest hashes are preserved at `b23f6a9`. They are not rewritten.

## 2. Recomputed distribution at b23f6a9

| Representation | Entries |
|----------------|---------|
| Committed Git blob (LF) | 19 |
| CRLF working-tree image of the same blob | 28 |
| Unresolved | 0 |

The manifest is **mixed-convention**: 19 entries were generated from committed
blobs and 28 from CRLF working-tree bytes. It is internally consistent with a
Windows CRLF checkout of `b23f6a9`, but it has never verified on a clean LF
checkout. The self-entry `evidence/manifest.sha256` is in the blob group.

At HEAD, the same 47 entries yield 19 blob matches, 27 CRLF-only matches and
one stale entry: `evidence/fixtures.json`.

## 3. The stale entry

| Field | Value |
|-------|-------|
| Manifest path | `evidence/fixtures.json` |
| Recorded digest | `2e9e7d8bfe1669b40f1289e2262ef67eab9ed8007cc1f22e0b94d152709449ca` |
| What that digest is | The R5-R2 fixtures content at `b23f6a9` with CRLF endings |
| `b23f6a9` blob | `bfdc0884014f5a50a9bfeaa22718c29c906905502e5f7242e64ba3ea612b84a0` |
| HEAD blob | `0234a384a4c8129566b170d59fcdfcffa36e9f8184cffbeb9197efb2c0741fc0` |
| Last change | `44085d3` — remove retired ED-06 references, add FCN-02 normalization |

At `b23f6a9` the entry matched the CRLF image. It became stale when
`44085d3` changed the file after the manifest was signed.

## 4. Post-signature changes to the R5-R3 directory

`b23f6a9..HEAD` changes ten paths under
`docs/reviews/d3-impl-01-r5-r3/`:

| Status | Path | Commit |
|--------|------|--------|
| A | `evidence/differential/cedar-adjudicator/Cargo.lock` | `c35e490` |
| A | `evidence/differential/cedar-adjudicator/Cargo.toml` | `c35e490` |
| A | `evidence/differential/cedar-adjudicator/src/main.rs` | `c35e490` |
| A | `evidence/differential/corrected-oracle/Cargo.lock` | `6a80917` |
| A | `evidence/differential/corrected-oracle/Cargo.toml.template` | `6a80917` |
| A | `evidence/differential/corrected-oracle/src/main.rs` | `6a80917` |
| A | `evidence/differential/probe/Program.cs` | `6a80917`; modified `17d90b8` |
| A | `evidence/differential/probe/R5R1Probe.csproj` | `6a80917` |
| A | `evidence/differential/probe/packages.lock.json` | `6a80917` |
| M | `evidence/fixtures.json` | `44085d3` |

The nine added files are R5-R4 harness artifacts placed under the R5-R3
evidence tree. They were not part of the 47-entry R5-R3 manifest. The probe was
then modified at `17d90b8` (macOS `dladdr` resident-image resolution), so the
probe used by the later R5-R4 runs is not the probe that produced the R5-R3
Windows observations.

The commit `b23f6a9` itself is immutable. The later working tree is not the
signed R5-R3 tree.

## 5. Preservation correction

The R5-R4 `README.md` states:

> R4, R5, Q2, R5-R1, R5-R2 and R5-R3 records are unchanged.

That claim is **correct for the commits** and **incorrect for the working
directory**. This record supersedes the claim for `docs/reviews/d3-impl-01-r5-r3/`
without rewriting the R5-R4 document:

- The R5-R3 record at `b23f6a9` is preserved unchanged.
- The R5-R3 directory at later commits contains nine R5-R4 harness additions
  and one modified fixture.
- The original R5-R3 manifest remains the historical signature and is not
  retroactively valid for the later directory.

## 6. Canonical successors

New canonical manifests hash committed Git blob bytes:

| Manifest | Scope | Verification |
|----------|-------|--------------|
| `evidence/r5r3-canonical-at-b23f6a9.sha256` | the 47 original paths at `b23f6a9` | 47/47 |
| `evidence/r5r3-canonical-at-head.sha256` | all 57 tracked R5-R3 files at HEAD | 57/57 |

Per-entry reconciliation, including original, b23f6a9, CRLF and HEAD
identities, is in `evidence/r5r3-manifest-entries.json`. The post-signature
change list is `evidence/r5r3-post-signature-changes.json`.

These are **successors**, not regenerated historical signatures. The original
manifest is retained byte-for-byte.

## 7. Verification controls

- `scripts/verify-evidence-manifest.js` hashes committed blobs in git mode and
  exact bytes in files mode; it reports newline-only mismatches explicitly and
  still fails.
- `scripts/self-test-evidence-manifest.js` exercises seven controls:
  exact pass, changed content, missing artifact, stale checksum, CRLF-vs-LF
  mismatch, the 71-entry Q3 manifest, and a stale copy of it. Result: 7/7.
- `.gitattributes` now marks native/package binaries as `binary` and TRX files
  as `text eol=lf`; the repository-wide default remains `text=auto eol=lf`.
- `D3 Q3 Remediation Evidence Verification` runs the same controls on Windows
  and Ubuntu and verifies the Q3 and canonical R5-R3 manifests.

Raw results: `evidence/manifest-self-test.json`,
`evidence/r5r3-manifest-entries.json`,
`evidence/r5r3-post-signature-changes.json`.

## 8. Evidentiary limitations

The original CRLF working-tree images were not retained as separate bytes;
they are derivable from the `b23f6a9` blobs. The original manifest's
mixed-convention generation method is inferred from the digest matches, not
from a retained generator script. No missing historical evidence is
manufactured.

## 9. Producer closure recommendation

Recommend closure of Q3-F01 and Q3-F10 only after the independent reviewer
confirms:

1. the original and successor identities are correct;
2. the preservation correction is accurate;
3. the 7/7 manifest controls pass on Windows and Linux;
4. the canonical manifests verify at their stated refs;
5. no historical file was rewritten.
