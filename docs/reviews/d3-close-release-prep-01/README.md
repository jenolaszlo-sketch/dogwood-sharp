# DOGWOOD-SHARP-D3-CLOSE-RELEASE-PREP-01 — D3 closure and release preparation

**Commission**: `DOGWOOD-SHARP-D3-CLOSE-RELEASE-PREP-01`
**Date**: 27 November 2026
**Owner acceptance**: `OWNER-ACCEPTANCE-D3-CLOSE.md`
**Disposition**: **A — D3 CLOSED; RELEASE CANDIDATE READY FOR PUBLICATION AUTHORIZATION**
**Verification**: `VERIFICATION.md`
**Machine-readable record**: `closure.json`
**Evidence manifest**: `manifest.sha256`

---

## 1. Disposition

**A — D3 CLOSED; RELEASE CANDIDATE READY FOR PUBLICATION AUTHORIZATION.**

D3 is formally closed with the owner-accepted Q3-R1 disposition
**B — SUPPORTED WITH QUALIFICATIONS**. All six Q3 findings are closed, the
three residual qualifications are accepted and preserved, and the preview
release candidate has been built and verified against the exact release
candidate commit.

The B disposition is not converted into an unqualified A disposition. The
release-candidate label A refers to the release-candidate state; the
qualification disposition remains B.

No release tag, GitHub release, NuGet publication or new development stage is
authorized by this record. Publication requires a separate explicit owner
authorization and the separately qualified publication pipeline.

---

## 2. Bound identities and source boundary

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

### 2.1 Production identity versus qualification record

The Q3-R1 record revision is a documentation/evidence successor. It is not
the production implementation identity.

The last commit touching the product and test source trees was
`7a22871ae89205a51e382c3cdc238190d927a471`. The qualified production content
is:

| Boundary | Identity at `7a22871` | Identity at the RC | Change |
| --- | --- | --- | --- |
| `src` tree | `50abeca4` | `1af8b0e3` | only `DogwoodSharp.csproj` metadata, the adapter `.csproj` metadata/symbol settings and the adapter README; no `.cs` file changed |
| `native` Rust source and lock | `100ffb3d` | `100ffb3d` | none |
| `tests` source | `93cfa537` | `93cfa537` | none |
| `eng` tree | `7d5d646d` | `edc42c9a` | only the adapter nuspec and the pack/test candidate scripts; `eng/Build-Native.ps1`, `eng/Verify.ps1` and `eng/Test-NativePackageAssets.ps1` are unchanged |
| `native/dogwoodsharp-bridge/Cargo.lock` | `ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa` | unchanged | none |

The RC changes `eng/Pack-PackageCandidates.ps1`,
`eng/Test-PackageCandidates.ps1`, the adapter nuspec, the package-consumer kit
and documentation. These are release packaging and verification tooling, not
product or native behavior. No independent delta qualification is required;
the package gate and the shipped-subject campaign re-verify the rebuilt
artifacts from the unchanged product source.

---

## 3. Q3 finding dispositions

| Finding | Disposition | Independent basis |
| --- | --- | --- |
| Q3-F01 | **Closed** | Independent verifier 71/71, 47/47, 57/57 and self-test 7/7 on Windows and Linux; remediation 74/74; Q3-R1 64/64. |
| Q3-F02 | **Closed** | Six cells, 124/124 cases, 26/26 families; resident image equals the verified product bridge; qualification-only bridge rejected with `Native asset SHA-256 differs from the manifest.` |
| Q3-F03 | **Closed** | Committed lock `ec901557...` in all six cells; the retired path's 36-package divergence is reproduced; Windows images differ in 24 embedded metadata bytes and normalize to `28add84f...`. |
| Q3-F04 | **Closed, bounded external qualification** | Package `99cc3659...`; nuspec commit `68ffdafa...`; published natives `77de6976...`, `85433a35...`, `290fcce9...`; package consumer 6/6. Native `sourceCommit 324d3c0` is not recoverable. |
| Q3-F05 | **Closed** | Owner RD-01 decision; original OQ-01, seven controls and current-invalid control re-executed; FCN-05 remains TC-03 through TC-06. |
| Q3-F10 | **Closed** | Zero changes under `docs/reviews/d3-impl-01-r5-r3` from `e980ee2` to `d2d9ad7`; original manifests/fixtures preserved; successor manifests 47/47 and 57/57. |

---

## 4. Accepted residual qualifications

1. **External CedarSharp native source metadata.** The published
   `CedarSharp 1.0.0` package and all three native images are hash-verified and
   the package nuspec commit is recoverable. The native manifests declare a
   `sourceCommit` that is not recoverable. The package and native hashes are
   the binding consumer identities; source reproducibility of the external
   native is not claimed.
2. **Windows binary metadata nondeterminism.** Same-source/same-lock Windows
   builds can differ in embedded PE/debug timestamps and the PDB GUID. The
   exact release-candidate SHA-256 binds the publication artifact; normalized
   equality is not a substitute for exact artifact identity.
3. **F04 evidence-path documentation defect.** The remediation F04 evidence
   path resolves under the Q3 reviewer tree, not the remediation tree. The
   content is present, hash-verified and covered by the Q3 manifest. See
   `F04-EVIDENCE-PATH-CLARIFICATION.md`.

