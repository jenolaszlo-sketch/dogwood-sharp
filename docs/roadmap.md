# Milestone roadmap

| Phase | Milestones | Decision gate |
| --- | --- | --- |
| Foundation | D0 discovery and build scaffolds | Accepted with qualifications |
| Language bridge | D1 parsing | **Accepted with qualifications and closed**; see [the owner record](d1-acceptance.md) |
| Validation | D2 lowering/schema validation | **Accepted with qualifications and closed**; see [the owner record](d2-acceptance.md) and [D2-QUAL-02](reviews/d2-qualification-02/README.md). |
| Execution feasibility | D3 CedarSharp; D4 events/reference temporal | **D3-E1 compatibility investigation completed: decision B, compatible with qualifications.** Full D3 implementation, D4 and D5 remain separately unauthorized. See [the E1 report](d3-e1-compatibility.md). |
| Reference conformance | D5 temporal authorization | Corpus parity and owner acceptance |
| Extensibility | D6 backend/provider contracts | Avoid undefined provider/failure behavior |
| Hardening | D7 threat-driven controls | Security qualification distinct from parity |
| Distribution | D8 packages, D9 preview, D10 stable | Exact artifact evidence and explicit release authorization |

D3-E1 was separately authorized and completed as bounded feasibility evidence
(decision B). Full D3 implementation and D4 require separate commissions; D5
joins them. A future D3 host must account for the resident D2 bridge, validated
bundle completeness and rule IDs reported separately from Cedar text. Plugins can share the
default-context DogwoodSharp instance. Hot reload and unloadable
`AssemblyLoadContext` boundaries cannot reclaim the native image. Process
isolation is a future hosting option for image replacement or native-memory
reclamation. D6 is optional for an initial smaller preview and its included
features must pass D7. Dates depend on agreed scope and acceptance.

Separate roadmap items: durable temporal stores and recovery, production-grade
incremental temporal evaluation, distributed multi-agent reservation coordination,
additional RIDs, and a future Penghou.Hufu.Dogwood adapter. These belong to
separately commissioned work; Hufu integration never becomes a core dependency.
