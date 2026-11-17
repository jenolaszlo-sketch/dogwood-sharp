# D1 native artifact manifest v1

Run `./eng/Build-Native.ps1 -RuntimeIdentifier <rid>` in the pinned toolchain on
the corresponding host architecture. The script checks the Cargo graph with
`--locked`, checks the compiler and host, builds the Release cdylib, and stages an
image and `dogwoodsharp-native.json` under `artifacts/native/runtimes/<rid>/native/`.
Keep the pair together and deploy it under `runtimes/<rid>/native/` relative to
the application's base directory. Build outputs are ignored by Git.

| RID | Target | Image |
| --- | --- | --- |
| win-x64 | x86_64-pc-windows-msvc | dogwoodsharp_bridge.dll |
| linux-x64 | x86_64-unknown-linux-gnu | libdogwoodsharp_bridge.so |
| osx-arm64 | aarch64-apple-darwin | libdogwoodsharp_bridge.dylib |

The manifest is a UTF-8 JSON object up to 4096 bytes with the nine native
identity fields below and five manifest-specific fields. All fields are
required and appear once.

| Field | Value |
| --- | --- |
| abiVersion | 1 |
| bridgeVersion | `0.1.0` |
| dogwoodRevision | `929f49e7305534ac61ef06cf8e47269c596dc851` |
| dogwoodVersion | `1.1.2` |
| cedarVersion | `4.11.0` |
| rustVersion | `1.99.1` |
| target | Target triple from the table |
| dogwoodFeatures | Empty array |
| cedarFeatures | `["datetime","decimal","ipaddr"]`, sorted |
| manifestVersion | 1 |
| rid | RID from the table |
| file | Fixed image name for the RID |
| sha256 | 64 hexadecimal characters encoding the image SHA-256 |
| cargoLockSha256 | 64 hexadecimal characters encoding the build's Cargo.lock SHA-256 |

The loader uses the process architecture and operating system to select one
fixed location. It checks the manifest identity and image SHA-256 before
loading the absolute path. It then checks the exported ABI and the nine
identity fields returned by operation 0. The Windows load restricts dependency
lookup to System32. On Linux and macOS the platform loader resolves the image's
system dependencies. The verified image stays loaded for the process lifetime.
Unsupported platforms and absent assets fail explicitly.

SHA-256 detects damage and substitution relative to the manifest. Publisher
authenticity requires a trusted delivery channel and an app directory protected
from untrusted writes. The manifest can be replaced together with the image.
The loader cannot establish trust in a directory that permits asset replacement
between hashing and loading. The lockfile digest is provenance for CI and
artifact reviewers. Runtime validation checks its field format; reviewers can
compare it against the exact source revision used by CI.
