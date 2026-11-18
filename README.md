# DogwoodSharp

An independent .NET library for parsing Dogwood temporal policy source on
.NET 8 and .NET 10. Licensed under Apache-2.0.

D1 provides policy parsing, macro expansion, structured diagnostics and native
version discovery through the pinned Dogwood parser. D2 adds schema validation
and policy lowering and remains under remediation pending independent
requalification and separate owner acceptance.

```csharp
using DogwoodSharp;

var result = Dogwood.Parse("permit(principal, action, resource);");
Console.WriteLine(result.PolicyCount);
```

## Build and verify

Install the SDK in `global.json`, .NET 8 and 10 runtimes, the Rust toolchain
in `rust-toolchain.toml`, PowerShell 7, and the platform's native C toolchain.
Then run `./eng/Verify.ps1`. This stages and verifies native assets and runs
managed tests and a NativeAOT smoke program for each framework.

See the [managed API contract](docs/managed-api.md) and
[native artifact layout](docs/architecture/native-artifacts.md) to stage an
asset for a local consuming application. Distribution and publishing require
a later release commission.

## Documents

- [D1 implementation report](docs/d1-implementation.md) and [verification](docs/verification.md)
- [Native ABI](docs/architecture/native-interop.md) and [threat model](docs/architecture/threat-model.md)
- [Accepted ADR](docs/architecture/0001-backend-strategy.md) and [roadmap](docs/roadmap.md)
- [Implementation plan](docs/implementation-plan.md) and [qualification](docs/qualification.md)
- [Discovery](docs/research/discovery.md), [source pins](docs/research/sources.md),
  [CedarSharp compatibility](docs/research/cedarsharp-compatibility.md) and [spike evidence](docs/research/feasibility-spike.md)
- [Original commission](docs/commission.md) and [D0 handoff](docs/handoff.md)
