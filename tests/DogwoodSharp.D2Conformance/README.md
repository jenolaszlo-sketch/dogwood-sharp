# D2 differential fixtures

`lowering-v1.json` records the upstream pin, the shared Cedar action schema, and
one case per Dogwood source plus its optional action-schema override, event
schema, macro library and lowering distincter. Each pinned `expect` block is the
outcome of parse, lower and validate, derived from the pinned Dogwood crate by
`native/dogwoodsharp-d2-diff`.

The differential crate never writes during verification. It re-derives every
case from the pinned crate, checks the pinned expectation still matches
upstream, and then compares the native bridge's exported C ABI replies for
operations 2 (`validate`) and 3 (`lower`) against the R1 wire contract.
It compares Cedar policy and schema text exactly and Cedar schema JSON
structurally. Diagnostic categories and locations are compared; upstream
diagnostic message text can vary.

The upstream `expect` for `temporal-policy-conflicting-declaration` retains the
actual String-to-Boolean replacement. Its `wrapperCollision` annotation
identifies the intentional wrapper rejection and is verified on both
operations. The runner preserves upstream validation failures and verifies
that they have no exported rules or Cedar artifacts. Managed tests run the same
cases on both frameworks, alongside additional R1 boundary cases.

Regenerate the pinned expectations only when the upstream pin changes:

```powershell
cargo run --manifest-path native/dogwoodsharp-d2-diff/Cargo.toml -- `
  generate tests/DogwoodSharp.D2Conformance/lowering-v1.json
```

Run `./eng/Verify.ps1` from the repository root and see
[verification](../../docs/verification.md) for actual evidence.