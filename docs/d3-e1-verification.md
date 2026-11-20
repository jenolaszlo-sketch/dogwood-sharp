# DOGWOOD-SHARP-D3-E1 verification record

Status: **producer verification executed on Windows `win-x64`. Independent
qualification has not been performed.**

This record binds the D3-E1 evidence to the exact source revision, host,
toolchain and commands. It reports only what was executed. Linux, macOS,
concurrency, performance and upstream-authorizer equivalence are not claimed.

## 1. Baseline

| Item | Identity |
| --- | --- |
| Starting `main` | `28073db77f355ef81d8689354181b285fe9bdb64` |
| Starting tree | `f385b8e880d0bf0be8e406af0a8a2d43a7f02be8` |
| Working branch | `main` |
| Worktree state at start | Clean, synced with live `origin/main` |
| D1 qualification tree | `4cd202db3b567c9893b00ba947acd802fe362456` |
| D2 qualification tree | `74870265e31fbe7f5633962174c227159b3ddc87` |

The delivery commit and tree are recorded in the handoff response and can be
re-derived with `git rev-parse HEAD` and `git rev-parse 'HEAD^{tree}'`. Source
files in `native/dogwoodsharp-e1-ref/`, `research/CedarSharp.E1Prototype/` and
`research/CedarSharp.E1AotConsumer/` are research-only and outside
`DogwoodSharp.slnx`.

## 2. Host and toolchain

| Item | Observed |
| --- | --- |
| OS | Microsoft Windows 11 Home Single Language, version 10.0.26200, 64-bit |
| CPU | Intel Core Ultra 5 125H, 14 cores / 18 logical processors |
| .NET SDK | 10.0.404 at `%LOCALAPPDATA%\Microsoft\dotnet` |
| .NET runtimes | Microsoft.NETCore.App 8.0.31 and 10.0.15 at the same root |
| Rust | rustc 1.99.1 (58cf8f9c3 2026-10-13), cargo 1.99.1 (5f94df478 2026-08-27) |
| Rust target | `x86_64-pc-windows-msvc` |
| PowerShell | 7.7.0 |
| Git | 2.55.0.windows.3 |
| RID / TFM | `win-x64`; `net8.0` and `net10.0` |

The repository `global.json` pins SDK `10.0.404` with `rollForward: disable`.
The .NET 8 runtime was installed alongside the SDK's runtime because the host
did not initially provide it; both runtimes report from the same root above.

## 3. Artifact identity

| Artifact | SHA-256 |
| --- | --- |
| D2 Dogwood bridge `artifacts/native/runtimes/win-x64/native/dogwoodsharp_bridge.dll` | `8ABF40D2526EAB650003E7EE1C05D97AA82CBEB69890849E867333EA54914D63` |
| CedarSharp native `cedarsharp_native.dll` | `77DE6976A7AE5B961920BB6843D39B32F3FAF285F16B723581680E9C06C03895` |
| Cedar 4.11 reference `native/dogwoodsharp-e1-ref/target/release/dogwoodsharp-e1-ref.exe` | `D617B02A12C511A67ADBB03F1F0388D19926664D5C4EAF916500AFF431E7EBB8` |

The CedarSharp hash matches the packaged `cedarsharp-native.json` manifest. The
bridge manifest reports Dogwood 1.1.2, revision
`929f49e7305534ac61ef06cf8e47269c596dc851`, Cedar 4.11.0, ABI 1, Rust 1.99.1,
`x86_64-pc-windows-msvc`, features `datetime`, `decimal`, `ipaddr`. The
CedarSharp manifest reports SDK 4.13.0, language version 4.5, ABI 1, source
commit `324d3c09fc94ab91464ec340eb81e4b167deb6f5`, the same features. The
reference binary reports SDK 4.11.0 and language version 4.5.

## 4. Executed checks

All commands ran from `D:\projects\dogwood-sharp` with the .NET and Cargo paths
on `PATH`. Release configuration was used throughout.

### 4.1 Reference oracle

```powershell
cargo fmt --manifest-path native\dogwoodsharp-e1-ref\Cargo.toml --check
cargo clippy --locked --release --manifest-path native\dogwoodsharp-e1-ref\Cargo.toml --all-targets -- -D warnings
cargo build --locked --release --manifest-path native\dogwoodsharp-e1-ref\Cargo.toml
```

Observed: exit 0 for all three. Clippy reported no warnings. The crate pins
`cedar-policy = "=4.11.0"` with `default-features = false` and features
`ipaddr`, `decimal`, `datetime`.

