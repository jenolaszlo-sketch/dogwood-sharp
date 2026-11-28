# CI, packaging and NuGet publication strategy

## D1 verification

CI executes the actual staged bridge for `win-x64`, `linux-x64`, and
`osx-arm64` using .NET SDK 10.0.404 and Rust 1.99.1. Every job verifies
both target frameworks through managed integration, JIT smoke and NativeAOT
smoke. The Linux job also checks native buffer ownership under Valgrind.
CI uploads staged binaries, manifests, test results, and verification logs.
The [verification record](verification.md) identifies completed runs. The
publication workflow is now the exact-artifact gate described in
[release operations](release-operations.md); it remains fail-closed until the
owner-side NuGet policy and explicit publication authorization are in place.

## D3 package gate and release candidate

The D3 package gate produces and verifies the preview release candidate. It
does not publish, sign, tag, release, or modify the publication workflow.
Publication remains a separate owner-authorized commission.

| Candidate | ID and version | Contents/dependencies |
| --- | --- | --- |
| Stable | `DogwoodSharp` `0.1.0-preview.1` | `net8.0`/`net10.0` managed assemblies and docs; all three RID image/manifest pairs; readme, license, notice; matching `.snupkg` with both TFMs' PDBs |
| Experimental adapter | `DogwoodSharp.CedarSharp` `0.1.0-experimental.1` | Only its two TFM managed assemblies/docs, readme, license, notice; exact dependency ranges `[DogwoodSharp 0.1.0-preview.1]` and `[CedarSharp 1.0.0]`; matching `.snupkg` with both TFMs' PDBs |

Each candidate produces a matching symbol package. The SDK moves the PDBs into
the `.snupkg` and keeps them out of the main `.nupkg`; the package gate verifies
both package identities.

The release package version is supplied by the pack gate
(`-p:PackageVersion=...`) and is not the checked-in project `Version`. The
checked-in project version remains the qualification-candidate identity so the
historical probe lockfiles and the qualified restore graph stay reproducible.
The package nuspec, the package filename and the consumer graph all bind the
release version exactly.

The experimental package does not contain native assets and does not repack
CedarSharp. Its checked-in candidate nuspec uses exact dependency ranges while
the source projects retain their locked restore graph. The original
`CedarSharp` `1.0.0` package is staged separately as an unmodified dependency
and checked against its pinned package SHA-256 and a pinned hash over every
package entry.

### Exact local commands

Run from the repository root with .NET SDK `10.0.404` selected by
`global.json` and with all three platform-built native pairs staged under
`artifacts/native/`:

```powershell
./eng/Test-NativePackageAssets.ps1 -AssetsRoot artifacts/native

$candidateRoot = Join-Path $env:TEMP 'dogwoodsharp-d3-candidates'
./eng/Pack-PackageCandidates.ps1 `
  -OutputPath (Join-Path $candidateRoot 'candidates') `
  -DependencyOutputPath (Join-Path $candidateRoot 'dependencies') `
  -ReportPath (Join-Path $candidateRoot 'pack-report.json')

./eng/Test-PackageCandidates.ps1 `
  -PackageDirectory (Join-Path $candidateRoot 'candidates') `
  -CedarSharpPackagePath (Join-Path $candidateRoot 'dependencies/cedarsharp.1.0.0.nupkg') `
  -RequireCedarSharpPackage `
  -ReportPath (Join-Path $candidateRoot 'verification-report.json')
```

These scripts use SDK `10.0.404`, are deterministic, and fail closed. The two
unsigned candidates are normalized to a fixed ZIP timestamp after NuGet packs
them, so repeated producer runs have byte-identical package hashes; the
original CedarSharp dependency is copied and hashed but never rewritten. The
scripts do not contain a publish command. For a local package-only cell, use:

```powershell
./qualification/D3.PackageConsumer/run.ps1 `
  -PackageDirectory (Join-Path $candidateRoot 'candidates') `
  -CedarSharpPackageDirectory (Join-Path $candidateRoot 'dependencies') `
  -RuntimeIdentifier win-x64 `
  -TargetFramework net8.0 `
  -WorkDirectory (Join-Path $env:TEMP 'dogwoodsharp-d3-consumer')
```

### Hosted release-candidate gate

`ci.yml` retains the existing D1/D2 `verify` matrix and adds these producer
jobs:

1. `verify` uploads a dedicated `dogwoodsharp-native-<rid>` artifact containing
   that RID's Dogwood image and manifest.
2. `pack-candidates` downloads all three dedicated native artifacts, validates
   them, packs the two release candidates and their symbol packages with SDK
   `10.0.404`, and uploads the candidates, the original CedarSharp dependency,
   and the consumer kit.
3. `verify-candidates` strictly checks package IDs, versions, TFMs, dependency
   groups, file allowlists, all three RIDs, manifest/image pairs, the absence
   of unexpected runtimes and PDBs from the main packages, the presence and
   identity of both symbol packages, and the exact CedarSharp package hashes.
4. `package-consumer` runs six cells with no repository checkout:
   `windows-2025`/`win-x64`, `ubuntu-24.04`/`linux-x64`, and `macos-15`/
   `osx-arm64`, each for `net8.0` and `net10.0`. Each cell consumes only the
   uploaded packages and consumer kit, uses an isolated NuGet cache and a
   disposable lockfile, runs JIT and NativeAOT, and verifies the package graph
   plus published native files and hashes.
