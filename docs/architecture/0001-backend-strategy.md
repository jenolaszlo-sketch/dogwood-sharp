# ADR-0001: staged reference and CedarSharp backends

Status: **Proposed — owner acceptance pending**. Date: 2026-11-16.

Context: Dogwood supplies temporal semantics and public engine seams. CedarSharp
is preferred for final Cedar evaluation, but the resolved Cedar versions differ
and complete interop has not been qualified. See [options](options.md) and
[compatibility](../research/cedarsharp-compatibility.md).

Proposed decision: implement a native reference backend first, then qualify a
CedarSharp-backed PolicyEngine adapter against it. Expose backend identities and
capability/guarantee boundaries explicitly. Keep the reference interpreter labeled
for semantic testing. Do not add public production policy APIs in D0.

Use versioned UTF-8 serialization, opaque owned session handles, locked upstream
revision/features and a fail-closed application enforcement helper separate from
raw reference decisions. Network providers and experimental arithmetic stay off.

Consequences: additional native footprint and qualification cost; no assumed
Cedar deduplication; strong differential oracle; API design is deferred until
the boundary is proven. A-only remains the fallback. Durable storage, distributed
reservations and Hufu integration remain separate projects.

Acceptance needed: approve the staged approach and authorize D1. D3 must separately
resolve version skew, mixed entity conformance, callback lifetime/AOT and error
identity. Technical qualification and owner acceptance are separate records.
