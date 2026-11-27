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
| cargoLockSha256 | 64 hexadecimal characters encoding the build's Cargo.lock SHA-256 after canonical LF line endings |

The loader uses the process architecture and operating system to select one
fixed location. It checks the manifest identity and image SHA-256 before
loading the absolute path. It then checks the exported ABI and the nine
identity fields returned by operation 0. The Windows load restricts dependency
lookup to System32. On Linux and macOS the platform loader resolves the image's
system dependencies. One default-context managed binding retains the verified
D2-enabled image for the process lifetime. A failed initialization releases
its loader reference and the binding caches the failure. Unsupported platforms
and absent assets fail explicitly.

SHA-256 detects damage and substitution relative to the manifest. Publisher
authenticity requires a trusted delivery channel and an app directory protected
from untrusted writes. The manifest can be replaced together with the image.
The loader cannot establish trust in a directory that permits asset replacement
between hashing and loading. The lockfile digest is provenance for CI and
artifact reviewers. Runtime validation checks its field format; reviewers can
compare it against the exact source revision used by CI.

## Package layout and qualification gate

The stable `DogwoodSharp` `0.1.0-preview.1` release candidate stores each
native pair at `runtimes/<rid>/native/` in the package:

| Package path | Payload |
| --- | --- |
| `runtimes/win-x64/native/dogwoodsharp_bridge.dll` | Windows x64 image |
| `runtimes/win-x64/native/dogwoodsharp-native.json` | Windows x64 manifest |
| `runtimes/linux-x64/native/libdogwoodsharp_bridge.so` | Linux x64 image |
| `runtimes/linux-x64/native/dogwoodsharp-native.json` | Linux x64 manifest |
| `runtimes/osx-arm64/native/libdogwoodsharp_bridge.dylib` | macOS arm64 image |
| `runtimes/osx-arm64/native/dogwoodsharp-native.json` | macOS arm64 manifest |

NuGet selects the matching runtime-native item and flattens the selected image
and manifest into a consuming application's publish root. The experimental
`DogwoodSharp.CedarSharp` candidate does not carry or repackage these files;
it declares the stable package and `CedarSharp` `1.0.0` as dependencies.

An asset is **stale** when its manifest does not match the exact supported
RID, target triple, pinned Dogwood/Cedar/Rust identities, expected feature
sets, canonical LF-normalized `native/dogwoodsharp-bridge/Cargo.lock` digest,
or the SHA-256 of the adjacent image. Missing files, duplicate/unknown manifest fields, an
unexpected runtime, an extra file in a native directory, or any stale field
fails closed.

From the repository root, with .NET SDK `10.0.404` selected by `global.json`,
validate staged assets without packing:

```powershell
./eng/Test-NativePackageAssets.ps1 -AssetsRoot artifacts/native
```

After the three platform jobs have supplied all three native pairs, create and
strictly inspect local qualification candidates. Use an approved temporary
directory for this local ad hoc output rather than treating it as a repository
artifact:

```powershell
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

The pack command never publishes. The original `CedarSharp` `1.0.0` package is
copied only as an unmodified dependency and is verified against its pinned
package and per-entry hashes; it is never repacked. The hosted equivalents
are documented in [CI, packaging and NuGet publication strategy](../ci-publishing.md).
