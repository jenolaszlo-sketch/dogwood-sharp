# D3 package-only qualification consumer

This disposable consumer is intentionally outside `DogwoodSharp.slnx`. Its only
package reference is the exact range
`DogwoodSharp.CedarSharp [0.0.0-experimental.1]`; the stable
`DogwoodSharp 0.0.0-discovery.1` and `CedarSharp 1.0.0` packages are resolved
transitively.

The consumer targets `net8.0` and `net10.0`, enables full trimming and
NativeAOT analyzers, and uses `Utf8JsonWriter`/`JsonDocument` rather than
reflection-based JSON serialization. It exercises:

1. native identity verification;
2. lower/export;
3. clean-Allow authorization and ordinary-Deny controls;
4. generated policy-identity preservation;
5. the trusted temporal-runtime path and its fail-closed ordinary path; and
6. fail-closed out-of-scope and malformed-JSON controls.

`run.ps1` restores from an uploaded `.nupkg` feed into an isolated NuGet cache,
uses disposable per-cell lockfiles, runs JIT and NativeAOT, and verifies the
published native files and hashes. It never uses a project reference, source
tree fallback, native environment override, or manual native staging.

The consumer is producer qualification evidence only. It is not independent
qualification, owner acceptance, publication authorization, or a release gate.
