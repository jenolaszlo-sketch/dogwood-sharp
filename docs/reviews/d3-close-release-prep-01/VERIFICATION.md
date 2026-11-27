# Release-candidate verification — `58b0a33` / tree `4c535ac8`

**Commission**: `DOGWOOD-SHARP-D3-CLOSE-RELEASE-PREP-01`
**Release candidate**: `58b0a3332391d85cdf19fd7b794d79516d2ecdfe` / tree `4c535ac8640ec5a1086d0956017e80b63850ef98`
**Verification date**: 27 November 2026
**Disposition**: **A — D3 CLOSED; RELEASE CANDIDATE READY FOR PUBLICATION AUTHORIZATION**

This file is append-only evidence for `README.md` and
`OWNER-ACCEPTANCE-D3-CLOSE.md`. It records the exact hosted run identities,
artifact hashes, package-consumer results and differential results for the
release candidate. It does not authorize publication.

---

## 1. Hosted execution identities

| Purpose | Run | Head | Conclusion | Duration |
| --- | --- | --- | --- | --- |
| Producer CI: build, package, symbols and six package-only consumers | `46053558029` | `58b0a33` | **success** | 21m42s |
| Shipped-subject 124-case differential (six cells) | `46053620392` | `58b0a33` | **success** | 15m53s |
| D3 R5-R3 cross-platform differential (four hosted cells) | `46053558043` | `58b0a33` | **success** | 11m35s |
| Evidence-integrity verification (Windows and Linux) | `46053625568` | `58b0a33` | **success** | 31s |

Hosted images: `windows-2025`, `ubuntu-24.04`, `macos-15`. Package consumer SDK:
`10.0.404`. The Dogwood bridge uses Rust `1.99.1`; the published CedarSharp
native uses its pinned Rust `1.94.0`.

Producer CI jobs:

| Job | Conclusion |
| --- | --- |
| `Verify win-x64 / net8.0 and net10.0` | success |
| `Verify linux-x64 / net8.0 and net10.0` | success |
| `Verify osx-arm64 / net8.0 and net10.0` | success |
| `Pack D3 package candidates` | success |
| `Verify D3 package candidates` | success |
| `Package consumer / win-x64 / net8.0` | success |
| `Package consumer / win-x64 / net10.0` | success |
| `Package consumer / linux-x64 / net8.0` | success |
| `Package consumer / linux-x64 / net10.0` | success |
| `Package consumer / osx-arm64 / net8.0` | success |
| `Package consumer / osx-arm64 / net10.0` | success |
| `D3 package consumer summary` | success |

---

## 2. Evidence manifest verification

| Manifest | Entries | Result | Evidence |
| --- | --- | --- | --- |
| Q3 reviewer manifest | 71 | **71/71** | hosted run `46053625568` (Windows and Linux); local `--git HEAD` |
| Remediation manifest | 74 | **74/74** | local `--git HEAD`; Q3/remediation fields in the hosted evidence |
| Q3-R1 manifest | 64 | **64/64** | local `--git HEAD` |
| R5-R3 canonical at `b23f6a9` | 47 | **47/47** | hosted run `46053625568` |
| R5-R3 canonical at `HEAD` | 57 | **57/57** | hosted run `46053625568` |
| Manifest self-test | 7 controls | **7/7** | hosted run `46053625568` |

Self-test controls: exact LF pass, changed content, missing artifact, stale
checksum, CRLF-only mismatch, Q3 git manifest pass, and stale-copy rejection.
The CRLF control is explicitly not silently accepted.

Machine-readable results:

- `evidence/local/q3-manifest-verification.json`
- `evidence/local/remediation-manifest-verification.json`
- `evidence/local/q3r1-manifest-verification.json`
- `evidence/manifest-verification/windows/*.json`
- `evidence/manifest-verification/linux/*.json`

Preserved signed identities:

- Signed 124-case register, canonical SHA-256
  `5962c4dc7191444e4dedb66db87e74d816b0e5f7cb7cf60af7ae5be11da65e3d`.
- Corrected fixture, canonical SHA-256
  `568267e0b31e233505ce322e54d4f9262d83647d7cc7a307c3c1500c598ff9f6`.

---

## 3. Package artifact inventory

The package gate produced and verified exactly four publication candidates.
The hashes below were recomputed independently from the downloaded CI
artifacts and match `verification-report.json` (`allMatch: true`).

