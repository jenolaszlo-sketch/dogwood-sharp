# Changelog

All notable changes to the DogwoodSharp preview release candidate are recorded
here. This file does not itself authorize publication.

## 0.1.0-preview.1 — 27 November 2026

### Added

- Experimental `DogwoodSharp.CedarSharp` `0.1.0-experimental.1` adapter for
  schema-carrying, request-validating CedarSharp authorization.
- Exact per-rule Cedar policy identity export (native operation 4) and strict
  managed bundle construction.
- Clean-Allow-only enforcement, fail-closed stage/category diagnostics, and
  trusted temporal/provider runtime paths.
- Six-cell package-only JIT and NativeAOT consumer verification.
- Matching `.snupkg` symbol packages for both TFMs of both packages.
- D3 closure and release-preparation record.

### Changed

- `DogwoodSharp` is prepared as `0.1.0-preview.1`. The closed D1/D2 public
  API is unchanged; the D3 export surface remains internal.
- `DogwoodSharp.CedarSharp` is prepared as `0.1.0-experimental.1` with exact
  dependencies on `DogwoodSharp` `[0.1.0-preview.1]` and `CedarSharp`
  `[1.0.0]`.
- Package documentation now states the supported matrix, the experimental
  adapter boundary, and the CedarSharp and Windows native qualifications.

### Qualification

- D1, D2 and D3 are closed for the reconciled subjects recorded in
  `docs/d1-acceptance.md`, `docs/d2-acceptance.md` and
  `docs/reviews/d3-close-release-prep-01/README.md`.
- D3 carries the accepted Q3-R1 disposition **B — supported with
  qualifications**. The qualifications are not converted into an unqualified
  A disposition.
- RD-01 is limited to the original OQ-01 case. FCN-05 remains exactly
  TC-03 through TC-06. The signed 124-case register and corrected fixture are
  unchanged.

### Known qualifications

- The external `CedarSharp 1.0.0` native manifests declare a `sourceCommit`
  that is not recoverable. The published package and native images are bound
  by exact hashes; source reproducibility is not claimed.
- Same-source Windows native builds can differ in embedded PE/debug timestamps
  and the PDB GUID. The exact release-candidate SHA-256 binds the published
  image.
- The adapter remains experimental and is not a stable public API contract.
- Publication requires a separate explicit owner authorization and a
  separately qualified publication pipeline.
