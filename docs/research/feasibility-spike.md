# Bounded feasibility spike

Executed 2026-11-16 on Windows x64, .NET SDK 10.0.401, runtimes 8.0.31 and
10.0.12, Rust 1.99.1. Dogwood is Git-pinned and Cargo-locked; CedarSharp 1.0.0 is
a package dependency, not a source project reference.

The Rust experiment constructs original fixtures, validates policies, exports
policy-ID/text mappings and augmented JSON schema, computes one temporal binding
independently from a fixed approval history, and compares the expected decision
with the upstream authorizer. The .NET experiment evaluates the exported policies
through CedarSharp and asserts decision, determining ID set and error-free result.

| Case | Upstream | CedarSharp net8.0 | CedarSharp net10.0 | Determining IDs |
| --- | --- | --- | --- | --- |
| plain-allow | Allow | Allow | Allow | policy_0 |
| plain-forbid | Deny | Deny | Deny | policy_1 |
| temporal-no-history | Deny | Deny | Deny | empty |
| temporal-approved | Allow | Allow | Allow | policy_0 |
| temporal-expired | Deny | Deny | Deny | empty |
| temporal-wrong-key | Deny | Deny | Deny | empty |

All cases had no diagnostics errors. An approval at timestamp 1, read at 5 and
10-second window produces Allow; absent approval, mismatched key, or read at 20
produces Deny. The event schema is explicitly unpinned to keep the experiment
bounded; no tenant-isolation result follows. Approval responses are history-only
and are asserted to return no decision. Logged fields and request context are
populated separately for decision events.

## Reproduce

From the repository root:

```powershell
./eng/Run-Spike.ps1
```

Sources: [Rust spike](../../research/interop-spike/src/main.rs),
[managed consumer](../../research/CedarSharp.Spike/Program.cs), and
[locked Rust graph](../../research/interop-spike/Cargo.lock).
Generated JSON and build output are ignored; retained evidence is the original
fixture source, pinned dependency graph, assertions and this execution record.

## Limits

This does not prove complete temporal semantics, exact window endpoints,
providers, macros, action hierarchies, attributed entities, source-location parity,
nonempty error equivalence, native callback/AOT safety, or concurrent histories.
It does not run the whole upstream corpus. It supports further investigation of
CedarSharp reuse despite Cedar 4.11.0 versus 4.13.0, not a production backend claim.
