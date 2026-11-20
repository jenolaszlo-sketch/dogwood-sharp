# DOGWOOD-SHARP-D3-E1 owner acceptance

Decision date: **2026-11-21**

## Decision

**D3-E1: ACCEPTED WITH QUALIFICATIONS and CLOSED.**

The owner accepts the D3-E1 feasibility evidence and CedarSharp 1.0.0
(Cedar 4.13.0) as the candidate Cedar evaluation backend for a future D3
implementation, subject to the binding qualifications and gates below.

This acceptance closes the D3-E1 feasibility/qualification finding set. It
does **not** accept a production adapter, a stable public API, a package, a
release, or upstream-equivalence.

## Accepted evidence

The accepted evidence is the independently qualified delivery at:

- source commits `14f18ac`, `3f22013`, and `e73c1ca`;
- qualified producer revision `001bd3e`;
- [the D3-E1 qualification report](reviews/d3-e1-qualification-01/README.md);
- [the producer compatibility report](d3-e1-compatibility.md);
- [the producer verification record](d3-e1-verification.md);
- the Q1 reviewer probe, NativeAOT evidence, CI attestation, and committed
  SHA-256 evidence under `docs/reviews/d3-e1-qualification-01/evidence/`.

The accepted result is bounded to the corpus and host scope recorded there:
49 reviewer cases on `net8.0` and `net10.0`, the supporting 30-case
CedarSharp/Cedar 4.11 differential, and `win-x64` NativeAOT coexistence.
Linux, macOS, concurrency, performance, package-only consumption, and the full
upstream-authorizer corpus were not qualified by D3-E1.

## Binding qualifications

The following remain binding for every future D3 implementation and
qualification:

1. Preserve D2-generated policy IDs and map determining/erroring identities by
   ID, using set semantics. A text-form policy set or positional mapping is
   not acceptable.
2. Use a complete per-rule ID/body export. Text splitting is not a production
   identity contract.
3. Attach the augmented schema to every request and validate the request on
   every dispatch. There is no schema-less fallback.
4. Preserve the complete `appliesTo` object of every dispatchable action.
5. Use the schema-carrying entity mode defined in the
   [D3 integration contract](d3-integration-contract.md): exact caller
   entities, no bare scope injection, no caller-supplied action entities or
   relationships, and no adapter-side synthesis of attributes, parents, or
   relationships. Caller-supplied action data MUST be rejected before
   dispatch.
6. Treat an error-free Allow as the only authorization success. A raw Allow
   carrying policy errors is not a clean Allow.
7. Keep generated temporal context fields owned by the trusted pipeline and
   reject caller injection.
8. Map policy errors, request/schema validation errors, lowering errors, and
   native transport errors distinctly; do not compare diagnostic wording as
   the semantic contract.
9. Keep both native images resident until process exit. Do not promise unload,
   hot replacement, or collectible native handles.
10. Requalify the affected gates whenever Dogwood, the bridge, CedarSharp,
    Cedar, the schema/export format, entity mode, validation mode, temporal
    pipeline, native ABI, or public/package shape changes.
11. Run the complete pinned upstream-authorizer corpus before claiming
    upstream compatibility or recommending D3 for owner acceptance.

## Authorization boundary

The owner authorized **DOGWOOD-SHARP-D3-PREP-01 only**:

- record this D3-E1 acceptance;
- remediate and verify the over-broad archived-R2 CI guard;
- resolve and document the D3 authorization-semantics architecture;
- design and calibrate the upstream-authorizer differential;
- turn the D3 plan into a binding implementation and qualification contract;
- dispose of QD3-01 through QD3-09 with evidence; and
- commit and push the authorized documentation, CI remediation, focused
  probes, and evidence.

The following remain **unauthorized**:

- full D3 implementation or production adapter/API;
- a D2 bridge production operation for per-rule export;
- a CedarSharp modification;
- a dependency or version-pin upgrade;
- unrelated CI, package, or publication changes;
- package publication, signing, a release tag, or release;
- a durable temporal/provider implementation;
- an upstream-equivalence claim before the mandatory corpus passes.

D3-PREP-01 does not authorize those later changes. They require a separate
owner commission.

## Finding disposition

QD3-01 through QD3-09 are dispositioned in the
[D3-PREP-01 review](reviews/d3-prep-01/README.md). QD3-01 and QD3-02 are
resolved by the binding contract; their implementation/qualification gates
remain pending and are not waived. QD3-03 is closed by the scoped guard
remediation and the green hosted producer-verification matrix recorded in the
PREP evidence. QD3-04 through QD3-09 are documented qualifications; QD3-08
remains a deferred mandatory upstream-corpus qualification and the current
calibration narrows but does not waive it.

## Reopening and requalification triggers

This acceptance MUST be reopened for owner review if a future implementation
cannot meet the binding contract, if the selected entity/scope mode must
change, if an upstream difference is found to permit an Allow where D3 must
reject, if the upstream corpus cannot qualify the claimed semantics, or if a
version/package change invalidates the recorded evidence.

A green producer suite, an implemented plan, or a successful publish does not
replace independent qualification or a separate owner acceptance.
