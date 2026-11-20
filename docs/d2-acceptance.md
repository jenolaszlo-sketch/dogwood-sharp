# DOGWOOD-SHARP-D2 owner acceptance record

Recorded 2026-11-20. This record captures the owner's acceptance decision for
D2 and the separate authorization for D3-E1. It does not modify the D2 producer
report ([d2-implementation.md](d2-implementation.md)), the R2 verification
record ([d2-r2-verification.md](d2-r2-verification.md)) or the independent
qualification evidence ([reviews/d2-qualification-02](reviews/d2-qualification-02/README.md)).

## Decision

D2 (lowering, schemas and validation) is **ACCEPTED WITH QUALIFICATIONS** and
**CLOSED**.

Independent qualification DOGWOOD-SHARP-D2-QUAL-02 reported **B. SUPPORTED WITH
QUALIFICATIONS** and recommended owner acceptance under the approved
process-lifetime hosting contract. It found no new blocking defect. The
qualifier's recommendation is adopted.

The six-cell matrix passed: `win-x64`, `linux-x64` and `osx-arm64`, each on
`net8.0` and `net10.0`, with the host-specific macOS retry recorded in the
qualification report. QD2-01 and QD2-03 are closed; the QD2-02 documentation
correction is confirmed.

## Accepted qualifications

| ID | Accepted qualification |
| --- | --- |
| QD2-01 | Closed. The incomplete-schema response mutation is rejected as `MalformedResponse`; valid empty and nonempty schema exports pass. |
| QD2-02 | Confirmed. Warning diagnostic locations remain null under the existing wire contract. Source provenance and UTF-8 byte-offset rebasing require a separately verified mapping before they can be populated. |
| QD2-03 | Closed within the approved resident contract. The D2-enabled native image remains resident until process termination. Unload, in-process reclamation and native image replacement are outside the supported boundary; process isolation is a future hosting option. |

Additional qualifications carried forward:

- Keep the documented input, distincter, request and response size limits. They
  establish no general memory quota or wall-time guarantee.
- Use the returned rule IDs and mappings. Reparsing Cedar policy text assigns
  new IDs.
- A temporal export that requires generated context still requires trusted
  generated values.
- Do not claim Linux or macOS qualification from Windows-only evidence.

## Authorizations

This acceptance authorizes **D3-E1 only**: a bounded CedarSharp interoperability
investigation with research-only prototype code, focused tests, a differential
corpus against the pinned Cedar version, and the resulting evidence documents.

Full D3 implementation, a stable or public production API, D4 event/reference
temporal execution, D5 temporal authorization, package publication, tags and
releases remain **unauthorized**. Each requires its own commission or explicit
authorization after the preceding gate.

## Preserved boundaries

The D1 acceptance, the D2 R1/R2 evidence, all archived qualification trees,
lockfiles and the original Discovery-01 proposal are unchanged. This record
adds the owner decision and the D3-E1 authorization; it does not reinterpret
D2's validated scope as temporal or provider authorization.

## Record

- Owner acceptance record: this document.
- D2 producer report: [d2-implementation.md](d2-implementation.md).
- R2 producer verification: [d2-r2-verification.md](d2-r2-verification.md).
- Independent D2 qualification: [reviews/d2-qualification-02/README.md](reviews/d2-qualification-02/README.md).
- R2 owner decisions: [d2-r2-owner-decisions.md](d2-r2-owner-decisions.md).
- Roadmap status updated in [roadmap.md](roadmap.md); ADR-0001 status line
  updated in [architecture/0001-backend-strategy.md](architecture/0001-backend-strategy.md).
