# DOGWOOD-SHARP-D1-QUAL-01 independent qualification

**Qualification outcome: QUALIFIED — no blocking findings.**
Owner acceptance remains pending; this report does not accept D1 on the owner's
behalf and does not authorize D2.

| Item | Value |
| --- | --- |
| Qualification date | 2026-11-17 |
| Qualifications baseline | `f5b40504c6eaa4efbe61589f2f081be90c768db1` (tree `2f346ed7165b500ab7dbf8b082cba764f6741616`) |
| D1 implementation | `85298534e55ad0a5403277c54872842960a516e0` (tree `cf15816a398282bf372e83b2250b63ff3d570ab7`) |
| Upstream pin | Dogwood `929f49e7305534ac61ef06cf8e47269c596dc851` (1.1.2), Cedar 4.11.0 |
| Independent harness commit | `def4654` (final harness state) |
| Qualification CI run | <https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44145288376> (`success` on `win-x64`, `linux-x64` and `osx-arm64`) |
| Producer CI run | <https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44008493648> |
| Evidence root | [evidence](evidence/baseline.md) |

## 1. Scope and independence

This qualification was run in a fresh session from the D1 implementation
session. It verifies the contract in
[the C ABI](../architecture/native-interop.md),
[the managed API](../managed-api.md),
[the artifact manifest](../architecture/native-artifacts.md) and the D1
qualification expectations. It does not add features, fix production code,
change the ABI, publish packages or begin D2.

The qualifier added only:

- `docs/reviews/d1-qualification/**` — this report, the independent harness and
  the evidence record;
- `.github/workflows/d1-qualification.yml` — a dispatch-only, read-only
  qualification workflow.

No production source, native crate, managed test, sample, build script, package
metadata, existing workflow or release setting was modified. The harness never
links or imports the bridge implementation: it loads the shipped image through
the documented C ABI, builds a separate oracle directly from the pinned upstream
revision, and exercises the managed package as an ordinary consumer.

## 2. Baseline and producer evidence

The baseline, upstream pins, producer jobs, artifact hashes and local toolchain
are recorded in [baseline.md](evidence/baseline.md). In summary:

- HEAD `f5b4050` and implementation `8529853` are the commissioned commits; the
  delta `8529853..HEAD` is `docs/verification.md` only (+35 lines).
- The worktree was clean and synchronized with `origin/main` before
  qualification.
- Producer run `44008493648` succeeded on all three RIDs (`windows-2025`,
  `ubuntu-24.04`, `macos-15`); downloaded images match their manifests:
  - `win-x64` `9d583395c96dc87fbce9f82cc0a7436b66ba0aba94ca5cdf87a013a56e0eb9dc`
  - `linux-x64` `564e259bde6c361f88c0fced03e6f0f21c8216c3ce12a10532440b2bb8e8b8a1`
  - `osx-arm64` `09c7fe872d1afe6d92bd60104e7228be4cfba4223d2c8482ce7c568f5fc96adc`
  - every `cargoLockSha256` is
    `2ca6001ce075006aebd71c9b372cec23d374043bcfa0b971fc36a8eac88669e7`,
    matching the checked-in `Cargo.lock`.
- Producer logs report 13/13 managed tests per framework and RID, JIT and
  NativeAOT smoke passes, and Linux Valgrind `ERROR SUMMARY: 0 errors` with
  `definitely lost: 0 bytes`.

## 3. Method

The independent harness lives in [harness](harness) and is orchestrated by
[run-all.ps1](harness/run-all.ps1). It performs:

1. Manifest and image hash verification independent of the managed loader.
2. A C11 ABI consumer built against the documented header layout only. It
   checks symbol/layout/identity contracts, status precedence, envelopes,
   UTF-8, diagnostic locations, limits, ownership, repeated lifecycle,
   concurrent calls and a real response-ceiling input.
3. A wire-protocol corpus executed through the C consumer: malformed JSON,
   duplicate/unknown fields, lone surrogates, raw invalid UTF-8, whitespace,
   BOMs, deep unknown nesting, operation precedence and byte-boundary probes.
4. An independent Rust oracle built from the pinned upstream revision. It
   re-derives expected policy counts, diagnostic categories/locations and the
   projected operation-1 reply size; the comparator requires status, counts,
   categories and locations to match and records message text as informational.
5. A managed consumer matrix on `net8.0` and `net10.0` against real and
   controlled-fake assets: public API surface, immutable snapshots, argument
   checks, repeated/concurrent calls, loader discovery, manifest field and hash
   mutation, image mutation, protocol mutation and status mapping.
6. JIT and trimmed NativeAOT smoke executables on both frameworks.
7. On Linux, the independent consumer under Valgrind.

The same harness runs on every RID in the qualification workflow after
`eng/Verify.ps1` reproduces the producer gates for that RID.

## 4. Verification matrix