| Artifact | Size (bytes) | SHA-256 |
| --- | ---: | --- |
| `DogwoodSharp.0.1.0-preview.1.nupkg` | 18,772,211 | `384e013a236bfcb9b761a92ff861f0df54b2e8a736a5f2c1fb778268307b1911` |
| `DogwoodSharp.0.1.0-preview.1.snupkg` | 26,896 | `e35ae30ef409b44402f32361dcec800a57b7499cd610e2345773d6adafbad563` |
| `DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg` | 53,207 | `3d882e6be062ed998cf05e485b06dee4868819785e22fdf702d5050ba306daa4` |
| `DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg` | 23,593 | `49cefe0d69af687659c60decb141c56a1773b0583784586cd3ee09e5ea1803fd` |
| `CedarSharp.1.0.0.nupkg` (external dependency, not repacked) | 10,797,439 | `99cc3659e750f3d5187d7b8c35e84359811a7af1247b3673d893eafc679bf835` |

CedarSharp package entry count: **741**. Aggregate entry hash:
`2bfb631cf3acbea3fe807983518660d717e4fc43794cea2d5e0ceeea78cd1482`.
The package and per-entry hashes are pinned in
`evidence/package/CedarSharp-1.0.0.package-hashes.json`.

### 3.1 Package metadata

- Stable: `DogwoodSharp` `0.1.0-preview.1`; no package dependencies.
- Experimental: `DogwoodSharp.CedarSharp` `0.1.0-experimental.1`; exact
  dependencies `DogwoodSharp [0.1.0-preview.1]` and `CedarSharp [1.0.0]` for
  both `net8.0` and `net10.0`.
- Stable entries: both TFMs' DLL/XML, all three RID image/manifest pairs,
  `README.md`, `LICENSE`, `NOTICE`. No PDBs in the main package.
- Experimental entries: both TFMs' DLL/XML, `README.md`, `LICENSE`, `NOTICE`.
  No native assets and no PDBs in the main package.
- Symbol packages: `lib/net8.0/DogwoodSharp.pdb`, `lib/net10.0/DogwoodSharp.pdb`,
  `lib/net8.0/DogwoodSharp.CedarSharp.pdb`,
  `lib/net10.0/DogwoodSharp.CedarSharp.pdb`.
- Assembly `ProductVersion` in the packaged assemblies:
  `0.1.0-preview.1+58b0a333...` and
  `0.1.0-experimental.1+58b0a333...`.
- Cargo lock: `ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa`.

Machine-readable inventory: `evidence/package/package-inventory.json`.
Full entry lists and native-asset records: `evidence/package/verification-report.json`.

---

## 4. Native asset identity

The stable package's native images exactly match the CI-built native artifacts
downloaded from the same run (`dogwoodsharp-native-<rid>`).

| RID | Package image SHA-256 | Package manifest SHA-256 | CI artifact match |
| --- | --- | --- | --- |
| `win-x64` | `61e92101b1aa127093635fa9a93f3d8248008dc9ba67e2ca6adcaeba3142cacc` | `cd6104b6090e3072bb09b5f3e47519184d33d87437d3e6da6491a87a96f4e28c` | exact |
| `linux-x64` | `39afbfd0b8ed60d627af6e3e0e743c594e8d72817fee16f70d98a39081dafbe9` | `789f3fd550db7dcd2ca82b54a0d7f58ec015336cef8aeb963cb74b2a6a09610e` | exact |
| `osx-arm64` | `fbd2e98ac6dc393b3afb42bc9684f3843f75f030107ce72cae675469c17db42e` | `bd951df87a3f681b3176cce134fe74859d9b6a25aaa759edbf240bf9c9a98bed` | exact |

Every manifest records the committed lock `ec901557...`, Dogwood `1.1.2` at
`929f49e7305534ac61ef06cf8e47269c596dc851`, Cedar `4.11.0`, Rust `1.99.1`,
ABI `1` and the expected target triple.

### 4.1 Windows exact-candidate identity

The Windows package image is `61e92101...`. The shipped campaign rebuilt the
same source and lock independently and produced `6797ea3f...` (`net8.0`) and
`21fffcde...` (`net10.0`). These are different exact candidates; their
difference is the accepted embedded PE/debug timestamp and PDB-GUID
nondeterminism (Q3R1-Q2). The package image `61e92101...` is the publication
artifact and is the image exercised by the package consumer and the D1
consumer regression. No normalized-equivalence claim substitutes for the exact
hash.

Linux and macOS package images are byte-identical to the shipped-campaign
subjects (`39afbfd0...` and `fbd2e98a...`).

### 4.2 CedarSharp identity

