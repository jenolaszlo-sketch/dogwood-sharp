# Owner acceptance and D3 closure (append-only)

**Commission**: `DOGWOOD-SHARP-D3-CLOSE-RELEASE-PREP-01`
**Decision date**: 27 November 2026
**Status**: **D3 CLOSED WITH QUALIFICATIONS; PREVIEW RELEASE CANDIDATE PREPARED**
**Record type**: append-only owner decision; does not rewrite any prior record

---

## 1. Decision

The owner accepts the independent Q3-R1 disposition:

> **B — SUPPORTED WITH QUALIFICATIONS.**

All six Q3 findings are accepted as independently closed within the qualified
subject boundary. The owner accepts the three documented residual
qualifications and authorizes formal closure of:

- `DOGWOOD-SHARP-D3-Q3-REMED-01`;
- `DOGWOOD-SHARP-D3-QUAL-01-Q3-R1`; and
- D3 implementation and qualification,

subject to reconciliation of all existing formal stage-closure requirements.
That reconciliation is recorded in `README.md` and `VERIFICATION.md`; the
requirements are satisfied for the bound subject.

The B disposition and its qualifications are preserved. They are **not**
retrospectively converted into an unqualified A disposition. This decision does
not grant release tagging, GitHub release publication, NuGet publication or any
new development stage.

---

## 2. Bound reviewed identities

| Object | Full identity |
| --- | --- |
| Q3 reviewer baseline | `e980ee2e91d96b314b1564a9794725fa7214fb33` / tree `d902e50011691b5b689b86fe7c13d7dffb173573` |
| Remediation record | `d2d9ad72e129a04e9d3502a3382103240aaa1c94` / tree `1692b33d0fff5648dcabc23ddd80e173512ad76d` |
| Q3-R1 record | `d8ea9782fca5cd16d0666d691eaf53ce1829f6bd` / tree `f0d3f9507462cb8f28b41fe46bc98d306910b658` |
| Q3 reviewer manifest | 71/71 entries |
| Remediation manifest | 74/74 entries |
| Q3-R1 manifest | 64/64 entries |
| Independent evidence run | `46044342439` at `d2d9ad7` |
| Independent shipped-subject run | `46044345656` at `d2d9ad7` |
| Producer shipped-subject run | `46039052322` at `2366f7ff39eaba6a03afc229939a4f67ec2be6eb` |
| Production implementation identity | `7a22871ae89205a51e382c3cdc238190d927a471`; `src=50abeca4`, `native=100ffb3d`, `tests=93cfa537`, `eng=7d5d646d` |
| Preview release-candidate commit | `58b0a3332391d85cdf19fd7b794d79516d2ecdfe` / tree `4c535ac8640ec5a1086d0956017e80b63850ef98` |
| Stable package | `DogwoodSharp` `0.1.0-preview.1` |
| Experimental adapter | `DogwoodSharp.CedarSharp` `0.1.0-experimental.1` |

The Q3-R1 record is a documentation/evidence successor. It is **not** treated
as the production implementation identity. The production source trees
(`src` C# code and `native` Rust code), the test tree, `eng/Build-Native.ps1`,
`eng/Verify.ps1`, `eng/Test-NativePackageAssets.ps1` and the committed
`Cargo.lock` (`ec901557...`) are unchanged from the qualified implementation
identity. The release-candidate commit changes only release metadata, release
tooling, verification tooling and documentation.

---

## 3. Finding dispositions accepted

| Finding | Disposition | Basis |
| --- | --- | --- |
| Q3-F01 | Closed | Independent verifier 71/71, 47/47, 57/57 and self-test 7/7 on Windows and Linux; remediation manifest 74/74; Q3-R1 manifest 64/64. |
| Q3-F02 | Closed | Six-cell shipped-subject campaign, 124/124 cases, 26/26 families; resident image equals the verified product bridge; qualification-only bridge rejected in every cell. |
| Q3-F03 | Closed | Committed lock `ec901557...` in every shipped cell; the retired path's 36-package divergence is reproduced; Windows images differ only in 24 embedded metadata bytes and normalize to `28add84f...`. |
| Q3-F04 | Closed as a bounded external qualification | Package `99cc3659...`, nuspec commit `68ffdafa...`, published natives `77de6976...`/`85433a35...`/`290fcce9...`; the native `sourceCommit 324d3c0` is not recoverable. |
| Q3-F05 | Closed | Owner RD-01 decision; original OQ-01 plus the seven controls and the current-invalid control re-executed; FCN-05 remains exactly TC-03 through TC-06. |
| Q3-F10 | Closed | Zero file changes under `docs/reviews/d3-impl-01-r5-r3` from `e980ee2` to `d2d9ad7`; original manifests and fixtures are preserved; successor manifests verify 47/47 and 57/57. |

---

## 4. Accepted residual qualifications

1. **External CedarSharp native source metadata.** The published
   `CedarSharp 1.0.0` package and all three native images are hash-verified and
   the package nuspec commit is recoverable. The native manifests declare a
   `sourceCommit` that is not recoverable. The published package and native
   hashes are the binding consumer identities; source reproducibility of the
   external native is not claimed.
2. **Windows binary metadata nondeterminism.** Same-source/same-lock Windows
   builds can differ in embedded PE/debug timestamps and the PDB GUID. The
   exact release-candidate SHA-256 binds the publication artifact; normalized
   equality is not a substitute for exact artifact identity.
3. **F04 evidence-path documentation defect.** The remediation
   `findings-closure.json` F04 supporting-evidence path resolves under the Q3
   reviewer tree, not the remediation tree. The content is present, hash
   verified and covered by the Q3 manifest. This is a documentation-reference
   defect, not an evidence-integrity failure. See
   `F04-EVIDENCE-PATH-CLARIFICATION.md`.

None of these qualifications waives a mandatory execution, provenance or
package-verification requirement.

---

## 5. RD-01 and FCN-05 preservation

- **RD-01** covers exactly
  `R5R1-OQ-01-temporal-history-event-policy-evaluation-error`.
- **FCN-05** covers exactly `TC-03`, `TC-04`, `TC-05` and `TC-06`.
- The signed 124-case register is unchanged (canonical SHA-256
  `5962c4dc7191444e4dedb66db87e74d816b0e5f7cb7cf60af7ae5be11da65e3d`).
- The corrected fixture is unchanged (canonical SHA-256
  `568267e0b31e233505ce322e54d4f9262d83647d7cc7a307c3c1500c598ff9f6`).
- RD-01 is a reference-defect record, not an FCN and not a general
  historical-evaluation exception. FCN-05 has not been expanded.

---

## 6. Release eligibility

The owner accepts the preview release candidate for a separate publication
authorization decision. This acceptance does **not** authorize:

- release tagging;
- GitHub release publication;
- NuGet publication;
- new development stages;
- dependency modernization;
- deterministic native build remediation; or
- destructive repository or worktree cleanup.

Publication remains subject to separate explicit owner authorization and the
separately qualified exact-artifact publication pipeline. `publish.yml` remains
a refusal gate until that pipeline is implemented and accepted.

---

## 7. Preservation statement

This record is added after the Q3-R1 record. It does not edit, replace or
regenerate the Q3, remediation, Q3-R1, R5-R3 or D1/D2 historical records,
manifests, signed registers, fixtures or evidence. The release-candidate
verification is recorded append-only in `VERIFICATION.md`.
