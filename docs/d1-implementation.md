# DOGWOOD-SHARP-D1-IMPL-01 producer report

D0 was accepted with qualifications. The owner accepted ADR-0001 and
commissioned D1. D2 requires independent D1 qualification and owner acceptance.

## Baseline

The clean main checkout at `D:\projects\dogwood-sharp` matched live
`origin/main` in `jenolaszlo-sketch/dogwood-sharp`.

- Commit `43667018738f125848e310e2cdff19ff9a94ed28`
- Tree `2d4fa2c3433637f04fe1e1c4a1a686ebc721d42d`
- Dogwood `929f49e7305534ac61ef06cf8e47269c596dc851`
- Rust 1.99.1 and starting .NET SDK 10.0.401

The implementation adapts the pinned CedarSharp loader, buffer, and staging
approach recorded at D0. Verification extends the existing D0 matrix and uses
per-RID evidence uploads following the CedarSharp and Baize workflows.
See the [pinned reference index](research/sources.md).

## Contracts and verification inventory

- [C ABI v1](architecture/native-interop.md) covers ABI discovery, version and
  parse dispatch, explicit release, bounded UTF-8 JSON and panic containment.
- [Managed API](managed-api.md) covers immutable parse and version snapshots
  and distinct argument, load, protocol and syntax failures.
- [Manifest v1](architecture/native-artifacts.md) covers fixed runtime paths,
  native identity, feature set and SHA-256 verification.
- [Fixture manifest](../tests/DogwoodSharp.Conformance.Tests/parsing-v1.json)
  covers Cedar, temporal and macro syntax, empty and multi-policy input,
  invalid syntax, Unicode, escaping and disabled experimental arithmetic.
  A Rust comparator calls the pinned parser directly, then compares counts,
  diagnostic categories and locations with the ABI. The managed suite consumes
  the same manifest through the staged native image on both frameworks.
- Native tests exercise version, ownership, null pointers as permitted by the
  ABI, UTF-8, envelopes, byte limits and a test-only injected unwind. Managed
  tests exercise repeated concurrent calls, malformed results, missing assets,
  image hash mismatch, manifest identity mismatch and an incompatible test ABI.
- The CI matrix runs locked builds, format and lint gates, native and managed
  tests, and framework-specific JIT and NativeAOT smoke programs. It uploads
  native manifests, binaries, test results and verification logs per RID.

## Dependency graph and scope

The bridge resolves Dogwood 1.1.2 and Cedar policy, core and formatter 4.11.0.
The Dogwood feature set is empty. Cedar policy/core enable `default`, which
expands to datetime, decimal and ipaddr. The formatter has no active features.
MCP schema generator 0.6.1, MCP SDK 0.4.1 and Rhai 1.27.0 remain transitive.
The registry graph differs from the D0 research lock for wasm-only packages
selected during offline resolution: bumpalo 3.20.4 to 3.20.3, js-sys 0.3.107
to 0.3.106 and wasm-bindgen and its macro/support/shared family 0.2.130 to
0.2.129, whose graph adds a dependency edge to the already present once_cell.
The D1 target graphs retain the D0 parser and Cedar versions.
The .NET SDK moved to 10.0.404 to use patched ILCompiler and ILLink packs
(8.0.34 and 10.0.15). The SDK's previous ILCompiler pack versions failed the
NativeAOT restore audit. See [Microsoft's advisory](https://github.com/advisories/GHSA-p3jp-9v83-76w9).

The boundary loads trusted application assets in-process. Native pointer
validity, stack exhaustion or abort, hard resource isolation, and publisher
authenticity remain outside the D1 guarantee. D1 only exposes parsing and
version discovery. See [verification](verification.md) for executed results.

## Independent qualification

Reproduce the native jobs on all three actual runners and both frameworks.
Review C layout and ownership with an independent consumer, including
concurrent calls and failure paths under a native allocator checker. Verify
exact CI manifests and artifact hashes, evaluate parser resource limits with
adversarial inputs, and expand upstream differential coverage around
diagnostic source spans and macro failures. Record independent findings before
requesting owner acceptance.
