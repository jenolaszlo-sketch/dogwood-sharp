# DOGWOOD-SHARP-D3 integration contract

Status: **binding design contract for a future authorized D3 implementation**.
This document resolves the D3 authorization-semantics architecture. It does not
authorize implementation, package publication or release.

## 1. Decision

DogwoodSharp will use a **schema-carrying, request-validating CedarSharp
mode** for the future D3 evaluation adapter:

1. Attach the augmented schema exported by the D2 lowering to every request.
2. Validate the request against that schema on every dispatch.
3. Pass the caller's entities exactly as supplied.
4. Do not inject bare scope entities and do not synthesize entity attributes,
   parents, or action-hierarchy relationships.
5. Derive action hierarchy from the attached schema.
6. Never fall back to schema-less authorization when schema validation,
   parsing, transport, or native evaluation fails.

This mode is intentionally **not claimed to be byte-for-byte or diagnostic-for-
diagnostic equivalent to the upstream Dogwood authorizer**. It is a fail-closed
security contract with bounded, recorded differences. Upstream differential
qualification remains a mandatory D3 gate.

The owner accepted D3-E1 with qualifications and authorized D3-PREP-01 only.
Selecting this contract for future D3 does not authorize the implementation.
See the [D3-E1 acceptance record](d3-e1-acceptance.md).

## 2. Why one mode is required

The upstream Dogwood authorizer constructs one entity store in three tiers
(`dogwood-language/src/api.rs`, `build_entities`):

1. Caller-supplied attributed entities are checked against the augmented
   schema. A conformance failure becomes a fail-closed Deny with diagnostics.
2. An omitted scope principal or resource is added as a bare, unchecked entity.
   This makes the scope UID present for identity comparisons but does not
   satisfy required attributes.
3. The schema's action entities and their `memberOf` edges are merged into the
   store so action-group membership resolves.

CedarSharp cannot reproduce all three tiers in one schema-carrying call. Its
attached schema makes every supplied entity conform, and a bare entity missing
a required attribute fails the whole call. Its schema-less mode does not carry
action hierarchy and, without an explicit scope check, can authorize a
principal outside the action's `appliesTo`.

The upstream oracle also demonstrated a security-relevant difference: the
upstream decision path does not itself enforce `appliesTo` for the principal or
resource. In the calibrated cases, a principal or resource outside `appliesTo`
could still reach an upstream Allow. The D3 contract therefore treats upstream
as a compatibility reference, not as the security oracle.

## 3. Alternatives considered

| Approach | Semantics and upstream compatibility | Schema / `appliesTo` | Hierarchy and bare scope | Required attributes and fail-closed behavior | CedarSharp / bridge needs | Principal risk | Complexity | Disposition |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **A. Schema-carrying, request-validating (recommended)** | Evaluates the exact D2-lowered rules with the augmented schema. It differs from upstream in bare-scope presence, diagnostic mode, and upstream's missing `appliesTo` enforcement; all differences are explicit. | Schema is always present. Scope, context, and entities are checked on every request. Out-of-`appliesTo` principal/resource and undeclared action are rejected. | Action hierarchy is derived from the schema. Omitted scope entities stay absent; no bare entity or action entity is synthesized. | Incomplete supplied entities and invalid context/entities fail before a decision. Missing scope attributes fail closed. No fallback. | Existing CedarSharp APIs suffice: `CedarSchema.FromJson`, `CedarAuthorizationRequest(..., schema, validateRequest: true)`, `Authorize`, `IsSuccess`, `IsCleanAllow`, and diagnostics. D3 still needs a per-rule D2 export for policy identity. | Does not reproduce upstream's bare-scope diagnostic mode. Upstream-compatible equivalence claims remain prohibited. | Medium | **Selected.** |
| **B. Upstream-shaped schema-less reconstruction** | Pre-check scope/context/entities against the schema, then call CedarSharp without the schema while adding bare scope entities and schema-derived action entities. This can reproduce more upstream outcomes. | Conformance depends entirely on a separate pre-check. A missing, stale, or bypassed pre-check recreates QD3-02. The schema-less dispatch itself does not enforce `appliesTo`. | Requires adapter construction of bare scope entities and action entities/edges. CedarSharp does not expose schema action-entity extraction; the adapter would need a native bridge change or a duplicate schema parser. | Pre-validation and reconstruction must be bound to one immutable snapshot. A bare scope entity bypasses required-attribute conformance by design. | Requires `CheckFullRequest` plus a trusted way to export action entities and construct the schema-less store; likely a D2 bridge operation. More CedarSharp API surface may be needed. | Highest risk of TOCTOU, hidden synthesis, stale schema, and accidental fallback. It conflicts with the no-silent-synthesis rule unless every derivation is explicit and separately qualified. | High | **Rejected for D3.** May be reconsidered only as a separately named, explicitly qualified mode with independent owner approval and differential evidence. |
| **C. Native upstream authorizer** | Uses the actual upstream Dogwood authorizer, including its entity-store, temporal, and provider pipelines. Highest fidelity to upstream. | Carries upstream behavior, including its observed lack of `appliesTo` enforcement unless an additional outer conformance check is added. | Upstream owns all three entity-store tiers. | Upstream fail-closed behavior applies, but scope applicability still needs an explicit outer check for D3's security contract. | A new native FFI/object lifecycle, policy and event transport, callback/temporal boundary, and state ownership model. CedarSharp is not the evaluator. | Large native ABI and lifecycle surface; duplicates the existing D2 bridge's upstream dependency; does not resolve the CedarSharp backend decision. | Very high | **Not selected.** Retain as an upstream differential/reference architecture, not a D3 production backend. |
| **D. Schema-less CedarSharp authorization** | Carries no schema; relies on policy and request shape alone. | Does not enforce `appliesTo`; the same request can Allow where the schema-carrying path rejects it. | Loses schema-derived action hierarchy and can deny silently without explicit action entities. | Entity/context parsing may be less strict; missing mechanisms can become silent Deny rather than an explicit conformance failure. | Existing authorization API could evaluate it, but no safe scope/entity guarantee exists without a separate check. | Directly violates the fail-closed and no-silent-fallback requirements. | Low implementation, unacceptable security | **Rejected.** |

