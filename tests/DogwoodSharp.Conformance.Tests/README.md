# D1 differential fixtures

`parsing-v1.json` records the upstream pin, source, expected policy count or
diagnostic category, and expected diagnostic byte ranges for each case.
The fixtures are repository-authored probes of grammar forms in the pinned
Dogwood parser. Rust compares each ABI result to a direct call to
`ParsedPolicySet::parse` with the same default service schema. Managed tests
compare the staged cdylib to the same fixture expectations on both frameworks.
Exact diagnostic messages are outside the assertions.

Run `./eng/Verify.ps1` from the repository root for both paths and see
[verification](../../docs/verification.md) for actual evidence.