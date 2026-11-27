# DogwoodSharp

An independent .NET library for parsing, validating and evaluating Dogwood
temporal policy on .NET 8 and .NET 10. Licensed under Apache-2.0.

D1 provides policy parsing, macro expansion and structured diagnostics through
the pinned Dogwood parser. D2 adds schema validation, policy lowering and
augmented-schema export. D3 adds an **experimental** CedarSharp evaluation
adapter in a separate package. D1, D2 and D3 are formally closed for the
reconciled subjects recorded in the owner acceptance records.

This repository is prepared for the first public preview release,
**`DogwoodSharp 0.1.0-preview.1`** with the experimental adapter
**`DogwoodSharp.CedarSharp 0.1.0-experimental.1`**. Publication is subject to
separate explicit owner authorization; no package has been published by this
repository preparation.

```csharp
using DogwoodSharp;

var result = Dogwood.Parse("permit(principal, action, resource);");
Console.WriteLine(result.PolicyCount);
```

## Packages

| Package | Version | Status |
| --- | --- | --- |
| `DogwoodSharp` | `0.1.0-preview.1` | Preview. D1/D2 stable surface; D3 export surface is internal. |
| `DogwoodSharp.CedarSharp` | `0.1.0-experimental.1` | Experimental D3 adapter. Exact dependencies on `DogwoodSharp` and `CedarSharp 1.0.0`. |
| `CedarSharp` | `1.0.0` | External dependency, not repacked. |

## Supported matrix

| OS | RID | Frameworks |
| --- | --- | --- |
| Windows Server 2025 | `win-x64` | `net8.0`, `net10.0` |
| Ubuntu 24.04 | `linux-x64` | `net8.0`, `net10.0` |
| macOS 15 (ARM64) | `osx-arm64` | `net8.0`, `net10.0` |

No other platform, RID or framework is claimed. The Windows native image is
bound by the exact release-candidate SHA-256; same-source builds can differ in
embedded PE/debug metadata.

## D3 evaluation adapter

```csharp
using DogwoodSharp;
using DogwoodSharp.Experimental.CedarSharp;

var schema = new DogwoodSchema(
    "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }");

using var authorizer = DogwoodCedarAuthorizer.ForLoweredBundle(
    "permit (principal, action == App::Action::\"read\", resource) when { context.count > 3 };",
    schema);

var result = authorizer.Authorize(new CedarAuthorizationInput(
    new CedarEntityRef("App::User", "alice"),
    new CedarEntityRef("App::Action", "read"),
    new CedarEntityRef("App::Doc", "doc-1"),
    "{ \"count\": 5 }",
    "[]"));

// Only an error-free Allow sets Authorized and IsCleanAllow.
Console.WriteLine(result.Authorized);
```

The adapter is experimental: its API is not a stable public contract and its
presence does not claim upstream Dogwood equivalence. The D3 contract is
schema-carrying and request-validating; it fails closed on lowering, request,
validation, identity, native and transport failures. The recorded difference
register and FCN-05 scope are preserved in the qualification records.

### Known qualifications

- **CedarSharp native provenance.** The published `CedarSharp 1.0.0` package
  and all three native images are hash-verified and the package nuspec commit
  is recoverable. The native manifests declare a `sourceCommit` that is not
  recoverable. This release does not claim source reproducibility of the
  external native; the published package/native hashes are binding.
- **Windows native metadata.** Same-source/lock Windows builds can differ only
  in embedded PE/debug timestamps and the PDB GUID. The exact release-candidate
  hash is the binding identity; normalized equality is not a publication
  substitute.
- **Experimental adapter.** The adapter is not a stable API and is not part of
  the stable `DogwoodSharp` package surface.

## Build and verify

Install the SDK in `global.json`, .NET 8 and 10 runtimes, the Rust toolchain in
`rust-toolchain.toml`, PowerShell 7, and the platform's native C toolchain.
Then run `./eng/Verify.ps1`. This stages and verifies native assets and runs
managed tests and a NativeAOT smoke program for each framework.

The package gate and package-only consumer commands are documented in
[CI and packaging](docs/ci-publishing.md). See the
[managed API contract](docs/managed-api.md) and
[native artifact layout](docs/architecture/native-artifacts.md) for local
consumption.

## Documents

- [D1 implementation report](docs/d1-implementation.md) and [verification](docs/verification.md)
- [D2 acceptance record](docs/d2-acceptance.md) and [D2-QUAL-02](docs/reviews/d2-qualification-02/README.md)
- [D3 closure and release preparation](docs/reviews/d3-close-release-prep-01/README.md)
- [D3-Q3-R1 independent requalification](docs/reviews/d3-qualification-01-q3-r1/README.md)
- [Native ABI](docs/architecture/native-interop.md) and [threat model](docs/architecture/threat-model.md)
- [Accepted ADR](docs/architecture/0001-backend-strategy.md) and [roadmap](docs/roadmap.md)
- [Original commission](docs/commission.md) and [D0 handoff](docs/handoff.md)