A fifth combination—attach the schema but set `validateRequest` to false—is also
rejected. Schema-based parsing still occurs, but scope applicability is not
enforced on dispatch; it therefore weakens the selected guarantee without
solving the upstream bare-scope problem.

## 4. Binding request and entity-store contract

### 4.1 Lowered artifact ownership

A future adapter instance owns one immutable, complete D2 lowering bundle:

- the exact set of generated Cedar policy IDs;
- one exact Cedar body per generated ID;
- the augmented schema in canonical JSON form;
- the policy/schema provenance and validation state.

The bundle is immutable for the life of the adapter. Callers cannot replace,
append, or reorder its policies or schema. A request references that bundle; it
does not carry policy text independently.

The current D2 export concatenates policy bodies. D3-1 must add a complete
per-rule `(generated ID, exact body)` export before an adapter can be built.
The adapter must never split concatenated text as an identity contract and must
never use `CedarPolicySet.FromText` for identity-bearing decisions.

### 4.2 Canonical schema transport

The augmented schema JSON export is the single canonical schema value for
authorization, validation, caching, and equality. The text export is retained
only as a diagnostic/export artifact because CedarSharp does not consider the
two forms value-equal even when they behave identically.

Every request carries the canonical schema. The adapter verifies that the
policy set, schema, and lowering provenance belong to the same immutable
bundle. A schema mismatch is rejected before dispatch.

Canonicalization MUST preserve every dispatchable action's complete
`appliesTo` object from the D2 lowering, including its principal, resource,
and context constraints. It MUST NOT drop, replace, widen, narrow, or
synthesize an `appliesTo` object. A dispatchable action without a complete
`appliesTo` MUST fail bundle construction. A schema-only action-group
declaration is not dispatchable; a request targeting such an action MUST be
rejected. No schema-less fallback is permitted.

### 4.3 Scope, context, and entity data

The request supplies:

- principal, action, and resource UIDs;
- Cedar-encoded context JSON;
- caller entities in Cedar entity JSON form.

The adapter copies these values into one immutable request snapshot. It MUST NOT
synthesize, infer, add, remove, or rewrite any authorization-relevant entity,
attribute, parent, or relationship. In particular, it:

- does not add caller attributes;
- does not add or remove parents;
- does not create a bare scope entity;
- does not merge action entities;
- does not coerce extension values;
- does not rewrite IDs or entity references.

The immutable augmented schema is the sole source of action-group hierarchy.
CedarSharp's internal resolution of schema-defined action hierarchy is engine
behavior and is not adapter-created authorization data. Caller-supplied action
entities or relationships MUST be rejected before dispatch; the adapter MUST
NOT silently drop them, merge them, or let them override the schema. A
conflicting action relationship MUST NOT authorize.

### 4.4 Scope applicability

`validateRequest` is fixed to `true` in the D3 adapter. Callers cannot turn it
off. The following must be rejected before a decision can be returned:

