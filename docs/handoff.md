# DOGWOOD-SHARP-DISCOVERY-01 handoff

Historical producer handoff. The owner subsequently accepted D0 with
qualifications and commissioned D1. See the [D1 report](d1-implementation.md).

**Formal disposition:** D0 discovery, planning and buildable foundation prepared
for owner review. Architecture proposed, not accepted. Stop before production
implementation. No package, release tag or external publishing setting changed.

## Repository

- Identity: https://github.com/jenolaszlo-sketch/dogwood-sharp
- Local project: `D:\projects\dogwood-sharp`, branch `main`.
- Starting HEAD/tree and verified upstream revisions: [source record](research/sources.md).
- Final revision: the commit containing this handoff; use `git rev-parse HEAD`
  after pulling main. The delivery response records the concrete revision.

## Findings and recommendation

Recommend staged Option C: native upstream reference backend, followed by a
separately qualified CedarSharp execution path through the public PolicyEngine
seam. CedarSharp reuse is **conditionally supported**. Dogwood exports lowered
policy IDs/text and augmented schemas, and exposes complete request/entity input
at its engine seam; reproducing the private decision loop in C# is unnecessary.

Dogwood's research graph resolves Cedar 4.11.0 while published CedarSharp 1.0.0
contains Cedar 4.13.0. The six-case spike matched decisions, determining IDs and
error-free diagnostics on both .NET versions, including temporal negative
controls. This is bounded feasibility, not full interoperability qualification.
No CedarSharp changes were made; possible version/entity/prepared-input work is
listed separately in [compatibility](research/cedarsharp-compatibility.md).

The source investigation found stale documentation about empty entities and
missing Rhai limits. The report cites actual entity population and operation,
depth and data-size limits. Neither removes upstream reference security limits.

## Foundation and readiness

The solution includes an empty documented library for net8.0/net10.0, foundation
tests/sample, an empty Rust cdylib, centralized versions/locks, SourceLink,
deterministic builds, analyzers, XML docs, license/notice and opt-in local symbol
packaging. Research code is isolated from the library and main solution.

Foundation CI passed for all three requested RIDs and both frameworks (run 43879066633). Publication
is deliberately blocked pending exact-artifact qualification and owner approval;
the documented future workflow uses NuGet/login OIDC. NuGet query found no
published DogwoodSharp entry, but no ID reservation/ownership guarantee was made.
See [verification](verification.md) for executed checks and explicit omissions.

## Remaining risks and owner decisions

Accept or revise [ADR-0001](architecture/0001-backend-strategy.md), then commission
D1 only: ABI version discovery, bounded UTF-8 buffers, native ownership and policy
parsing with diagnostic tests. D3 must resolve version skew, mixed entity
conformance, full request serialization and callback/AOT lifetime before promising
a CedarSharp backend. Broader provider, corpus and cross-platform security
qualification remains required.

Reference temporal evaluation has no production event authentication, trusted
timestamp enforcement, bounded durable storage or atomic spending reservation.
Applications and optional infrastructure own those operational responsibilities.
Do not describe reference conformance as production security qualification.

## Deliverable index

1. [Upstream discovery](research/discovery.md)
2. [Capability/API inventory](research/capabilities.md)
3. [Architecture options](architecture/options.md)
4. [CedarSharp compatibility](research/cedarsharp-compatibility.md)
5. [Feasibility evidence](research/feasibility-spike.md)
6. [Proposed ADR](architecture/0001-backend-strategy.md)
7. [Threat model](architecture/threat-model.md)
8. [Native interop design](architecture/native-interop.md)
9. [CI/publishing strategy](ci-publishing.md)
10. [Implementation plan](implementation-plan.md)
11. [Roadmap](roadmap.md)
12. [Qualification strategy](qualification.md)
13. [Buildable foundation](../DogwoodSharp.slnx)
