# DOGWOOD-SHARP-D1 owner acceptance record

Recorded 2026-11-18. This record captures the owner's acceptance decision for
D1. It does not modify the D1 producer report
([d1-implementation.md](d1-implementation.md)), its verification record
([verification.md](verification.md)) or the independent qualification evidence
([reviews/d1-qualification](reviews/d1-qualification/README.md)).

## Decision

D1 (versioned native bridge and Dogwood policy parsing) is **ACCEPTED WITH
QUALIFICATIONS** and **CLOSED**.

Independent qualification DOGWOOD-SHARP-D1-QUAL-01 passed with no blocking
findings: DOGWOOD-SHARP-D1-IMPL-01 is qualified on `win-x64`, `linux-x64` and
`osx-arm64` for `net8.0` and `net10.0`. The qualifier's recommendation is
adopted.

## Accepted qualifications

The three informational observations recorded by the qualifier are accepted as
documented limitations of D1 rather than defects to remediate before D2:

| ID | Accepted limitation |
| --- | --- |
| QF-01 | A 1 MiB malformed string token containing control characters or backslashes can expand the encoded diagnostic reply past the 2 MiB response ceiling, so the bridge returns status 6 with an empty output instead of partial diagnostics. |
| QF-02 | Diagnostic message text is produced by the pinned upstream parser and may change with upstream revisions; only message non-emptiness is asserted, while counts, categories and locations are compared exactly. |
| QF-03 | The producer Valgrind run reports one 48-byte runtime-loader `possibly lost` block, with zero errors and zero definitely or indirectly lost bytes. These are not bridge allocations. |

## Authorizations

This acceptance authorizes **D2 implementation only** (policy lowering,
schemas, validation, augmented schema export, rule/source mapping and
upstream differential qualification fixtures).

D3 CedarSharp interoperability, D4 temporal execution and NuGet publishing
remain **unauthorized**. Authorization for those milestones requires their own
commission after D2 qualification.

## Preserved exclusions

D1's documented boundary is unchanged: parsing and version discovery only, with
schema validation and authorization outside the guarantee. D2 must not
reinterpret D1's accepted scope as authorization support.

## Record

- Owner acceptance record: this document.
- D1 producer report: [d1-implementation.md](d1-implementation.md).
- D1 producer verification: [verification.md](verification.md) (D1 sections).
- Independent D1 qualification: [reviews/d1-qualification/README.md](reviews/d1-qualification/README.md).
- Roadmap status updated in [roadmap.md](roadmap.md); ADR-0001 status line
  updated in [architecture/0001-backend-strategy.md](architecture/0001-backend-strategy.md).