### 4.2 Prototype restore, build and format

```powershell
dotnet restore research\CedarSharp.E1Prototype\CedarSharp.E1Prototype.csproj --locked-mode
dotnet build research\CedarSharp.E1Prototype\CedarSharp.E1Prototype.csproj -c Release --no-restore
dotnet format research\CedarSharp.E1Prototype\CedarSharp.E1Prototype.csproj --verify-no-changes --no-restore
```

Observed: locked restore and build succeeded with 0 warnings and 0 errors on
both TFMs. The final format check passed. An earlier format run found
whitespace in `Cedar11Reference.cs`; it was corrected before the final check.
The prototype is not in `DogwoodSharp.slnx`, so this is a project-scoped format
check, not the repository-wide `dotnet format` gate.

### 4.3 Prototype tests

```powershell
dotnet test research\CedarSharp.E1Prototype\CedarSharp.E1Prototype.csproj -c Release -f net8.0 --no-build --logger "console;verbosity=detailed"
dotnet test research\CedarSharp.E1Prototype\CedarSharp.E1Prototype.csproj -c Release -f net10.0 --no-build --logger "console;verbosity=detailed"
```

Observed:

| TFM | Total | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| `net8.0` | 34 | 34 | 0 | 0 |
| `net10.0` | 34 | 34 | 0 | 0 |

The 34 tests per TFM are distributed as follows:

| Test class | Tests | Coverage |
| --- | ---: | --- |
| `DifferentialAuthorizationTests` | 3 | 30-case differential; raw-Allow-with-errors; errored-forbid |
| `E1EndToEndTests` | 3 | version pair; full lowering-to-decision flow; artifact round trip |
| `EntityModelDifferenceTests` | 3 | supplied incomplete entity; absent versus attribute-less scope entity; identity without presence |
| `EntityRequestExportTests` | 6 | action entities; schema-only hierarchy; typed entity API; parents; extensions; bare-scope modes |
| `NativeCoexistenceTests` | 2 | both native images resident; both versions serve in one process |
| `NegativeControlTests` | 7 | pre-export rejection; optional-attribute guard; syntax error; malformed body; invalid set with matching permit; splitter controls |
| `PolicyIdPreservationTests` | 5 | `FromPolicies`; rule mapping; `FromText` renumbering; `@id` annotation; default distincter |
| `SchemaRoundTripTests` | 3 | both schema forms on all 30 cases; reference parse; entity round trip |
| `TemporalBoundaryTests` | 2 | generated field; true/false wiring against both engines |

### 4.4 Differential corpus result

All 30 corpus cases agreed across CedarSharp 4.13.0 and the Cedar 4.11.0
reference on: success/operation-failure, decision, determining ID set,
per-policy error counts keyed by policy ID, operation-error presence and warning
counts. Validation outcome and entity-parse outcome also agreed for every case.

Observed aggregate:

- 30/30 cases agreed.
- 24 cases had hand-authored decisions and all matched.
- 6 cases have no hand-authored decision: three `E1Case.Evidence` observations
  and three constructor cases whose observed operation failure or entity mode
  is recorded. Both engines agreed on each.
- One validation warning in total: `escaped-ids`, identical on both schema
  forms and both engines.
- Three operation failures: `validate-request-true-rejects`,
  `context-missing-required-attribute-fails`, `supplied-incomplete-entity`.
- Two raw Allows with policy errors (`forbid-error-skipped`,
  `allow-with-policy-errors`) were observed as not clean.

Representative exact output lines from the differential test:

```text
permit-direct: managed=allow reasons=[e1_0] errors=[] operationErrors=[] reference=allow reasons=[e1_0] errors=[] operationErrors=[]
permit-forbid: managed=deny reasons=[e1_1] errors=[] operationErrors=[] reference=deny reasons=[e1_1] errors=[] operationErrors=[]
forbid-error-skipped: managed=allow reasons=[e1_1] errors=[e1_0:entity `App::Session::"s1"` does not exist] operationErrors=[] reference=allow reasons=[e1_1] errors=[e1_0:entity `App::Session::"s1"` does not exist] operationErrors=[]
validate-request-true-rejects: managed=operation-failure reasons=[] errors=[] operationErrors=[principal type `App::Doc` is not valid for `App::Action::"read"`] reference=operation-failure reasons=[] errors=[] operationErrors=[principal type `App::Doc` is not valid for `App::Action::"read"`]
scope-entity-present-missing-attribute: managed=deny reasons=[] errors=[e1_0:`App::Session::"s1"` does not have the attribute `token`] operationErrors=[] reference=deny reasons=[] errors=[e1_0:`App::Session::"s1"` does not have the attribute `token`] operationErrors=[]
```