- a principal type outside the action's `appliesTo`;
- a resource type outside the action's `appliesTo`;
- an action not declared by the schema;
- a context shape that does not conform to the action context;
- an entity that does not conform to the schema.

This is intentionally stricter than the calibrated upstream decision path.
The difference is documented as a D3 security property and must be covered in
the upstream differential corpus.

## 5. Decision and failure semantics

| Stage | Condition | Public result | Enforcement effect |
| --- | --- | --- | --- |
| Bundle construction | Lowering did not export a complete validated bundle; policy IDs/bodies are missing or duplicated; schema is absent or invalid; a dispatchable action lacks a complete `appliesTo`; bundle versions disagree. | `OperationFailure`, no decision, classified adapter/lowering error. | Deny/fail closed. Never evaluate an empty set. |
| Request construction | UIDs, context JSON, entity JSON, or immutable snapshot construction is malformed. | `OperationFailure`, no decision. | Deny/fail closed. |
| Schema/request validation | Scope, action, context, or entity does not conform. | `OperationFailure`, no decision, preserving CedarSharp diagnostics. | Deny/fail closed. |
| Cedar evaluation | Cedar completes with Deny and no errors. | `Deny`, clean Deny, determining set, no policy errors. | Deny. |
| Cedar evaluation | Cedar completes with Allow and no errors. | `Allow`, clean Allow, determining set. | Allow only for an enforcement caller that requires a clean Allow. |
| Cedar evaluation | Cedar returns Allow with one or more policy errors. | Raw Allow plus the error set; `IsCleanAllow` is false. | The D3 enforcement decision is Deny/fail closed. Diagnostic callers may inspect the raw Allow. |
| Cedar evaluation | Cedar returns Deny with policy errors. | Deny plus the error set; not a clean Deny. | Deny/fail closed. |
| Native/transport failure | Native load, ABI, transport, or response decoding fails. | `OperationFailure`, no decision. | Deny/fail closed. |
| Unknown diagnostic identity | Cedar names a policy ID not in the bundle. | `OperationFailure`, no usable decision. | Deny/fail closed; do not silently drop the ID. |

A raw Cedar decision and the D3 enforcement decision are separate concepts.
The adapter must expose both where needed, but the only success value that may
authorize is an error-free Allow.

## 6. Identity and diagnostic mapping

- The D2-generated Cedar ID is the transport identity.
- Each Cedar ID must map to exactly one D2 rule and source rule index.
- Determining and erroring IDs are compared and exposed as sets. Their order is
  not a contract.
- A source `@id` annotation is never treated as the stored generated ID.
- Unknown IDs are adapter failures.
- Diagnostic wording is informative but not compared as exact text across
  engine versions. Diagnostic category, attached policy ID, failure stage, and
  fail-closed effect are normative.
- CedarSharp operation errors and Cedar policy errors remain distinct. A
  schema/request validation failure is not represented as a policy evaluation
  error, and a policy error is not represented as a native transport failure.

## 7. Temporal and provider boundary

The D2 lowering's generated temporal context field is authoritative only when
produced by the trusted temporal pipeline.

- The field name is reserved by the lowering contract.
- Caller-supplied context containing the reserved field is rejected before
  dispatch.
- The trusted pipeline computes the temporal Boolean and binds it to the
  immutable request snapshot.
- Temporal evaluation failure returns no decision and fails closed.
- Request context and durable logged history remain separate upstream data
  sets. A caller cannot populate the logged set through the D3 request API.
- Provider values are similarly computed by the trusted provider boundary.
  Missing or failed provider evaluation fails closed and is never replaced by
  a caller value.
- No provider callback or lazy context source is introduced into CedarSharp.

D3-5 depends on a separately authorized temporal/provider commission. D3 does
not implement a durable event store, replay service, distributed coordinator,
or transaction/reservation mechanism.

## 8. Native coexistence and hosting

The D2 native bridge and the CedarSharp native image are independent native
components. A D3 process may load both, but:

- each image is verified by its own manifest and identity;
- ABI or hash mismatch rejects the operation before target dispatch;
- both successful images remain resident until process exit;
- there is no unload, hot replacement, collectible native handle, or promise
  that one image can be released while the other remains active;
- managed adapter disposal releases managed references only and never claims to
  unload a native image;
- process isolation is the replacement boundary for an untrusted or failed
  native host.

## 9. Concurrency and thread safety

The immutable lowered bundle, policy set, schema, and request snapshots may be
shared across threads. A `CedarEngine` is documented as safe to share, but D3
must still qualify concurrent calls before making that guarantee part of its
public contract.

The first qualification must cover:

