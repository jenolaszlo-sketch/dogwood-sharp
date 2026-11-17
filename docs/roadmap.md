# Milestone roadmap

| Phase | Milestones | Decision gate |
| --- | --- | --- |
| Foundation | D0 discovery and build scaffolds | Accepted with qualifications |
| Language bridge | D1 parsing | **Accepted with qualifications and closed**; see [the owner record](d1-acceptance.md) |
| Validation | D2 lowering/schema validation | **Authorized by the D1 acceptance record**; requires independent D2 qualification and owner acceptance |
| Execution feasibility | D3 CedarSharp; D4 events/reference temporal | Resolve interop risks before backend promise |
| Reference conformance | D5 temporal authorization | Corpus parity and owner acceptance |
| Extensibility | D6 backend/provider contracts | Avoid undefined provider/failure behavior |
| Hardening | D7 threat-driven controls | Security qualification distinct from parity |
| Distribution | D8 packages, D9 preview, D10 stable | Exact artifact evidence and explicit release authorization |

D3 and D4 can proceed independently after D2; D5 joins them. D6 is optional for
an initial smaller preview but its included features must pass D7. Dates are not
estimated until scope and acceptance are agreed.

Separate roadmap items: durable temporal stores and recovery, production-grade
incremental temporal evaluation, distributed multi-agent reservation coordination,
additional RIDs, and a future Penghou.Hufu.Dogwood adapter. These belong to
separately commissioned work; Hufu integration never becomes a core dependency.
