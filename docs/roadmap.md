# Milestone roadmap

| Phase | Milestones | Decision gate |
| --- | --- | --- |
| Foundation | D0 discovery and build scaffolds | Accepted with qualifications |
| Language bridge | D1 parsing | **Accepted with qualifications and closed**; see [the owner record](d1-acceptance.md) |
| Validation | D2 lowering/schema validation | **Accepted with qualifications and closed**; see [the owner record](d2-acceptance.md) and [D2-QUAL-02](reviews/d2-qualification-02/README.md). |
| Execution feasibility | D3 CedarSharp; D4 events/reference temporal | **D3 closed with qualifications.** See the [D3 closure and release preparation record](reviews/d3-close-release-prep-01/README.md) and the [Q3-R1 independent requalification](reviews/d3-qualification-01-q3-r1/README.md). D4 and D5 remain separately unauthorized. |
| Reference conformance | D5 temporal authorization | Corpus parity and owner acceptance |
| Extensibility | D6 backend/provider contracts | Avoid undefined provider/failure behavior |
| Hardening | D7 threat-driven controls | Security qualification distinct from parity |
| Distribution | D8 packages, D9 preview, D10 stable | Exact artifact evidence and explicit release authorization |

D3 was implemented and independently requalified, and the owner accepted the
Q3-R1 disposition B with the documented residual qualifications. A preview
release candidate is prepared under `DOGWOOD-SHARP-D3-CLOSE-RELEASE-PREP-01`;
publication remains subject to separate explicit owner authorization. D4 and
D5 require separate commissions. A D3 host must account for the resident D2
bridge, validated bundle completeness and rule IDs reported separately from
Cedar text. Plugins can share the
default-context DogwoodSharp instance. Hot reload and unloadable
`AssemblyLoadContext` boundaries cannot reclaim the native image. Process
isolation is a future hosting option for image replacement or native-memory
reclamation. D6 is optional for an initial smaller preview and its included
features must pass D7. Dates depend on agreed scope and acceptance.

Separate roadmap items: durable temporal stores and recovery, production-grade
incremental temporal evaluation, distributed multi-agent reservation coordination,
additional RIDs, and a future Penghou.Hufu.Dogwood adapter. These belong to
separately commissioned work; Hufu integration never becomes a core dependency.
