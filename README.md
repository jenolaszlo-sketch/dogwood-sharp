# DogwoodSharp

An independent, local-first .NET library project for the Dogwood temporal policy
language. Target frameworks: .NET 8 and .NET 10. License: Apache-2.0.

**Status: discovery foundation (D0), awaiting architecture acceptance.**
There is no implemented policy evaluation API and no release package. The
upstream Dogwood interpreter is a semantic reference, not a production security
engine. No AWS service or Penghou infrastructure dependency is introduced.

## Build

Install the SDK in `global.json`, .NET 8 and 10 runtimes, and Rust from
`rust-toolchain.toml`, then run:

```powershell
./eng/Verify.ps1
```

The managed smoke tests verify assembly loading and the discovery boundary.
The Rust scaffold has no authorization operations. The optional research spike
uses actual upstream Dogwood and published CedarSharp; see its evidence below.
Build artifacts and research output are ignored by Git.

## Documents

- [Original commission](docs/commission.md)
- [Discovery and source revisions](docs/research/discovery.md)
- [Capability inventory](docs/research/capabilities.md)
- [Architecture options](docs/architecture/options.md) and [proposed ADR](docs/architecture/0001-backend-strategy.md)
- [CedarSharp compatibility](docs/research/cedarsharp-compatibility.md) and [spike evidence](docs/research/feasibility-spike.md)
- [Threat model](docs/architecture/threat-model.md) and [native boundary](docs/architecture/native-interop.md)
- [CI and publication](docs/ci-publishing.md)
- [Implementation plan](docs/implementation-plan.md), [roadmap](docs/roadmap.md), [qualification](docs/qualification.md)
- [Executed verification](docs/verification.md) and [handoff](docs/handoff.md)

Publishing is deliberately blocked at D0. Platform support is a qualification
target until the corresponding CI evidence exists. Production implementation
requires a separate owner decision after review of these documents.