Every gate below was executed, not skipped. The same results appear in the
Windows reproduction in `evidence/local-win-x64`. Rust Release builds are not
byte-reproducible, so a locally rebuilt Windows image has a different SHA-256
from the producer artifact; every gate is functional (ABI, differential,
limits, loader) rather than hash-based, and the Linux and macOS images reused
from the producer step do match the producer artifact hashes byte for byte.

### 4.1 Producer gate reproduction (`eng/Verify.ps1` and `.github/workflows/ci.yml` equivalents)

| Gate | Result | Evidence |
| --- | --- | --- |
| `cargo fmt --check` | Pass on all 3 RIDs | `evidence/qualification-ci/<rid>/producer-verification.log` |
| `cargo clippy --locked --release --all-targets -- -D warnings` | Pass on all 3 RIDs | same log |
| `cargo test --locked --release` | 5 passed, 0 failed on all 3 RIDs | same log |
| `dotnet restore --locked-mode` + Release build, `TreatWarningsAsErrors` | 0 warnings, 0 errors on all 3 RIDs | same log |
| `dotnet format --verify-no-changes` | Pass on all 3 RIDs | same log |
| Managed suite, 13 tests | 13/13 `net8.0` and 13/13 `net10.0` on every RID; producer TRX agrees | `evidence/qualification-ci/<rid>/producer-verification.log`, `evidence/producer-ci/test-results/*.trx` |
| 15-fixture differential (`tests/DogwoodSharp.Conformance.Tests/parsing-v1.json`) | Pass; fixture diagnostics printed and matched | producer log, `tests/DogwoodSharp.Conformance.Tests/parsing-v1.json` |
| Manifest verification (identity, SHA-256 image and `Cargo.lock`) | Pass on all 3 RIDs | `evidence/qualification-ci/<rid>/reports/<rid>/manifest-verification.json` |
| JIT smoke (producer `dotnet run`) | Pass both TFMs on all 3 RIDs | producer log |
| NativeAOT smoke (producer `dotnet publish` + executable) | Pass both TFMs on all 3 RIDs | producer log |

### 4.2 Independent harness (`docs/reviews/d1-qualification/harness`)

| Gate | win-x64 | linux-x64 | osx-arm64 | Evidence |
| --- | --- | --- | --- | --- |
| C ABI consumer selftest (layout, symbols, identity, rejections, parse results, limits, ownership, concurrency) | 83/83 | 83/83 | 83/83 | `evidence/qualification-ci/<rid>/logs/native-selftest.log` |
| Independent Valgrind memory check of the C consumer | n/a (Windows) | 0 errors; definitely/indirectly/possibly lost `0 bytes` | n/a (macOS) | `.../logs/native-selftest-valgrind.log`, `.../reports/<rid>/valgrind-summary.json` |
| Fake native library protocol experiments (controllable version/parse/status/image and manifest variants) | 23 fake-version, 36 fake-parse, 20 status-mapping, 29 manifest and 3 image scenarios | identical | identical | managed reports; on Windows also `.../logs/build-fake-*.log` |
| Upstream differential: 73 independent sources / 130 wire requests | 0 failures, 0 message mismatches, 0 wire-length mismatches; response ceiling exercised | identical | identical | `.../reports/<rid>/differential/differential-report.json` |
| Status distribution observed across the corpus | `{"0":89,"1":1,"3":2,"4":2,"5":30,"6":6}` | identical | identical | same report |
| Resource-limit boundaries | depth 31 accepted / 32+ rejected; lists 31/32; records 33 rejected; macro budget level 16 accepted / 18 rejected; 1 MiB source and request ceilings exact; response ceiling returns status 6 with an empty slot | identical | identical | same report, `resourceTransitions` |
| Scope containment (parser only, no lowering or evaluation) | `scope-unknown-entity`, `scope-unknown-attribute`, `scope-type-mismatch` and `scope-action-group` parse like the pinned upstream parser; `scope-unknown-function` fails with the upstream `macro` diagnostic | identical | identical | same report, `resourceTransitions.scope` |
| Managed API/loader/artifact-integrity matrix | 114/114 `net8.0`, 114/114 `net10.0` | identical | identical | `.../reports/<rid>/managed-<tfm>/managed-report.json` |
| Managed public-surface scope assertion | included in the 114 scenarios on both TFMs | identical | identical | same report |
| Independent NativeAOT smoke (own project, not the producer sample) | `AOT SMOKE PASS` both TFMs | identical | identical | `.../logs/aot-smoke-<tfm>.log` |

### 4.3 Scope containment and publication