These qualifications do not waive any mandatory execution, provenance or
package-verification requirement.

---

## 5. D1, D2 and D3 reconciliation

| Stage | Independent qualification | Disposition | Owner record | Closure state |
| --- | --- | --- | --- | --- |
| D1 | `DOGWOOD-SHARP-D1-QUAL-01` | Qualified; no blocking findings | `docs/d1-acceptance.md` | Closed with QF-01, QF-02 and QF-03 preserved |
| D2 | `DOGWOOD-SHARP-D2-QUAL-02` | B — supported with qualifications | `docs/d2-acceptance.md` | Closed with QD2-01, QD2-02 and QD2-03 preserved |
| D3 | `DOGWOOD-SHARP-D3-QUAL-01-Q3-R1` | B — supported with qualifications | `OWNER-ACCEPTANCE-D3-CLOSE.md` | Closed with Q3R1-Q1, Q3R1-Q2 and Q3R1-Q3 preserved |

The D1 and D2 acceptance records remain in force. Their historical evidence
trees, lockfiles and manifests are unchanged. The release-candidate CI runs the
D1 consumer regression, the D2 suites and differential, the D3 tests and the
six-cell D3 campaign. D1/D2 behavior is not reinterpreted as D3 authorization
evidence, and D3 does not widen D1 or D2's qualified scope.

The formal stage-closure reconciliation is satisfied:

- D1 and D2 owner acceptance records are present and unmodified.
- The Q3, remediation and Q3-R1 evidence manifests verify 71/71, 74/74 and
  64/64.
- The six Q3 findings are closed.
- RD-01 and FCN-05 scopes are preserved.
- No additional mandatory D3 closure requirement remains outstanding.

---

## 6. Supported platform and framework matrix

| OS | RID | Frameworks | Release-candidate evidence |
| --- | --- | --- | --- |
| Windows Server 2025 | `win-x64` | `net8.0`, `net10.0` | native build, D1/D2/D3 tests, package consumer, 124-case campaign |
| Ubuntu 24.04 | `linux-x64` | `net8.0`, `net10.0` | native build, D1/D2/D3 tests, package consumer, 124-case campaign |
| macOS 15 (ARM64) | `osx-arm64` | `net8.0`, `net10.0` | native build, D1/D2/D3 tests, package consumer, 124-case campaign |

No other OS, RID, architecture or framework is claimed. A cell that did not
run is not claimed as supported.

---

## 7. Contract exclusions and limitations

- **No upstream-equivalence claim.** The adapter is a schema-carrying,
  request-validating contract with the approved ED-01 through ED-05
  differences. Diagnostic wording is not an equality criterion.
- **Experimental adapter.** `DogwoodSharp.CedarSharp` is not a stable public
  API. The stable `DogwoodSharp` package's public D1/D2 surface is unchanged;
  the D3 export surface remains internal.
- **No schema-less fallback.** Lowering, schema, request-validation, identity,
  native or transport failure never authorizes. The adapter does not add bare
  scope entities or synthesize entity attributes, parents or action
  relationships.
- **Trusted temporal/provider boundary.** Caller context containing the
  generated temporal field is rejected before dispatch; no caller value can
  replace the trusted binding.
- **Resident native lifetime.** The bridge and CedarSharp images remain
  resident for the process lifetime. Unload, hot replacement and in-process
  native-memory reclamation are outside the supported boundary; process
  isolation is the recovery boundary.
- **Input and response limits.** The documented parsing, request and response
  limits apply. They are not a general memory quota or wall-time guarantee.
- **RD-01.** Limited to the original OQ-01 reference-defect case; not an FCN
  and not a general historical-evaluation exception.
- **FCN-05.** Limited to TC-03 through TC-06; the signed 124-case register is
  unchanged and not expanded.
- **CedarSharp provenance.** The published package and native images are bound
  by hashes. Source reproducibility of the external native is not claimed.
- **Windows metadata.** The exact release-candidate hash is binding; normalized
  binary equality is not a publication identity.
- **Later stages.** D4, D5, D6, D7, D9 and D10 remain separately gated. This
  record prepares only the preview release candidate.

---

## 8. RD-01 and FCN-05 preservation

- RD-01 covers exactly
  `R5R1-OQ-01-temporal-history-event-policy-evaluation-error`.
- FCN-05 covers exactly `TC-03`, `TC-04`, `TC-05` and `TC-06`.
- Signed register canonical SHA-256:
  `5962c4dc7191444e4dedb66db87e74d816b0e5f7cb7cf60af7ae5be11da65e3d`.
- Corrected fixture canonical SHA-256:
  `568267e0b31e233505ce322e54d4f9262d83647d7cc7a307c3c1500c598ff9f6`.
- The Q3-R1 owner decision is preserved in
  `docs/reviews/d3-qualification-01-q3-r1/OWNER-DECISION-RD01.md`.
- The current-invalid control fails closed on D3, so RD-01 does not create a
  general continuation-after-invalid-replay exception.

---

## 9. Repository reconciliation