5. `package-consumer-summary` rejects missing, pending, duplicate, failed, or
   extra consumer cells and requires exactly six successful cells.

The consumer project has one direct `PackageReference` to the experimental
candidate. It has no project reference, direct assembly reference, `HintPath`,
native environment override, manual native staging, or source-path package
fallback. The release-candidate packages and consumer kit are uploaded CI
artifacts; they are not published to NuGet.

### Explicit exclusions

This release-candidate gate does **not**:

- publish, sign, push, tag, or release a package;
- trigger or modify `publish.yml`;
- replace the independent qualification and owner acceptance already recorded
  for D1, D2 and D3;
- convert the experimental adapter into a stable public API; or
- authorize distribution outside the controlled release-candidate artifact
  flow.

A green release-candidate matrix is supporting evidence for a separate
publication authorization; it is not publication itself. The exact-artifact
publication workflow is implemented and producer-verified, but it refuses
until the owner-side NuGet policy, environment approval and explicit
publication authorization are in place.

## D0 implementation (historical)

Foundation CI runs on Windows Server 2025 x64 (`win-x64`), Ubuntu 24.04 x64
(`linux-x64`) and macOS 15 ARM64 (`osx-arm64`). Each installs .NET 8 and SDK
10.0.401, builds both net8.0/net10.0, checks formatting, runs foundation tests and
smokes, and runs Rust formatting/clippy/tests/build under toolchain 1.99.1.
The managed vulnerability command reports findings; it is not yet an enforced
severity gate. The empty native crate's zero tests are not native qualification.

NuGet metadata, SourceLink, XML docs, deterministic builds, central versions,
symbols and Apache metadata are configured. This is a historical D0 statement:
at D0, packaging was opt-in for local inspection and no native runtime files
were included. The current preview release candidates are described above;
they are not published artifacts. The solution omits an empty conformance
project, the research projects, and the disposable package-only consumer
intentionally.

The historical D0 publication workflow was manually dispatchable but **always
failed at readiness**; it could not request OIDC tokens or push packages. That
refusal scaffold has since been replaced by the exact-artifact workflow described
below and in [release operations](release-operations.md).

## Reference findings

Baize CI includes build/format, line+branch coverage and independent dependency
qualification; its publish workflow uses NuGet/login OIDC but rebuilds packages
and accepts tag or manual triggers. Adopt its credential pattern, not an
unqualified rebuild. [BZ-CI](https://github.com/jenolaszlo-sketch/penghou-baize/blob/3986dde792f6af49f4f3a51e27db1a6d721d691f/.github/workflows/ci.yml), [BZ-PUBLISH](https://github.com/jenolaszlo-sketch/penghou-baize/blob/3986dde792f6af49f4f3a51e27db1a6d721d691f/.github/workflows/publish.yml).

CedarSharp CI builds native RIDs, runs memory checks, stages provenance manifests,
qualifies package consumers/AOT and verifies packages. Its manual publish workflow
uses CI-produced artifacts and NuGet/login. Use this as the stronger foundation
for exact-artifact publication. [CS-CI](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/.github/workflows/ci.yml), [CS-PUBLISH](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/.github/workflows/publish.yml).

## Implemented exact-artifact publication pipeline

The publication workflow is `.github/workflows/publish.yml`. It:

- is manual-dispatch only, defaults to a dry-run and never runs on push, pull
  request or tag;
- binds the exact release candidate, source CI run/attempt, artifact ID and
  digest, destination, environment and four-artifact inventory;
- verifies package IDs, versions, sizes, hashes, symbol correspondence, native
  assets and the CedarSharp dependency identity before any credential request;
- uses a protected `nuget-publication` environment, GitHub OIDC through
  `NuGet/login@v1`, a short-lived key and job-scoped `id-token: write`;
- pins every action to an immutable commit and uses a publication concurrency
  group with cancellation disabled;
- pushes the stable package and symbol package before the experimental adapter
  and its symbol package, with `--no-symbols` on nupkg pushes and explicit
  `.snupkg` pushes;
- performs no restore, build, pack, native compilation, re-signing or artifact
  transformation; and
- refuses rather than using `--skip-duplicate` or silently masking a conflict.

The exact inventory, owner-side NuGet configuration, repository-signing
verification, symbol-server procedure and partial-publication recovery contract
are in [release operations](release-operations.md).

## Owner-side configuration (required, not completed in this repository)

The owner must:

- confirm `DogwoodSharp` and `DogwoodSharp.CedarSharp` eligibility/ownership on
  NuGet;
- create the NuGet Trusted Publishing policy for GitHub owner
  `jenolaszlo-sketch`, repository `dogwood-sharp`, workflow `publish.yml` and
  environment `nuget-publication`;
- set the `NUGET_USER` repository variable;
- provide an independent reviewer or explicitly accept the current
  self-approval environment limitation; and
- decide whether to add `main` branch protection/rulesets.

The `nuget-publication` environment exists with a required reviewer (the
repository owner), a `main`-only branch policy and administrator bypass. This is
not independent review or separation of duties. Actual publication remains a
separate explicit owner authorization.

DogwoodSharp flat-container returned 404 on 2026-11-16; no published conflict was
observed, but availability/reservation is not guaranteed. See [sources](research/sources.md).
