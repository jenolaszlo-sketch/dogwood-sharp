# D1 qualification baseline evidence

Recorded 2026-11-17 by the independent qualification session for
DOGWOOD-SHARP-D1-QUAL-01. This file records the starting state only; results
are in the [qualification report](../README.md).

## Repository baseline

| Item | Value |
| --- | --- |
| Repository | `https://github.com/jenolaszlo-sketch/dogwood-sharp.git` |
| Branch | `main` |
| HEAD commit | `f5b40504c6eaa4efbe61589f2f081be90c768db1` |
| HEAD tree | `2f346ed7165b500ab7dbf8b082cba764f6741616` |
| Implementation commit | `85298534e55ad0a5403277c54872842960a516e0` |
| Implementation tree | `cf15816a398282bf372e83b2250b63ff3d570ab7` |
| Delta `8529853..HEAD` | `docs/verification.md` only (+35 lines) |
| Worktree before qualification | Clean; `origin/main...main` 0/0 |

The post-implementation delta is documentation-only. Its one change records
the producer cross-platform verification; no production source, workflow,
package metadata or ABI file changed between the implementation commit and the
qualification baseline.

## Upstream pins

| Item | Value |
| --- | --- |
| Dogwood revision | `929f49e7305534ac61ef06cf8e47269c596dc851` |
| Dogwood version | 1.1.2 |
| Cedar policy/core/formatter | 4.11.0 |
| Dogwood feature set | none |
| Cedar extensions | `datetime`, `decimal`, `ipaddr` |
| Rust | 1.99.1 |
| Cargo.lock SHA-256 | `2ca6001ce075006aebd71c9b372cec23d374043bcfa0b971fc36a8eac88669e7` |

## Producer CI evidence

Run: <https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44008493648>
(`success`, head `85298534e55ad0a5403277c54872842960a516e0`).

| Job | Runner | Result |
| --- | --- | --- |
| D1 win-x64 / net8.0 and net10.0 | `windows-2025` | success |
| D1 linux-x64 / net8.0 and net10.0 | `ubuntu-24.04` | success |
| D1 osx-arm64 / net8.0 and net10.0 | `macos-15` | success |

Downloaded artifacts were hash-verified against their manifests:

| RID | Target | Image | Image SHA-256 | Manifest |
| --- | --- | --- | --- | --- |
| win-x64 | `x86_64-pc-windows-msvc` | `dogwoodsharp_bridge.dll` | `9d583395c96dc87fbce9f82cc0a7436b66ba0aba94ca5cdf87a013a56e0eb9dc` | [manifest](manifest-win-x64.json) |
| linux-x64 | `x86_64-unknown-linux-gnu` | `libdogwoodsharp_bridge.so` | `564e259bde6c361f88c0fced03e6f0f21c8216c3ce12a10532440b2bb8e8b8a1` | [manifest](manifest-linux-x64.json) |
| osx-arm64 | `aarch64-apple-darwin` | `libdogwoodsharp_bridge.dylib` | `09c7fe872d1afe6d92bd60104e7228be4cfba4223d2c8482ce7c568f5fc96adc` | [manifest](manifest-osx-arm64.json) |

Producer verification logs and managed test results are retained beside this
file:

- [win-x64 verification](verification-win-x64.log)
- [linux-x64 verification](verification-linux-x64.log)
- [osx-arm64 verification](verification-osx-arm64.log)
- [linux-x64 memory ownership report](memory-ownership-linux-x64.log)
- `test-results/<rid>-<tfm>.trx` for all six RID/framework combinations

The producer logs report 13/13 managed tests per framework and RID, JIT and
NativeAOT smoke passes, and Linux Valgrind `ERROR SUMMARY: 0 errors` with
`definitely lost: 0 bytes`. The `48 bytes in 1 blocks are possibly lost`
record is a runtime-loader block, not a bridge allocation.

## Local reproduction environment

| Item | Value |
| --- | --- |
| OS | Windows 10.0.26200 |
| .NET SDK | 10.0.404 |
| .NET runtimes | 8.0.34 and 10.0.15 |
| Rust | 1.99.1 (LLVM 23.1.1), host `x86_64-pc-windows-msvc` |
| MSVC | 14.51.36231 (Visual Studio 18 Build Tools) |
| Python | 3.14.8 |
| PowerShell | 7.7.0 (portable) |

The qualification harness was executed against the downloaded producer
`win-x64` artifact (`artifacts/native` replacement) as well as a local
producer rebuild. Results are in the [report](../README.md).