| Gate | Result | Evidence |
| --- | --- | --- |
| No production source, native crate, test, sample, build script, package metadata or existing workflow modified | Only `docs/reviews/d1-qualification/**` and `.github/workflows/d1-qualification.yml` added | `git diff --name-only f5b40504c6eaa4efbe61589f2f081be90c768db1..def4654be755e53c48f729b00b9dbe4c4c7f36c7` |
| Public managed surface is exactly the D1 API (eight types, `Dogwood` with `GetNativeVersion`/`MaximumSourceBytes`/`Parse`, immutable sealed snapshots) | Asserted on both TFMs on all three RIDs | managed matrix scenario `positive` |
| D1 stays a parser: unknown entities/attributes and type-mismatched expressions parse like the pinned upstream parser; unknown functions fail exactly as upstream does | 5 differential scope cases matching the oracle on all three RIDs | differential report, `resourceTransitions.scope` |
| Publication blocked | `.github/workflows/publish.yml` is `workflow_dispatch` only, `permissions: contents: read`, and its single step refuses and exits 1. No tags or releases exist. `IsPackable` is false unless `DogwoodSharpFoundationPack=true`, which no workflow sets. | `.github/workflows/publish.yml`, `src/DogwoodSharp/DogwoodSharp.csproj`, `Directory.Build.props`, `git tag`, `gh release list` |
| Qualification CI cannot publish or push | `permissions: contents: read`; only `actions/upload-artifact`; no `workflow_dispatch` credentials and no push trigger | `.github/workflows/d1-qualification.yml` |

## 5. Findings

No blocking or major findings were identified. The following observations are
informational and are consistent with the documented contract.

| ID | Severity | Observation | Disposition |
| --- | --- | --- | --- |
| QF-01 | Informational | A 1 MiB malformed string token containing control characters or backslashes is echoed whole in the diagnostic message. JSON escaping can expand that reply past the 2 MiB response ceiling, so the bridge returns status 6 and an empty output slot instead of partial diagnostics. | Documented behaviour; exercised by both the C probe and the differential. No D1 change required. |
| QF-02 | Informational | Diagnostic message text is produced by the pinned upstream parser and can change with upstream revisions; only non-emptiness is asserted. Counts, categories and locations are compared exactly. | Expected; message text is documented as unstable. |
| QF-03 | Informational | The producer Valgrind run on `linux-x64` reports one 48-byte `possibly lost` block from the runtime loader, with `0 errors`, `definitely lost: 0` and `indirectly lost: 0`. | Not attributable to the bridge; no leak or invalid access in bridge allocations. |

## 6. Residual limitations

These are boundaries of the D1 guarantee and of this qualification, not
unresolved failures:

- Assets are trusted application files loaded in-process. Native pointer
  validity, stack exhaustion/abort, hard resource isolation and publisher
  authenticity remain outside the D1 boundary.
- The differential is evidence over the executed corpus, not a proof over all
  possible policy text.
- Valgrind is available only on Linux. Windows and macOS rely on the ABI
  ownership tests, repeated lifecycle checks and OS loader behaviour.
- No fuzzing campaign or sanitizer run was part of D1; those belong to later
  hardening commissions.
- Diagnostic messages are not a stable interface.

## 7. Disposition

**DOGWOOD-SHARP-D1-IMPL-01 is QUALIFIED.**

- All producer gates were reproduced and pass on the three required runners
  (`windows-2025`, `ubuntu-24.04`, `macos-15`) for both `net8.0` and `net10.0`.
- All independent harnesses pass on the same matrix: 83 C ABI checks per RID,
  0-error Valgrind on Linux, 73/130 differential cases with exact status,
  count, category and location agreement against the pinned upstream parser,
  the documented resource limits (nesting 32, node budget 1,000,000, 1 MiB
  source, request and 2 MiB response ceilings), 114 managed scenarios on both
  TFMs per RID, and independent NativeAOT smoke passes on both TFMs per RID.
- No blocking, major or minor findings. Three informational observations
  (QF-01, QF-02, QF-03) are recorded above; none contradicts the contracts.

What this report does **not** do, per the commission:

- It does not accept D1 for the owner. Owner acceptance remains pending.
- It does not authorize or begin D2. D2 remains gated on owner acceptance.
- It does not publish packages. Publication remains blocked.
- It does not remediate production code. No production file changed.

Recommended owner decision: accept D1 for parsing and version discovery on
`win-x64`, `linux-x64` and `osx-arm64` under `net8.0` and `net10.0`. The
provenance caveat in
[the native artifact manifest](../architecture/native-artifacts.md) (publisher
authenticity is not established) should stay in owner decisions that consume the
shipped images.

## 8. Reproduction

Prerequisites: the pinned Rust 1.99.1 toolchain, .NET SDK 10.0.404 with 8.0.34
and 10.0.15 runtimes, a C11 compiler, Python 3, and (on Windows) an MSVC
developer environment. From the repository root:

```powershell
# Optional producer reproduction for the current RID
./eng/Verify.ps1 -RuntimeIdentifier win-x64

# Independent qualification against a producer artifact
./docs/reviews/d1-qualification/harness/run-all.ps1 `
  -RuntimeIdentifier win-x64 `
  -AssetsRoot artifacts/native `
  -SkipNativeBuild

# Or dispatch the isolated three-RID workflow and download each artifact
gh workflow run d1-qualification.yml --ref main
```

`run-all.ps1` writes ignored outputs under `artifacts/d1-qualification`, then
fails if any tracked production file changed during the run. The qualification
workflow has `contents: read` and never packs, signs or publishes.
