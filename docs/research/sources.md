# Investigation sources and baseline

Investigation date: 2026-11-16 (Asia/Manila). Sources were cloned read-only for
inspection; no changes were made to CedarSharp, Baize, or upstream Dogwood.

| Repository | Investigated revision |
| --- | --- |
| Dogwood | `929f49e7305534ac61ef06cf8e47269c596dc851` |
| CedarSharp main | `a25181d17603381e42adde60613f7a49d5309a2b` |
| Penghou.Baize | `3986dde792f6af49f4f3a51e27db1a6d721d691f` |
| DogwoodSharp starting HEAD | `c928bfa52d15ac39d4b54802e3c161cc31117f66` |
| DogwoodSharp starting tree | `8f57a99980891ccc68701b94b94342f7ae0e02d6` |

Target identity: `https://github.com/jenolaszlo-sketch/dogwood-sharp.git`.
Fresh checkout at `D:\projects\dogwood-sharp`, branch `main`, clean before edits;
only LICENSE existed. No applicable target/parent AGENTS.md was present.

CedarSharp NuGet 1.0.0 was inspected from the restored package, not inferred from
main. Its nuspec records commit `68ffdafa3bd9a2e4e5744069220bededb5684c5b`;
the Windows native manifest records source commit
`324d3c09fc94ab91464ec340eb81e4b167deb6f5`, ABI 1, Rust 1.94.0, Cedar 4.13.0,
and SHA-256 `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895`.
These different revision identifiers describe different release inputs; do not
substitute main's SHA for the published binary's provenance.

## Source index

- [DW-ROOT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/README.md)
- [DW-MANIFEST](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/Cargo.toml)
- [DW-API](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/lib.rs)
- [DW-LOWER](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/policy_set.rs)
- [DW-ENGINE](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/engine.rs)
- [DW-AUTH](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/authorize/mod.rs)
- [DW-LOOP](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs)
- [DW-EVENT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/interpreter/value.rs)
- [DW-PROVIDER](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/extension/provider/eval.rs)
- [DW-PROVIDER-CONTRACT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-docs/guide/05-information-providers.md)
- [DW-FORMAT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-formatter/src/lib.rs)
- [DW-DIAGNOSTICS](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/error.rs)
- [DW-TESTS](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/tests)
- [DW-CORPUS](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/corpus.rs)
- [DW-DOC-TESTS](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-docs/tests/examples.rs)
- [CS-INPUT](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/CedarInputs.cs)
- [CS-RESULT](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/CedarResults.cs)
- [CS-NATIVE](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/native/src/lib.rs)
- [CS-LOADER](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/NativeBridge.cs)
- [CS-LOCK](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/native/Cargo.lock)
- [CS-MANIFEST](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/native/Cargo.toml)
- [CS-PROJECT](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/CedarSharp.csproj)
- [CS-CI](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/.github/workflows/ci.yml)
- [CS-PUBLISH](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/.github/workflows/publish.yml)
- [BZ-CI](https://github.com/jenolaszlo-sketch/penghou-baize/blob/3986dde792f6af49f4f3a51e27db1a6d721d691f/.github/workflows/ci.yml)
- [BZ-PUBLISH](https://github.com/jenolaszlo-sketch/penghou-baize/blob/3986dde792f6af49f4f3a51e27db1a6d721d691f/.github/workflows/publish.yml)

## Additional references

- [Dogwood guide](https://dogwood-policy.github.io/dogwood/) establishes the documentation structure; substantive findings below use pinned source.
- [OpenShell Cedar context, 2026-11-09](https://nvidia.github.io/OpenShell-Research/dev-notes/posts/2026-11-09-cedar-policy-language/): motivation for temporal policies and policy inclusion. It is architectural context, not evidence that the Dogwood reference interpreter is production-qualified.
- NuGet v3 flat-container queries on the investigation date: DogwoodSharp returned HTTP 404; CedarSharp returned HTTP 200 with 1.0.0 as the newest listed version. No observed published DogwoodSharp conflict; this does not prove ownership eligibility or reserve the ID.

## Dependency evidence

Dogwood has no tracked Cargo.lock at this revision (`git ls-files '*Cargo.lock'`
returned no files). The committed research Cargo.lock is a newly resolved,
reproducible discovery graph, not an upstream lockfile. It resolves Dogwood 1.1.2,
Cedar policy/core/formatter 4.11.0, MCP schema generator 0.6.1, MCP SDK 0.4.1,
and Rhai 1.27.0. The generator's registry manifest pins cedar-policy-core
`=4.11.0`; this explains why reading Dogwood's `4.11` declaration alone is
insufficient. `cargo tree -e features -i cedar-policy` shows datetime, decimal,
and ipaddr enabled. Dogwood `net`, `corpus`, and `experimental-arithmetic` are off.

The research Cargo.lock preserves source URLs, full Git revision and registry
checksums. CedarSharp's tracked native lock resolves Cedar 4.13.0; its manifest
explicitly enables datetime, decimal and ipaddr with default features disabled.