| Item | Finding |
| --- | --- |
| Canonical repository | `jenolaszlo-sketch/dogwood-sharp` |
| Local `main` at review start | `d8ea978` |
| Remote `main` at review start | `d8ea978` |
| Local/remote branches | `main` only; no divergent or stale branches |
| Registered worktrees | main; a prunable detached temp worktree at `ee870e9` (ancestor, no unique commits); `D:\q3-independent` at `aa31995` (ancestor, scratch evidence) |
| Unmerged commits | none |
| Uncommitted changes | pre-existing untracked scratch files only (`artifacts-downloaded/`, `console.log`, `dispatch.json`, `lifecycle-corrected-reference.json`, `observations-corrected-reference.json`); not canonical evidence; not deleted |
| Unintegrated implementation or qualification records | none |
| Evidence retained exclusively outside canonical history | none identified; the Q3-R1 independent evidence is committed, and the build/worktree directories are disposable or ancestor scratch |
| Existing release preparation changes | none before this commission |

No destructive cleanup, history rewrite or unique-evidence deletion was
performed.

---

## 10. Release version determination

### 10.1 Public history

| Source | Result on 27 November 2026 |
| --- | --- |
| NuGet `dogwoodsharp` flat container | HTTP 404; no published versions |
| NuGet `dogwoodsharp.cedarsharp` flat container | HTTP 404; no published versions |
| GitHub repository tags | none (`[]`) |
| GitHub releases | none |
| Reserved/attempted public identities | `0.0.0-discovery.1` and `0.0.0-experimental.1` were qualification candidates only and were never published |

No prior public package contract, release tag or published version exists.
The `0.0.0-*` identities are retired as publication identities.

### 10.2 Proposed version

| Package | Proposed version | Rationale |
| --- | --- | --- |
| `DogwoodSharp` | `0.1.0-preview.1` | First public preview; D1/D2 stable surface, pre-1.0 package; no prior version to supersede |
| `DogwoodSharp.CedarSharp` | `0.1.0-experimental.1` | First public experimental adapter for the D3 capability increment; exact dependency on the preview stable package and CedarSharp 1.0.0 |

This is a preview release candidate, not a stable 1.0.0 or D10 release. D9
preview readiness and D10 stable readiness remain separate decisions.

The package version is supplied by the pack gate
(`-p:PackageVersion=...`). The packed assemblies' `InformationalVersion` is
set to the same release version. The checked-in project `Version` remains the
qualification-candidate identity so historical probe lockfiles and the
qualified restore graph stay reproducible. The package nuspec, package
filename, dependency ranges and consumer graph all bind the release version
exactly.

---

## 11. Release preparation changes

Release-related changes only:

- package version metadata and package descriptions/tags;
- `CHANGELOG.md` and `docs/release-notes/v0.1.0-preview.1.md`;
- `README.md`, `NOTICE`, `docs/ci-publishing.md`, `docs/managed-api.md`,
  `docs/architecture/native-artifacts.md`, `docs/roadmap.md` and the D3
  producer-report closure note;
- the package gate, symbol-package production/verification and the
  package-only consumer kit;
- the stable and experimental package READMEs.

No `.cs`, `.rs`, `Cargo.lock`, NuGet lockfile, dependency version, native
source or test source changed. No feature, production refactor, dependency
upgrade or architectural change was introduced.

---

## 12. Release-candidate verification

The exact release candidate is `58b0a33` / tree `4c535ac8`. The hosted CI,
six-cell 124-case campaign, six package-only consumer cells, package hashes
and negative controls are recorded in `VERIFICATION.md`.

No release-candidate blocker remains after that verification. The separate
publication-commission prerequisites are:

- implement and qualify the exact-artifact OIDC publication pipeline (the
  current `publish.yml` is a refusal gate);
- configure the owner-side NuGet trusted publisher and protected environment;
- obtain explicit owner publication authorization.

These are publication authorization prerequisites, not D3 closure or
release-candidate verification blockers.

---

## 13. Release eligibility

**D3 is closed. The preview release candidate is eligible for a separate
publication authorization decision.**

The owner acceptance is recorded in `OWNER-ACCEPTANCE-D3-CLOSE.md`. Publication
is not performed or authorized by this record.

---

## 14. Recommended publication commission

A separate commission should:

1. verify the exact RC commit/tree and the artifact hashes in
   `VERIFICATION.md`;
2. implement and qualify the exact-artifact OIDC publication pipeline;
3. configure the protected NuGet trusted-publisher environment;
4. re-verify the nupkg/snupkg hashes immediately before push;
5. publish only after explicit owner authorization;
6. record the publication identities and preserve the refusal-path evidence.

---

## 15. Authority exclusions

This record does not authorize:

- release tagging;
- GitHub release publication;
- NuGet publication;
- new development stages;
- dependency modernization;
- deterministic native build remediation; or
- destructive repository or worktree cleanup.

Publication remains subject to separate explicit owner authorization.

---

## 16. Evidence manifest

`manifest.sha256` covers the files in this record except the manifest itself,
using committed Git blob bytes. The evidence layout and verification command
are in `VERIFICATION.md`.
