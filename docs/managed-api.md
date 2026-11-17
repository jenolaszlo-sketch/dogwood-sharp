# D1 managed API

```csharp
using DogwoodSharp;

DogwoodNativeVersion version = Dogwood.GetNativeVersion();
PolicyParseResult result = Dogwood.Parse("permit(principal, action, resource);");
```

Use .NET 8 or .NET 10 and stage the native artifact beside the application using
the [manifest layout](architecture/native-artifacts.md). The API is synchronous
and supports independent concurrent calls. Both results are immutable
snapshots. The version reports ABI, bridge, Dogwood revision and version, Cedar,
Rust and native target.

`Dogwood.Parse(string source)` accepts Unicode source up to
`Dogwood.MaximumSourceBytes` (1 MiB when encoded as UTF-8). A successful result
has `Success == true`, a nonnegative `PolicyCount` and empty diagnostics. A
failed result has `Success == false`, `PolicyCount == null` and an immutable
array of syntax or macro diagnostics in upstream order. Diagnostic offsets and
lengths count UTF-8 bytes, with the [ABI EOF convention](architecture/native-interop.md#operations).
Message text is upstream-dependent and may contain policy text. A successful
parse establishes that Dogwood accepted the source for parsing and macro
expansion. Schema validation and authorization are beyond D1.

Null source raises `ArgumentNullException`. Unpaired surrogates or oversized
source raise `ArgumentException`. `DogwoodBridgeException.Failure` identifies
asset or identity failure, nonzero native call status, and malformed responses.
`NativeStatus` carries the raw status for a failed native call. The process-wide
loader caches its initial load result, including any initial failure. Restart
the process after correcting an asset deployment.

The wrapper uses explicit JSON handling and unmanaged function pointers. The
verification script runs a NativeAOT consumer for each target framework.
