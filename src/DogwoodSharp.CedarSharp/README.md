# DogwoodSharp.CedarSharp (experimental qualification candidate)

`DogwoodSharp.CedarSharp` is an **experimental, package-only qualification
candidate**. It provides the schema-carrying CedarSharp authorization adapter
for Dogwood policy lowering.

| Package | Version | Role |
| --- | --- | --- |
| `DogwoodSharp.CedarSharp` | `0.0.0-experimental.1` | This experimental adapter |
| `DogwoodSharp` | `0.0.0-discovery.1` | Stable managed API and Dogwood native assets |
| `CedarSharp` | `1.0.0` | Cedar authorization engine (transitive dependency) |

The package targets `net8.0` and `net10.0`. It is trimmable and NativeAOT
compatible. Its dependency list contains only exact ranges for the stable
`DogwoodSharp` and `CedarSharp` 1.0.0 packages; neither is repacked into this
package. The
qualification consumer references only this experimental adapter package
directly.

This candidate is **not authorized for publication, signing, tagging, or
release**. A green producer package gate is not independent qualification and
is not owner acceptance. See `docs/ci-publishing.md` for the exact local and
hosted package-only commands and exclusions.
