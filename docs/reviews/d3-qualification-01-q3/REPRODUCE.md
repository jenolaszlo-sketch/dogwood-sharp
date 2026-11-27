# Q3 independent reproduction commands

These are the commands used by the independent reviewer. They do not modify
the repository; all work occurs in a detached worktree or in reviewer evidence.

## 1. Resolve the binding identities

```bash
git fetch origin --tags --prune
git rev-parse aa31995
git rev-parse 'aa31995^{tree}'
git rev-parse 12a4555
git rev-parse '12a4555^{tree}'
git merge-base --is-ancestor 12a4555 aa31995
git diff --name-status 12a4555 aa31995
```

Expected: handoff `aa319955082edd3ea0ff1ad4fa04c7676c7bf5ab` / tree
`7871374758c54dcba9a5d95b922042d25743c3a0`; execution
`12a45557eb991482ee4c7b02f26cea13eee1fa4c` / tree
`f58951e4c63935826b9630f69f18d32c02c085f9`; five documentation additions.

## 2. Independent hosted differential run

```bash
gh workflow run 431473878 -f platform=all -f tfm=all
gh run watch <new-run-id> --exit-status
gh run download <new-run-id> -n r5r3-differential-linux-x64-net8.0 -D evidence/hosted/linux-x64-net8.0
# repeat for linux-x64 net10.0, osx-arm64 net8.0 and net10.0
```

The reviewer run used `46027320360`. Verify each
`observations-corrected-reference.json` summary is 124/124, 0 failed, 0
pending, 26 families and 0 coverage errors.

## 3. Independent Windows execution

```bash
git worktree add --detach D:/q3-independent aa31995
# Build the corrected oracle (Rust 1.99.1):
git clone https://github.com/dogwood-policy/dogwood.git D:/q3-work/dogwood-pinned
git -C D:/q3-work/dogwood-pinned checkout 929f49e7305534ac61ef06cf8e47269c596dc851
cd D:/q3-work/dogwood-pinned
patch -p1 < D:/q3-independent/docs/reviews/d3-impl-01-r5-r1/evidence/differential/corrected-oracle/dogwood-typed-context.patch
sha256sum dogwood-language/src/api.rs   # 4ff2eb11...
# Stage the corrected oracle with Cargo.toml pointing at the patched path and build --locked --release.
```

The reviewer used the `scripts/build-oracle.sh` recipe with the path corrected
for the Windows/MSYS path form; the resulting oracle binary was 16,747,008
bytes.

```bash
# Build the differential D2 bridge exactly as scripts/build-bridge.sh does,
# then build the probe:
cd D:/q3-independent
RID=win-x64 ./scripts/stage-assets.sh   # with BRIDGE_PATH/SHA and CEDARSHARP_PATH/SHA
dotnet build docs/reviews/d3-impl-01-r5-r3/evidence/differential/probe/R5R1Probe.csproj -c Release -f net8.0
dotnet build ... -f net10.0
```

Run the differential:

```powershell
PrepProbe.exe differential `
  docs/reviews/d3-impl-01-r5-r3/evidence/fixtures-corrected-reference.json `
  r5r3-work/oracle-target/release/dogwoodsharp-corrected-oracle.exe `
  docs/reviews/d3-impl-01-r5-r3/evidence/register-corrected-reference.json `
  observations-net8.json lifecycle-net8.json r5r3-work/scratch-q3-win8
```

The reviewer also repeated the run with the shipped win-x64 bridge from the CI
artifact `dogwoodsharp-native-win-x64` (78f64c4b) and the published CedarSharp
native (77de6976).

## 4. Harness self-test

```powershell
PrepProbe.exe self-test `
  docs/reviews/d3-impl-01/evidence/differential/r4/upstream-oracle/target/release/dogwoodsharp-upstream-oracle.exe `
  self-test-net8.0.json
```

Expected: 19/19 passed on both TFMs.

## 5. OQ-01 controls

Extract the seven-case fixture and empty register from commit `6dd3053`, then
run the probe in differential mode with the corrected oracle. Expected: 3
agreements and 4 stage-only divergences (TC-03..TC-06), with no decision on the
four divergent controls. The original single-case fixture from HEAD reproduces
D3 Allow versus reference evaluation failure.

## 6. Test suites

```powershell
dotnet test tests/DogwoodSharp.CedarSharp.Tests -c Release -f net8.0
dotnet test tests/DogwoodSharp.CedarSharp.Tests -c Release -f net10.0
dotnet test tests/DogwoodSharp.Tests -c Release -f net8.0
dotnet test tests/DogwoodSharp.Tests -c Release -f net10.0
```

The core suite requires the invalid-ABI asset produced by
`rustc --edition 2024 --crate-type cdylib tests/DogwoodSharp.Tests/invalid_abi.rs`
in the test output's `test-assets` directory. Expected: 96/96 CedarSharp and
71/71 core on each TFM.

## 7. Manifest verification

```bash
node docs/reviews/d3-qualification-01-q3/evidence/manifest-verification.json  # read the recorded result
# Reproduce by hashing each manifest line against git cat-file blob and the working tree.
```
