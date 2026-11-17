# DogwoodSharp native bridge

The bridge is a Rust cdylib implementing D1 ABI v1 with the pinned Dogwood
parser. Build and stage on the intended host using
`./eng/Build-Native.ps1 -RuntimeIdentifier <rid>` from the repository root.

See the [ABI specification](../docs/architecture/native-interop.md) and
[artifact manifest](../docs/architecture/native-artifacts.md).
