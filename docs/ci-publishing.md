# CI, packaging and NuGet publication strategy

## D1 verification

CI executes the actual staged bridge for `win-x64`, `linux-x64`, and
`osx-arm64` using .NET SDK 10.0.404 and Rust 1.99.1. Every job verifies
both target frameworks through managed integration, JIT smoke and NativeAOT
smoke. The Linux job also checks native buffer ownership under Valgrind.
CI uploads staged binaries, manifests, test results, and verification logs.
The [verification record](verification.md) identifies completed runs. The
publication workflow remains a refusal gate.

## D0 implementation (historical)

Foundation CI runs on Windows Server 2025 x64 (`win-x64`), Ubuntu 24.04 x64
(`linux-x64`) and macOS 15 ARM64 (`osx-arm64`). Each installs .NET 8 and SDK
10.0.401, builds both net8.0/net10.0, checks formatting, runs foundation tests and
smokes, and runs Rust formatting/clippy/tests/build under toolchain 1.99.1.
The managed vulnerability command reports findings; it is not yet an enforced
severity gate. The empty native crate's zero tests are not native qualification.

NuGet metadata, SourceLink, XML docs, deterministic builds, central versions,
symbols and Apache metadata are configured. Packaging is opt-in for local D0
inspection only. No production native runtime files are included. The solution
omits an empty conformance project and the research projects intentionally.

Publication workflow is manually dispatchable but **always fails at readiness**.
It cannot request OIDC tokens or push packages. D0 is ineligible for publication;
the release pipeline below must be implemented and independently qualified at D8.
This is a deliberate scaffold, not a completed publishing workflow.

## Reference findings

Baize CI includes build/format, line+branch coverage and independent dependency
qualification; its publish workflow uses NuGet/login OIDC but rebuilds packages
and accepts tag or manual triggers. Adopt its credential pattern, not an
unqualified rebuild. [BZ-CI](https://github.com/jenolaszlo-sketch/penghou-baize/blob/3986dde792f6af49f4f3a51e27db1a6d721d691f/.github/workflows/ci.yml), [BZ-PUBLISH](https://github.com/jenolaszlo-sketch/penghou-baize/blob/3986dde792f6af49f4f3a51e27db1a6d721d691f/.github/workflows/publish.yml).

CedarSharp CI builds native RIDs, runs memory checks, stages provenance manifests,
qualifies package consumers/AOT and verifies packages. Its manual publish workflow
uses CI-produced artifacts and NuGet/login. Use this as the stronger foundation
for exact-artifact publication. [CS-CI](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/.github/workflows/ci.yml), [CS-PUBLISH](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/.github/workflows/publish.yml).

## Release pipeline to implement at D8

1. Build pinned native sources on explicit runners/targets; enforce rustfmt,
   clippy, tests, dependency audits and complete feature/license inventories.
2. Stage each binary plus manifest (ABI, source/lock/toolchain/target/features/hash).
   Upload artifacts with provenance; no generated binaries in Git.
3. Build managed code, run unit/integration/differential tests and line/branch
   coverage. Set justified thresholds when real implementation exists.
4. Assemble a single nupkg/snupkg pair with all three native RIDs, XML docs,
   README, LICENSE, NOTICE and transitive native license notices. Verify ZIP
   entries, metadata, hashes, assembly TFMs, manifest consistency and symbols.
5. Run fresh package-only consumers on all six RID/TFM combinations. Execute
   NativeAOT/trimmed consumers for both TFMs where toolchains support them;
   missing evidence blocks the corresponding support claim. Run native memory
   ownership tests and adversarial inputs independently of managed coverage.
6. Emit a qualification manifest binding exact package hashes, commit, source
   pins, CI run IDs and results. No package rebuild after this stage.
7. Manual dispatch on main selects that successful CI run and exact SHA. Validate
   repository, branch, run conclusion and artifact digest before obtaining
   credentials. Require owner approval via protected publication environment.
8. Use `NuGet/login@v1` with job-scoped `id-token: write` and the configured NuGet
   account name. Push only the verified nupkg and adjacent matching snupkg using
   its short-lived credential. No long-lived API key, wildcard package selection,
   skip-duplicate masking, PR publication, ordinary-push publication or rebuild.

Prefer manual dispatch over release tags because it names the already qualified
artifact and makes authorization explicit. Use a repository-wide publication
concurrency group with cancellation disabled. Pin action revisions before release
qualification and record runner/toolchain versions in evidence.

## Owner-side configuration (not performed)

Confirm DogwoodSharp ID eligibility/ownership on NuGet; configure its trusted
publisher for this GitHub owner/repository, exact workflow filename and protected
environment. Set the NuGet username referenced by NuGet/login; configure required
reviewers and main-branch restrictions; grant id-token only to the publish job.
Test refusal paths before first release. No credentials, release tags, external
settings or packages were created in this commission.

DogwoodSharp flat-container returned 404 on 2026-11-16; no published conflict was
observed, but availability/reservation is not guaranteed. See [sources](research/sources.md).
