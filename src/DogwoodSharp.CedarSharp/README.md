# DogwoodSharp.CedarSharp (experimental preview)

`DogwoodSharp.CedarSharp` is the **experimental** CedarSharp authorization
adapter for Dogwood policy lowering. It is prepared as a preview release
candidate under `DOGWOOD-SHARP-D3-CLOSE-RELEASE-PREP-01`; publication requires
separate explicit owner authorization.

| Package | Version | Role |
| --- | --- | --- |
| `DogwoodSharp.CedarSharp` | `0.1.0-experimental.1` | This experimental adapter |
| `DogwoodSharp` | `0.1.0-preview.1` | Stable managed API and Dogwood native assets |
| `CedarSharp` | `1.0.0` | Cedar authorization engine (exact dependency) |

The package targets `net8.0` and `net10.0`. It is trimmable and NativeAOT
compatible. Its dependency list contains only exact ranges for the stable
`DogwoodSharp` and `CedarSharp` 1.0.0 packages; neither is repacked into this
package. The package-only consumer references only this adapter package
directly and resolves the other two transitively.

## Supported matrix

| OS | RID | Frameworks |
| --- | --- | --- |
| Windows Server 2025 | `win-x64` | `net8.0`, `net10.0` |
| Ubuntu 24.04 | `linux-x64` | `net8.0`, `net10.0` |
| macOS 15 (ARM64) | `osx-arm64` | `net8.0`, `net10.0` |

No other RID or framework is claimed.

## Qualification boundary

D3 is closed as **B — supported with qualifications** for the exact subject
reconciled in the D3 closure record. The adapter is an experimental surface:
its API is not a stable public contract, and it must not be treated as an
upstream-equivalence claim.

The published `CedarSharp` 1.0.0 package and its three native images are bound
by immutable package and native hashes. The native manifests declare a
`sourceCommit` that is not recoverable from the published repository; the
package nuspec commit is recoverable. This release does **not** claim source
reproducibility of the external CedarSharp native. See the release notes and
the closure record for the exact limitation.

The Windows Dogwood bridge is bound by the exact release-candidate SHA-256.
Same-source Windows builds can differ in embedded PE/debug timestamps and the
PDB GUID; that metadata nondeterminism is not a claim of cross-build byte
equality.

This package is **not authorized for publication, signing, tagging, or
release** by this document. See `docs/ci-publishing.md` for the package gate
and exclusions.
