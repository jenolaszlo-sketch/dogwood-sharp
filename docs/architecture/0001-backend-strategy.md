# ADR-0001: staged reference and CedarSharp backends

Status: **Accepted with D0 qualifications. D1 accepted with qualifications and
closed. D2 remediation required pending independent requalification and
separate owner acceptance.** Proposed 2026-11-16.

Context: Dogwood supplies temporal semantics and public engine seams. CedarSharp
is preferred for final Cedar evaluation, but the resolved Cedar versions differ
and complete interop has not been qualified. See [options](options.md) and
[compatibility](../research/cedarsharp-compatibility.md).

Decision: implement a native reference backend first, then qualify a
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

The D1 commission records owner acceptance of this approach. D3 must separately
resolve version skew, mixed entity conformance, callback lifetime/AOT and error
identity. Technical qualification and owner acceptance are separate records.

The [D2 owner lifetime decision](../d2-r2-owner-decisions.md) requires the
D2-enabled native image to remain resident until process termination. D3
architecture must account for this boundary in plugin hosts, hot reload and
collectible assembly contexts. Process isolation is a future option for
hosts that need native image replacement or memory reclamation.