The full 30-case table is in [d3-e1-compatibility.md](d3-e1-compatibility.md#6-e1-4-differential-corpus).

### 4.5 NativeAOT consumer

```powershell
dotnet publish research\CedarSharp.E1AotConsumer\CedarSharp.E1AotConsumer.csproj -c Release -r win-x64 -f net8.0 -o <publish-net8>
dotnet publish research\CedarSharp.E1AotConsumer\CedarSharp.E1AotConsumer.csproj -c Release -r win-x64 -f net10.0 -o <publish-net10>
# Stage the D2 bridge asset produced by the repository gate into each publish directory:
#   <publish>\runtimes\win-x64\native\dogwoodsharp_bridge.dll
#   <publish>\runtimes\win-x64\native\dogwoodsharp-native.json
<publish-net8>\CedarSharp.E1AotConsumer.exe
<publish-net10>\CedarSharp.E1AotConsumer.exe
```

Observed for both TFMs, exit 0:

```text
lowered rule id = aot_0
cedarsharp decision = Allow
determining policies = [aot_0]
AOT consumer OK
```

Both publishes completed with exit 0. The consumer is fully trimmed
(`PublishAot`, `TrimMode=full`, `IsTrimmable`) and uses only JSON/typed inputs.
The D2 bridge is a project reference; its native asset is not copied by the
library project, so the verification stages the already-built manifest and
binary. Without that staging the consumer fails closed with
`DogwoodBridgeException` because the manifest is missing. This is expected for
the research consumer and is not a package-only consumer qualification.

### 4.6 Repository regression gate

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File eng\Verify.ps1 -RuntimeIdentifier win-x64
```

Observed: exit 0. The gate re-built the D2 native bridge and manifest, ran
`cargo fmt --check`, clippy and 13 Rust tests, performed locked restore/build of
`DogwoodSharp.slnx`, ran repository-wide `dotnet format --verify-no-changes`,
and ran the D2 differential (39 cases, 0 failures, `RESULT PASS`). It then ran
69 managed tests per TFM with zero failures or skips, the JIT foundation smoke,
and a NativeAOT publish and execution per TFM. Both AOT executions printed
`DogwoodSharp bridge 0.1.0, Dogwood 1.1.2, Cedar 4.11.0,
x86_64-pc-windows-msvc: passed.`

The gate does not include the D3-E1 research projects because they are outside
`DogwoodSharp.slnx`. Sections 4.2 to 4.5 cover them separately. The gate also
does not execute Linux or macOS cells.

The final worktree contained only the D3-E1 source and documentation additions;
the generated lockfile sections described in section 5 were absent after the
gate.

## 5. Cleanup and worktree state

- The 30-case corpus writes one temporary JSON input per reference call under
  the system temp directory and deletes it in a `finally` block.
- Test `bin/`, `obj/`, the reference `target/`, and the AOT publish directory are
  ignored by `.gitignore`. No native binary, log or generated file is part of
  the delivery.
- A RID-specific restore/publish can append empty `net8.0/win-x64` and
  `net10.0/win-x64` sections to `src/DogwoodSharp/packages.lock.json`. This is a
  generated lockfile change; it was reverted and is not part of the delivery.
  The same behavior is documented in the D2 R2 verification procedure.
- No CedarSharp repository, package cache or lock was modified.
- No existing D1/D2 evidence, workflow, owner record or version pin was
  modified.

## 6. Explicit exclusions

The following are **not** qualified by this record:

- Linux (`linux-x64`) and macOS (`osx-arm64`) execution.
- Independent qualification or owner acceptance of D3.
- Differential against the upstream Dogwood authorizer. E1 compares CedarSharp
  with the pinned Cedar FFI on the same envelopes; entity-store equivalence
  with Dogwood's authorizer remains open.
- Full Cedar language or SDK parity between 4.11.0 and 4.13.0.
- Concurrency, throughput, latency, memory or allocation behavior.
- Production package, trimming of arbitrary CedarSharp object overloads, or
  package-only consumer behavior.
- Temporal evaluation, providers, event storage, replay or recovery.
- Any production API, compatibility promise or release artifact.

These remain pending. A green producer suite is not qualification and is not
owner acceptance.
