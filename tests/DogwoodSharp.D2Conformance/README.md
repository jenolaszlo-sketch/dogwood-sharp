# D2 differential fixtures

`lowering-v1.json` records the upstream pin, the shared Cedar action schema, and
one case per Dogwood source plus its optional action-schema override, event
schema, macro library and lowering distincter. Each pinned `expect` block is the
outcome of parse, lower and validate, derived from the pinned Dogwood crate by
`native/dogwoodsharp-d2-diff`.

The differential crate never writes during verification. It re-derives every
case from the pinned crate, checks the pinned expectation still matches
upstream, and then compares the native bridge's exported C ABI replies for
operations 2 (`validate`) and 3 (`lower`) against the same expectations. Cedar
policy text and augmented schema JSON are compared structurally; diagnostic
message text is upstream-owned and is outside the assertions.

Managed tests read the same file and compare `Dogwood.Validate` and
`Dogwood.Lower` against the pinned expectations on both frameworks, so the same
fixtures cover all three runtime identifiers and both frameworks.

Regenerate the pinned expectations only when the upstream pin changes:

```powershell
cargo run --manifest-path native/dogwoodsharp-d2-diff/Cargo.toml -- `
  generate tests/DogwoodSharp.D2Conformance/lowering-v1.json
```

Run `./eng/Verify.ps1` from the repository root and see
[verification](../../docs/verification.md) for actual evidence.