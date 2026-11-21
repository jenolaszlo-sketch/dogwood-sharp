# Architecture options and recommendation

Status update, 2026-11-21: D3-E1 was accepted with qualifications and owner
acceptance of D3-PREP-01 only. The accepted [D3 integration
contract](../d3-integration-contract.md) therefore selects the managed,
schema-carrying, request-validating CedarSharp path shown in option A. The
historical callback-based implementation described below is superseded as the
future D3 design and remains only as proposal material. It does not authorize
any D3 implementation by itself.

Recommendation: **Option C, staged**. Establish a native reference backend as
the oracle, and qualify a separate CedarSharp-backed path before exposing it as
a supported backend. The pre-D3-E1 proposal described a preferred callback;
the current D3 design is the direct schema-carrying path in option A. This
proposal awaiting owner acceptance is historical; see
[the integration contract](../d3-integration-contract.md).

| Criterion | A: full native authorizer | B: CedarSharp decision path | C: native reference plus qualified CedarSharp path |
| --- | --- | --- | --- |
| Semantic fidelity | Closest to upstream lifecycle | Conditional on lossless adapter and version parity | Oracle retained for differential checks |
| Security | Reference limitations remain | Same temporal risks; adds callback/serialization risks | Explicit backend guarantees; more code to review |
| Testability | Upstream corpus directly reusable | Needs independent parity tests | Strongest differential evidence |
| Performance | Single native call; history cost remains | Additional serialization/callback; benchmark needed | Backend-specific budgets; no measured advantage claimed |
| Packaging | One Dogwood native library | Dogwood plus CedarSharp native assets | Two backend asset paths to qualify |
| Duplication | Dogwood contains Cedar | Cedar remains in Dogwood for parsing/lowering/validation | Cannot promise removal of duplicated Cedar |
| API usability | Reference session is straightforward | Callback lifetime and full request transfer are internal concerns | Explicit backend selection prevents false guarantees |
| Maintainability | Upstream upgrades still need tests | Version skew and contract mapping add work | More qualification work, clearer regression oracle |
| Extensibility | Public upstream backend seams | Reuses mature managed Cedar API | Supports future temporal engines without committing now |
| Implementation complexity | Lowest | Higher | Highest overall; staged to bound each increment |
| Upgrade compatibility | Pin and rerun corpus | Two independently versioned engines | Block upgrades until both paths qualify |

Verified enabling seams: exported policy/schema artifacts and public
`PolicyEngine`/`TemporalEngine`; complete request/entity delivery at the policy
engine call. [DW-LOWER](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/policy_set.rs), [DW-ENGINE](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/engine.rs), [DW-LOOP](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs).

The preferred B implementation retains Dogwood's authorizer pipeline and installs
a Rust PolicyEngine adapter that serializes its request into a synchronous
managed CedarSharp call. It should capture schema export and ID maps before
moving the lowered bundle into the authorizer. This avoids copying private
provider argument resolution, context construction and entity population.
Callback/AOT/lifetime feasibility is still unproven; D3 must prototype it.
A separate managed reconstruction of the entire decision loop is not recommended.

The spike demonstrates a narrower path: public lowering and augmented schema
export, independently calculated boolean for one temporal expression, and
evaluation through published CedarSharp. It does not demonstrate the full
callback backend, provider transfer or entity equivalence.

Public API sufficiency is therefore **conditional**: enough seams exist to avoid
copying internals, but serialization of complete request/entities and error
mapping must be qualified. In particular Dogwood can add bare scope entities
without schema conformance while validating caller-supplied entities separately;
CedarSharp schema-aware parsing may not reproduce that mixed treatment.
This is a concrete D3 negative-control requirement, not a resolved equivalence.

Choose A alone if D3 cannot preserve semantics economically. Do not modify
CedarSharp in this commission; propose any necessary extensions separately.
No architecture choice turns the reference temporal evaluator into a production
transactional enforcement system.
