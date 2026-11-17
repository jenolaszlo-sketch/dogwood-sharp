# Qualification CI evidence index

These files are the downloaded artifacts of qualification CI run
[44145288376](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44145288376)
(`success` on all three runtime identifiers; head `def4654be755e53c48f729b00b9dbe4c4c7f36c7`).

```text
qualification-ci/
  run.json                                  run metadata and per-job timing
  <rid>/qualification.json                  harness summary written by run-all.ps1
  <rid>/producer-verification.log           eng/Verify.ps1 reproduction for this RID
  <rid>/logs/                               per-step harness logs
  <rid>/reports/                            harness JSON and text reports
```

| RID | Runner | Job | Producer verify | Selftest | Differential | Managed net8.0 | Managed net10.0 | AOT net8.0 | AOT net10.0 | Valgrind |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| win-x64 | `windows-2025` | success | pass | 83/83 | 73/130, 0 failures | 114/114 | 114/114 | pass | pass | n/a |
| linux-x64 | `ubuntu-24.04` | success | pass | 83/83 | 73/130, 0 failures | 114/114 | 114/114 | pass | pass | 0 errors, 0 leaks |
| osx-arm64 | `macos-15` | success | pass | 83/83 | 73/130, 0 failures | 114/114 | 114/114 | pass | pass | n/a |

Runner environments recorded in the producer logs:

| RID | OS image | Rust | .NET SDK |
| --- | --- | --- | --- |
| win-x64 | `windows-2025`, `OS Version: 10.0.26100`; local image hash `bb14a4fa1f71ac5706a362424c33091b674133020be790734f1b0e9834033895` | 1.99.1 `(58cf8f9c3 2026-10-13)`, host `x86_64-pc-windows-msvc`, LLVM 23.1.1 | 10.0.404 |
| linux-x64 | `ubuntu-24.04`; image hash `564e259bde6c361f88c0fced03e6f0f21c8216c3ce12a10532440b2bb8e8b8a1` matches the producer artifact | 1.99.1 `(58cf8f9c3 2026-10-13)`, host `x86_64-unknown-linux-gnu`, LLVM 23.1.1 | 10.0.404 |
| osx-arm64 | `macos-15`, `OS Version: 15.8`; image hash `09c7fe872d1afe6d92bd60104e7228be4cfba4223d2c8482ce7c568f5fc96adc` matches the producer artifact | 1.99.1 `(58cf8f9c3 2026-10-13)`, host `aarch64-apple-darwin`, LLVM 23.1.1 | 10.0.404 |

Notes:

- The Windows qualification job rebuilds the native image inside `eng/Verify.ps1`,
  so the `win-x64` image hash in `qualification.json` is a fresh build hash
  rather than the producer artifact hash (Rust Release builds are not
  byte-reproducible). The Linux and macOS qualification jobs reuse the images
  built by the producer verification step in the same job, and their hashes
  match the downloaded producer artifacts exactly.
- The Valgrind run in this workflow is the independent C ABI consumer
  (`abi_probe selftest`) on the real Linux bridge, in addition to the producer's
  Rust-suite Valgrind step retained in [the producer evidence](../producer-ci).