| RID | Published native SHA-256 | Package consumer |
| --- | --- | --- |
| `win-x64` | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` | pass |
| `linux-x64` | `85433a352ed9b6b0122e77a4e37cbe9ba1354872641e217350bdf871ea33ebf6` | pass |
| `osx-arm64` | `290fcce995f70cd963e024d991fe7f9e2806fde81add9749304ce222b904258b` | pass |

The published package hash is `99cc3659...`. The native manifests still declare
the unrecoverable `sourceCommit`; the published package and native hashes are
the binding identities. This is the accepted Q3R1-Q1 qualification.

---

## 5. Six package-only consumer cells

Each cell used only the uploaded `nupkg`/`snupkg` feed and the consumer kit,
with an isolated NuGet cache, a disposable lockfile, JIT and NativeAOT. No
project reference, source fallback, native override or manual staging was used.

| Cell | Job | Conclusion | Result |
| --- | --- | --- | --- |
| `win-x64` / `net8.0` | `140698311840` | success | `PASS RID=win-x64 TFM=net8.0` |
| `win-x64` / `net10.0` | `140698311764` | success | `PASS RID=win-x64 TFM=net10.0` |
| `linux-x64` / `net8.0` | `140698311741` | success | `PASS RID=linux-x64 TFM=net8.0` |
| `linux-x64` / `net10.0` | `140698311736` | success | `PASS RID=linux-x64 TFM=net10.0` |
| `osx-arm64` / `net8.0` | `140698311859` | success | `PASS RID=osx-arm64 TFM=net8.0` |
| `osx-arm64` / `net10.0` | `140698311793` | success | `PASS RID=osx-arm64 TFM=net10.0` |

Summary job `D3 package consumer summary`: **success**, exactly six cells.

The consumer verifies the package graph, the published Dogwood and CedarSharp
native files and hashes, native identity, clean Allow/Deny, determining policy
identity, trusted temporal runtime, and fail-closed out-of-scope/malformed
controls. Machine-readable results: `evidence/package/consumer-results.json`.

---

## 6. Six-cell 124-case differential campaign

Run `46053620392` at `58b0a33` executed all six cells. Every cell reports
`total 124`, `passed 124`, `failed 0`, `pending 0`, `familyCount 26`, with
identical classification totals: **84 agreement, 22 approved-difference, 9
fail-closed-equivalence, 9 operation-failure**.

| Cell | Product bridge SHA-256 | CedarSharp SHA-256 | Resident | Negative control |
| --- | --- | --- | --- | --- |
| `win-x64` / `net8.0` | `6797ea3f52bf3943125a649107fdfa072ab1b86973f62cf4f5efe3c9cfd14599` | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` | pass | pass (exit 2) |
| `win-x64` / `net10.0` | `21fffcde18db2c429984493e01f6a20962fdbf3f13266ee0bfccdce1ff11bade` | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` | pass | pass (exit 2) |
| `linux-x64` / `net8.0` | `39afbfd0b8ed60d627af6e3e0e743c594e8d72817fee16f70d98a39081dafbe9` | `85433a352ed9b6b0122e77a4e37cbe9ba1354872641e217350bdf871ea33ebf6` | pass | pass (exit 2) |
| `linux-x64` / `net10.0` | `39afbfd0b8ed60d627af6e3e0e743c594e8d72817fee16f70d98a39081dafbe9` | `85433a352ed9b6b0122e77a4e37cbe9ba1354872641e217350bdf871ea33ebf6` | pass | pass (exit 2) |
| `osx-arm64` / `net8.0` | `fbd2e98ac6dc393b3afb42bc9684f3843f75f030107ce72cae675469c17db42e` | `290fcce995f70cd963e024d991fe7f9e2806fde81add9749304ce222b904258b` | pass | pass (exit 2) |
| `osx-arm64` / `net10.0` | `fbd2e98ac6dc393b3afb42bc9684f3843f75f030107ce72cae675469c17db42e` | `290fcce995f70cd963e024d991fe7f9e2806fde81add9749304ce222b904258b` | pass | pass (exit 2) |

The resident control records the loaded module path and hash; every cell's
resident hash equals its product-bridge hash. The negative control substitutes
the qualification-only patched bridge under the product manifest; every cell
rejects it with `Native asset SHA-256 differs from the manifest.` and exit
code 2 before authorization.

Producer comparison (`46039052322` vs `46053620392`) is in
`evidence/shipped-campaign/campaign-comparison.json`:

- every one of the 124 classifications agrees in every cell;
- Linux and macOS product-bridge hashes are identical across the two runs;
- Windows product-bridge hashes differ only in embedded build metadata, as
  accepted in Q3R1-Q2.

Machine-readable results: `evidence/shipped-campaign/results.json`.
Per-cell observations, lifecycle records, product assets and negative-control
logs are under `evidence/shipped-campaign/<rid>/<tfm>/`.

---

## 7. D1/D2/D3 regression evidence

All three producer `Verify` jobs passed. The release candidate's native image
is the same image used by the D1 qualification and D2-R2 regression.

| Check | `win-x64` | `linux-x64` | `osx-arm64` |
| --- | --- | --- | --- |
| D1 consumer qualification | pass | pass | pass |
| D1/D2 managed tests, `net8.0` | 71/71 | 71/71 | 71/71 |
| D1/D2 managed tests, `net10.0` | 71/71 | 71/71 | 71/71 |
| D3 managed tests, `net8.0` | 96/96 | 96/96 | 96/96 |
| D3 managed tests, `net10.0` | 96/96 | 96/96 | 96/96 |
| Native tests | 32/32 | 32/32 | 32/32 |
| D2 differential | 39 cases, 0 failures | 39 cases, 0 failures | 39 cases, 0 failures |
| D2-R2 archived corpus | pass | pass | pass |
| Valgrind (native ownership) | n/a | 0 errors, 0 definitely lost | n/a |
| NativeAOT consumers | pass | pass | pass |

The D1 qualification records the exact package native hash for each RID:
`61e92101...` (Windows), `39afbfd0...` (Linux), `fbd2e98a...` (macOS), and the
committed lock `ec901557...`. Machine-readable summary:
`evidence/regression/regression-summary.json`; per-RID D1 records and the Linux
Valgrind summary are alongside it.

This preserves the D1 acceptance and D2 acceptance qualifications. It does not
reinterpret D1 or D2 as D3 authorization evidence.

---

## 8. RD-01 and FCN-05 preservation

| Item | Identity | Verification |
| --- | --- | --- |
| Signed 124-case register | canonical `5962c4dc...` | R5-R3 HEAD manifest 57/57; unchanged |
| Corrected fixture | canonical `568267e0...` | R5-R3 HEAD manifest 57/57; unchanged |
| FCN-05 scope | exactly `TC-03`, `TC-04`, `TC-05`, `TC-06` | `docs/reviews/d3-impl-01-r5-r4/fcn-05.json`, SHA-256 `c463d00e4801fef01964c2f14e98b53bef56162e1233703bd03f79112c0a2bc7` |
| RD-01 owner decision | original OQ-01 only | Q3-R1 manifest 64/64; owner decision preserved |
| Current-invalid control | D3 fails closed | Q3-R1 evidence; unchanged |

The signed 124-case register does **not** contain the TC-03 through TC-06
controls; FCN-05 is a supplemental reference-defect normalization, not a
member of the signed corpus. The release candidate therefore preserves FCN-05
by preserving the signed corpus, the corrected fixture, the FCN-05 record and
the committed Q3-R1 re-execution evidence. It does not claim that the 124-case
campaign executes those four supplemental controls.

No release-preparation change modified the register, fixture, FCN-05 record or
RD-01 owner decision.

---

## 9. Artifact and evidence retention

The four publication-candidate files are retained as CI artifacts of run
`46053558029` (`dogwoodsharp-d3-candidates`). Their exact SHA-256 values are
recorded above and in `evidence/package/package-inventory.json`. The external
CedarSharp dependency is retained as `dogwoodsharp-d3-dependencies` with the
same pinned hash.

This record's manifest, `manifest.sha256`, covers the files in this review
directory except the manifest itself, using committed Git blob bytes. It is
generated after this file is committed and verified against `HEAD`.

No production source, native source, test source, dependency graph, lockfile
or signed historical record was changed by the release candidate.

---

## 10. Conclusion

All release-candidate verification requirements passed for the exact candidate
`58b0a33` / tree `4c535ac8`:

- locked dependency restoration and exact CedarSharp `1.0.0` identity;
- correct RID-specific native assets, matching the CI build and the package;
- package content, metadata and symbol packages;
- actual resident native identity and negative native-identity controls;
- six package-only JIT/NativeAOT consumer cells;
- D1, D2 and D3 regression suites and the D2 differential;
- the six-cell 124-case D3 differential campaign;
- RD-01 and FCN-05 preservation;
- successful hosted CI for the exact release candidate.

No release-candidate blocker remains. Publication remains a separate explicit
owner authorization and requires the separately qualified publication pipeline
and owner-side NuGet configuration described in `README.md`.
