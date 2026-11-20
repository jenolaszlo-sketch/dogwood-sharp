# ADR-0001: staged reference and CedarSharp backends

Status: **Accepted with D0 qualifications. D1 accepted with qualifications and
closed. D2 accepted with qualifications and closed. D3-E1 completed with
compatibility decision B; D3-PREP-01 selected the schema-carrying,
request-validating CedarSharp path; full D3 implementation remains
unauthorized.**
Proposed 2026-11-16.

D3-PREP-01 status update, 2026-11-21: the D3-E1 acceptance resolved D3-E1 as
a candidate backend but did not authorize full D3. The accepted integration
contract supersedes the historical callback-based preferred implementation in
`options.md`; the native upstream reference remains the differential oracle.

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

The D1 commission records owner acceptance of this approach. D3-E1 established
the 4.11.0/4.13.0 version pair and ID-preserving request construction for a
bounded corpus, with decision B (compatible with qualifications). Full D3 must
still resolve upstream entity-store equivalence, trusted temporal wiring,
concurrency and cross-platform qualification before a backend promise. Technical
qualification and owner acceptance are separate records.

The [D2 owner lifetime decision](../d2-r2-owner-decisions.md) requires the
D2-enabled native image to remain resident until process termination. D3
architecture must account for this boundary in plugin hosts, hot reload and
collectible assembly contexts. Process isolation is a future option for
hosts that need native image replacement or memory reclamation.