- concurrent first use and native initialization;
- repeated calls on one adapter and on independent adapters;
- at least four worker threads;
- deterministic results independent of request completion order;
- no native buffer, request, or diagnostic-state sharing between calls;
- bounded cancellation behavior: a native call already started may not be
  interrupted.

Until that gate passes, the public contract must require caller serialization
or explicitly withhold a concurrency guarantee.

## 10. NativeAOT, trimming, and package consumption

The future package must be consumed from the package output alone. The
qualification consumer must not reference repository source paths, sibling
staging trees, or local build artifacts.

The package gate must prove:

- the D2 bridge and CedarSharp native assets are present for every supported
  RID and match their manifests;
- missing, mismatched, or wrong-RID assets fail before target dispatch;
- `PublishAot=true`, `TrimMode=full`, and trimming analyzers pass on every
  supported TFM/RID cell;
- the package-only consumer uses reflection-free JSON inputs and completes
  lower → authorize → clean-Allow enforcement;
- a RID-specific restore/publish does not mutate the canonical checkout's
  lockfile; publishing occurs in a disposable copy or the generated change is
  restored and the clean-tree check is recorded.

## 11. Version and requalification policy

The initial D3 candidate remains pinned to:

- upstream Dogwood `dogwood-language` 1.1.2 at
  `929f49e7305534ac61ef06cf8e47269c596dc851`;
- bridge Cedar 4.11.0;
- CedarSharp 1.0.0 / Cedar 4.13.0.

A change to Dogwood, the bridge, CedarSharp, Cedar, the schema export, the
policy-ID export, the entity-store mode, request-validation mode, temporal
pipeline, or native ABI requires a recorded impact analysis and the applicable
gates below. A version-pair change is not qualified by Cedar-to-Cedar results
alone; the upstream authorizer corpus must be rerun when entity, scope,
temporal, provider, or error semantics are affected.

## 12. Mandatory implementation and qualification gates

A future D3 implementation is complete only when all of these pass:

1. **Build and dependency gate** — locked restore, Release build, zero
   warnings, format/lint checks, and an unchanged dependency graph.
2. **Bundle gate** — complete per-rule export; exact body/ID pairing; canonical
   schema; no text-splitting identity path.
3. **Unit and integration gate** — adapter, request, entity, schema, error, and
   negative-control suites on both TFMs.
4. **Decision gate** — Allow, Deny, forbid precedence, default Deny, hierarchy,
   clean Allow, raw Allow with policy errors, and fail-closed cases.
5. **Scope gate** — principal, resource, and action outside schema constraints
   are rejected on every dispatch.
6. **Entity-store gate** — exact caller entities; no bare scope injection; no
   synthesized attributes, parents, or action relationships; conformance errors
   fail closed.
7. **Temporal/provider gate** — reserved-field rejection, trusted value
   provenance, missing/failed evaluation, and no caller substitution.
8. **Identity gate** — determining/erroring IDs map by identity; set semantics;
   unknown-ID rejection; source-annotation controls.
9. **Native lifecycle gate** — both images verified and resident; missing,
   mismatched, and incompatible assets rejected; no unload promise.
10. **Concurrency gate** — concurrent initialization and calls qualified under
    an explicit worker bound.
11. **NativeAOT/trimming gate** — every supported RID/TFM cell publishes and
    executes fail closed.
12. **Package-only gate** — a consumer uses only the produced package and its
    declared native assets.
13. **Regression gate** — existing D1 and D2 suites and the amended
    archived-R2 guard remain unchanged and green.
14. **Upstream differential gate** — the mandatory corpus in the
    [D3 qualification plan](d3-qualification-plan.md) runs against the pinned
    upstream authorizer and the D3 adapter. No unexplained difference is
    permitted; an approved expected difference must be encoded in the corpus.
15. **Hosted matrix gate** — the complete supported RID/TFM matrix passes.
16. **Evidence gate** — durable evidence is bound to the exact source revision,
    native identities, host, matrix scope, exact checks, cleanup, and explicit
    exclusions.
17. **Independent qualification and owner acceptance** — producer evidence is
    supporting material only. A green producer suite is not acceptance.
18. **Publication/release gate** — package publication, signing, tags, and
    release require a separate owner authorization after qualification.

## 13. Exclusions

This contract does not authorize or claim:

- a production adapter or public API;
- a change to CedarSharp or the upstream Dogwood repository;
- an unrestricted or schema-less fallback;
- upstream-equivalence before the upstream corpus passes;
- durable temporal storage, replay, transaction coordination, or distributed
  spending enforcement;
- package publication, signing, a release tag, or release;
- Linux/macOS qualification from Windows-only evidence;
- performance, throughput, memory-bound, or general unload guarantees.
