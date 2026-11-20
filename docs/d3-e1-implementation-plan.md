# DOGWOOD-SHARP-D3 binding implementation contract

Status: **binding for a future D3 implementation commission; implementation is
not authorized**.

This contract supersedes the earlier proposed D3 work packages. It combines
the [D3 integration contract](d3-integration-contract.md), the
[D3 qualification plan](d3-qualification-plan.md), and the accepted
[D3-E1 qualifications](d3-e1-acceptance.md). Normative terms **MUST**, **MUST
NOT**, **SHOULD**, and **MAY** are used as requirements.

No production API, native operation, dependency upgrade, package, tag, or
release is created by this document.

## 1. Objective and non-objectives

### 1.1 Objective

A future D3 implementation will provide an experimental, fail-closed
CedarSharp evaluation path for a complete validated D2 lowering bundle. It
will preserve Dogwood-generated policy identity, enforce schema and scope
constraints, expose raw and enforcement decisions separately, and operate
safely with the existing D2 native bridge in one process.

### 1.2 Non-objectives

D3 does not implement or claim:

- a durable temporal event store, replay service, distributed coordinator, or
  transactional spending/resource reservation;
- an information-provider service or network callback;
- an upstream Dogwood source modification or CedarSharp modification;
- schema-less authorization as a fallback;
- upstream-equivalence before the upstream corpus passes;
- a stable public API before independent qualification;
- package publication, signing, a release tag, or release.

## 2. Entry and stopping conditions

### 2.1 Entry conditions

A D3 implementation commission must verify and record:

1. D3-E1 remains accepted with its qualifications.
2. The archived-R2 guard remediation is delivered and the hosted producer
   matrix is green on `win-x64`, `linux-x64`, and `osx-arm64`.
3. The implementation names the supported RIDs and TFMs and authorizes the
   required dependency/native changes.
4. The implementation starts from a clean, synced `main` revision with exact
   source/tree identity.
5. The per-rule D2 export and the upstream differential corpus are included
   in the commissioned scope.

### 2.2 Mandatory stop points

The implementation MUST stop for review after:

- the per-rule export and policy-ID gate;
- the adapter core, request validation, and clean-Allow gate;
- the entity/scope/failure-semantics gate;
- native lifecycle, concurrency, AOT, and package-only consumption;
- the complete upstream and hosted qualification matrix.

A later package MUST NOT inherit an earlier package's evidence unless the
source and exact checks are re-bound to the later revision.

## 3. Version pins

The initial implementation candidate MUST remain pinned to:

| Component | Required pin |
| --- | --- |
| Upstream Dogwood | `dogwood-language` 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851` |
| Bridge Cedar | 4.11.0 |
| CedarSharp | 1.0.0 |
| CedarSharp Cedar | 4.13.0 |
| .NET TFMs | `net8.0`, `net10.0` |
| Native RIDs | `win-x64`, `linux-x64`, `osx-arm64` |
| Rust | the repository-pinned 1.99.1 toolchain |

A dependency update MUST NOT be bundled into an unrelated semantic change.
Every pin change requires the impact analysis and requalification policy in
section 15.

## 4. Future public API shape and ownership

The exact names remain provisional until the adapter semantics are qualified,
but the following ownership and behavior are binding.

### 4.1 Experimental surface

A future implementation SHOULD use an explicitly experimental namespace such
as `DogwoodSharp.Experimental.CedarSharp`. It MUST remain:

- outside the stable API surface;
- documented as experimental;
- excluded from the stable package until the qualification and owner gates
  pass, unless a later owner decision explicitly changes that rule;
- free to change before independent qualification, without compatibility
  promises.

The research prototype, splitter, upstream oracle, and qualification harness
MUST NOT graduate into the production namespace.

### 4.2 Plan / bundle object

A plan or evaluator object MUST own one complete immutable lowering bundle:

- generated Cedar policy IDs;
- the exact body paired with each ID;
- the canonical augmented schema JSON;
- policy/schema provenance and validation state;
- the pinned engine identities.

The plan MUST copy the bundle into immutable managed snapshots. It MUST NOT
retain mutable caller-provided collections, expose native handles, or allow a
caller to mutate policies/schema after construction.

Construction MUST fail when:

- the D2 lowering did not export all artifacts;
- a generated ID is missing or duplicated;
- a body is missing or cannot be paired exactly with one ID;
- the schema is absent, malformed, or not the schema of that lowering;
- the policy/schema provenance does not belong to one immutable bundle;
- a dispatchable action lacks a complete `appliesTo` object;
- a caller attempts to supply a conflicting action entity or relationship.

A failed construction MUST NOT create an empty policy set and continue.

### 4.3 Request ownership

A request snapshot contains:

- principal, action, and resource UIDs;
- Cedar-encoded context JSON;
- caller entities in Cedar entity JSON form.

The evaluator MUST copy request JSON and entity data into immutable snapshots
before dispatch. The caller MUST NOT be able to mutate an in-flight request.
Caller-owned data is not retained beyond the snapshot contract.

The public request surface MUST NOT expose a trusted temporal Boolean as an
ordinary caller field. A future temporal API MUST distinguish trusted pipeline
input from caller context and MUST enforce the reserved-field rule in section
9.

### 4.4 Result ownership

A result MUST contain:

- operation-failed state and stage;
- raw decision, if one exists;
- enforcement decision (only a clean Allow authorizes);
- determining policy/rule identities as a set;
- policy errors keyed by generated policy ID;
- operation errors and warnings with stable categories;
- optional raw diagnostic payload appropriate to the public surface.

The result owns copied diagnostics and MUST remain valid after the native call
returns. It MUST NOT expose a borrowed native buffer or native handle.

### 4.5 Disposal

Disposing a plan or result releases managed references only. It MUST NOT claim
to unload either native image. Process exit is the native lifetime boundary.

## 5. D3-1: per-rule export and policy identity

### 5.1 Required native export

The D2 bridge MUST gain a complete operation, or extend its lowering result,
that returns each lowered rule as one record containing:

- the exact generated Cedar policy ID;
- the exact Cedar policy body for that ID;
- the source rule index;
- existing source-text/provenance data.

The export MUST be all-or-nothing under the existing D2 response-size,
UTF-8, diagnostic, and identity discipline. It MUST NOT return a partial
mapping.

The existing concatenated `CedarPolicyText` MAY remain for compatibility, but
it MUST NOT be an identity-bearing input to the D3 adapter.

### 5.2 Pairing and identity

The adapter MUST build `CedarPolicySet.FromPolicies` from the per-rule export.
It MUST NOT:

- use `FromText` for a decision-bearing policy set;
- split concatenated text to recover IDs;
- use a source `@id` annotation as the stored ID;
- map a diagnostic ID by list position;
- silently ignore an unknown diagnostic ID.

Determining and erroring IDs MUST be exposed and compared as sets. The order
returned by Cedar is not stable and MUST NOT be a public ordering contract.
Source rule index and generated Cedar ID are both retained.

### 5.3 Gate

The D3-1 gate MUST prove:

- one, many, and zero-rule behavior;
- duplicate-ID rejection;
- escaped quotes, backslashes, semicolons, braces, and Unicode;
- exact body/ID pairing for every rule;
- source annotation controls;
- reconstruction or exact per-rule equality with the existing export;
- bounded response limits;
- no partial export on failure.

## 6. D3-2: adapter core, schema transport, and request validation

### 6.1 Canonical schema

The augmented schema JSON export is the canonical schema for authorization,
validation, caching, and equality. The text export MAY be retained for
diagnostics, but MUST NOT be used as a second cache key or identity value.

Every request MUST carry the canonical schema. The evaluator MUST verify that
policy set, schema, and lowering provenance belong to the same plan.

Canonicalization MUST preserve every dispatchable action's complete
`appliesTo` object from the D2 lowering, including its principal, resource,
and context constraints. It MUST NOT drop, replace, widen, narrow, or
synthesize an `appliesTo` object. A dispatchable action without a complete
`appliesTo` MUST fail bundle construction. A schema-only action-group
declaration is not dispatchable; a request targeting such an action MUST be
rejected. No schema-less fallback is permitted.

### 6.2 Fixed validation mode

The adapter MUST construct the request with `validateRequest=true` and MUST
NOT expose a caller option to disable it. A request outside the schema's
`appliesTo` constraints MUST be rejected before an authorization result is
returned.

The adapter MUST preserve Cedar extension encodings for `ipaddr`, `decimal`,
and `datetime`; it MUST NOT coerce them to strings.

### 6.3 Request validation

The implementation MUST validate the complete request using CedarSharp's
schema-bearing request path. It MUST distinguish:

- scope UID incompatibility;
- undeclared action;
- context schema mismatch;
- entity schema mismatch;
- malformed JSON/entity encoding.

A validation failure MUST produce an operation failure with no decision. It
MUST NOT be converted to a clean Deny while preserving the invalid request for
evaluation, and it MUST NOT trigger schema-less evaluation.

### 6.4 Gate

The D3-2 gate MUST cover both schema exports for parsing and the JSON export
for canonical behavior; exact preservation of every dispatchable action's
complete principal, resource, and context `appliesTo` constraints across a
multi-action schema; rejection of an action with omitted or replaced
`appliesTo`; rejection of a request targeting a schema-only action-group
declaration; request validation on the only supported path; missing required
context; invalid principal/resource/action types; invalid entity types and
attributes; extension values; malformed JSON; duplicate IDs; and unknown
diagnostic IDs.

## 7. D3-3: approved entity-store construction

The approved mode is the schema-carrying mode in the
[integration contract](d3-integration-contract.md#4-binding-request-and-entity-store-contract):

1. pass caller entities exactly as supplied;
2. attach the augmented schema;
3. validate scope, context, and entities;
4. let CedarSharp resolve action hierarchy defined by the immutable schema;
5. evaluate without adapter-side entity synthesis.

The adapter MUST NOT synthesize, infer, add, remove, or rewrite any
authorization-relevant entity, attribute, parent, or relationship. In
particular, it MUST NOT:

- inject a bare scope principal or resource;
- synthesize caller attributes;
- synthesize or rewrite parents;
- merge caller action entities to override schema hierarchy;
- derive action relationships from a different schema;
- silently drop a malformed entity or caller-supplied action entity;
- retry without the schema or without validation.

The immutable augmented schema is the sole source of action-group hierarchy.
CedarSharp's internal resolution of schema-defined action hierarchy is engine
behavior and is not adapter-created authorization data. Caller-supplied action
entities or relationships MUST be rejected before dispatch; they MUST NOT be
silently dropped, merged, or allowed to override the schema, and a conflict
MUST NOT authorize.

CedarSharp's schema-bearing entity conformance is the store boundary. A
caller-supplied incomplete entity MUST fail the operation closed.

The entity-store implementation MUST document and test ED-01 through ED-03.
The D3 qualification package MUST implement and test ED-01 through ED-05;
ED-04 and ED-05 are not entity-store differences and are covered by D3-2 and
D3-7. The implementation MUST NOT claim store equivalence for ED-01 through
ED-03.

## 8. D3-4: decision, clean-Allow, and error semantics

### 8.1 Raw and enforcement decisions

The adapter MUST expose the raw Cedar decision and a separate enforcement
decision.

Only an error-free Allow is an authorization success. A raw Allow with any
policy error MUST set `IsCleanAllow=false` and MUST NOT authorize an
enforcement caller.

### 8.2 Required failure mapping

| Condition | Required adapter result |
| --- | --- |
| Incomplete/mismatched lowering bundle | Operation failure; no decision. |
| Invalid/malformed request input | Operation failure; no decision. |
| Scope/context/entity validation failure | Operation failure; no decision. |
| Cedar clean Deny | Clean Deny. |
| Cedar clean Allow | Clean Allow. |
| Cedar Allow with policy errors | Raw Allow, not clean, no enforcement authorization. |
| Cedar Deny with policy errors | Deny with errors, not clean. |
| Native load/ABI/transport failure | Operation failure; no decision. |
| Unknown determining/erroring policy ID | Operation failure; no usable decision. |

### 8.3 Error categories

Policy evaluation errors, request/schema validation errors, lowering errors,
and native transport errors MUST remain distinct. Diagnostic wording MAY
change with an engine version and MUST NOT be the equality key. The attached
policy ID, category, stage, and fail-closed effect are normative.

### 8.4 Gate

The gate MUST include default Deny, direct Allow/Deny, forbid precedence,
multiple determining permits, unknown IDs, raw Allow with policy errors,
errored forbid, malformed policy set, context failure, entity failure, native
failure, and a valid-permit-beside-invalid-policy negative control that can
never yield authorization.

## 9. D3-5: trusted temporal and provider boundary

This package depends on a separate owner authorization for temporal/provider
execution. Until then it remains a contract and test design only.

### 9.1 Reserved temporal field

The D2-generated context field name MUST remain reserved. The adapter MUST:

- identify it from the trusted lowered/schema artifact, never from caller
  context;
- reject caller context containing the reserved field before dispatch;
- accept the temporal Boolean only from the trusted temporal pipeline;
- bind the trusted value to the immutable request snapshot;
- fail closed with no decision when temporal evaluation fails.

Caller context and durable logged history are separate. The public D3 request
MUST NOT provide a way to populate logged history.

### 9.2 Temporal normalization

The upstream differential MUST use explicit ordered history, explicit trusted
timestamps, and one monotonic timeline. The upstream temporal engine and the
D3 trusted pipeline MUST compute the same normalized Boolean before their
authorization results are compared.

### 9.3 Provider boundary

Provider values MUST be computed by the trusted provider boundary. Missing,
failed, or malformed provider evaluation MUST fail closed. There is no caller
fallback and no lazy provider callback in CedarSharp.

### 9.4 Gate

The gate MUST cover temporal true/false, missing/malformed history, temporal
failure, reserved-field injection, generated-field collision, provider
success/omission/failure, cross-request isolation, and proof that no public
request path can substitute caller data for a trusted binding.

## 10. D3-6: native coexistence, lifecycle, concurrency, AOT, and packaging

### 10.1 Native coexistence

The D2 bridge and CedarSharp native images MAY coexist in one process. Each
MUST be independently discovered and verified by manifest/hash/ABI identity.
An identity mismatch MUST reject the operation before target dispatch.

### 10.2 Process-lifetime hosting

Both successfully loaded native images MUST remain resident until process exit.
The implementation MUST NOT provide, imply, or test an unload/hot-replacement
promise. Native consumers MUST leave the image resident. Process isolation is
the recovery boundary.

### 10.3 Concurrency

Immutable plan and request snapshots MAY be shared. A shared `CedarEngine` MAY
be used only after the concurrency gate proves safe first initialization and
repeated evaluation. The gate MUST cover at least four workers, concurrent
first use, repeated calls, independent plans, deterministic results, and
buffer/diagnostic isolation.

A native call already started MUST NOT be represented as cancellable. The
public contract MUST state the supported concurrency model and worker bound.
Until qualification, callers MUST serialize or the API MUST withhold a
concurrency guarantee.

### 10.4 NativeAOT and trimming

Every supported RID/TFM cell MUST publish with `PublishAot=true` and
`TrimMode=full`, pass trimming analyzers, and execute lower → authorize →
clean-Allow enforcement. The consumer MUST use reflection-free JSON paths.
Missing or wrong-RID assets MUST fail closed before target dispatch.

### 10.5 Package-only consumption

A package-only consumer MUST use only the produced NuGet package and declared
native assets. It MUST NOT reference repository source, a sibling staging tree,
or an unpublished local artifact. The consumer MUST verify the package version,
native manifests, and hashes before execution.

### 10.6 Lockfile and checkout hygiene

RID-specific restore/publish MUST occur in a disposable copy, or any generated
lockfile change MUST be restored with an explicit clean-tree check. The
canonical checkout MUST remain clean after qualification.

### 10.7 Gate

The gate MUST pass missing/manifest/hash/ABI mismatch controls, concurrent and
repeated use, JIT and NativeAOT on every supported cell, package-only
consumption, no-unload inspection, and clean-tree verification.

## 11. D3-7: qualification corpus and matrix

The future corpus MUST satisfy the
[D3 qualification plan](d3-qualification-plan.md), including all mandatory
case families, expected-difference IDs, no skipped required case, the pinned
upstream authorizer, and the complete RID/TFM matrix.

The corpus MUST be built from D2-lowered fixtures and MUST assert that a
non-exported lowering is surfaced as an error rather than an empty evaluation.
It MUST include caller-supplied action-entity and conflicting-relationship
controls that are rejected before dispatch, and it MUST prove that every
dispatchable action's complete `appliesTo` object survives canonicalization.

A Cedar-to-Cedar differential is supporting evidence only. It cannot close
the upstream gate for entity, scope, temporal, provider, or error semantics.

The matrix MUST use the same fixture and expected-difference manifest on every
cell. An unavailable native cell is pending, never passed.

## 12. D3-8: independent qualification and owner acceptance

The producer MUST package source, exact pins, native identities, commands,
per-case results, expected differences, cleanup, and exclusions. The
independent qualifier MUST use independently authored fixtures and oracle
controls where practical.

A green producer suite is not acceptance. The owner MUST issue a separate
acceptance record after independent qualification. Package publication and
release require a further explicit authorization.

## 13. Framework and platform matrix

| Platform | RID | TFMs | Required behavior |
| --- | --- | --- | --- |
| Windows | `win-x64` | `net8.0`, `net10.0` | Full D3 and upstream corpus; JIT, AOT, package-only, concurrency. |
| Linux | `linux-x64` | `net8.0`, `net10.0` | Same semantic corpus; native residency and failure controls; JIT, AOT, package-only, concurrency. |
| macOS | `osx-arm64` | `net8.0`, `net10.0` | Same semantic corpus; native residency and failure controls; JIT, AOT, package-only, concurrency. |

A platform outside this table is unsupported until separately commissioned and
qualified. Windows-only evidence MUST NOT be presented as a cross-platform
qualification.

## 14. Mandatory implementation, qualification, and release gates

| Gate | Requirement | Blocking condition |
| --- | --- | --- |
| Source/baseline | Clean, synced, exact source/tree and dependency identity. | Unverifiable baseline or unexpected dependency drift. |
| Per-rule export | Complete ID/body pairs; no text-splitting identity path. | Partial/mispaired export. |
| Build | Locked restore, Release build, zero warnings, format/lint. | Warning, lock drift, or failed check. |
| Unit/integration | Full adapter and negative-control suites on both TFMs. | Failure or missing required case. |
| Schema/request | Canonical schema and fixed request validation. | Missing schema or fallback. |
| Scope | Principal/resource/action outside constraints rejected. | Any out-of-constraint Allow. |
| Entity store | Exact caller entities; no synthesis; conformance fails closed. | Silent entity/relationship change. |
| Decision/error | Raw and enforcement decisions separated; classified errors. | Raw Allow with errors authorizes. |
| Identity | Every diagnostic ID maps by identity; set semantics. | Unknown ID dropped or positional mapping. |
| Temporal/provider | Reserved-field rejection and trusted provenance. | Caller substitution or fallback. |
| Native lifecycle | Both images verified/resident; invalid assets rejected. | Unload promise or pre-dispatch dispatch. |
| Concurrency | Qualified initialization/calls under explicit bound. | Race, shared mutable state, nondeterminism. |
| NativeAOT/trimming | Every supported cell publishes and runs. | Missing/skipped cell. |
| Package-only | Consumer uses produced package/assets only. | Source-path or sibling-tree dependency. |
| Regression | Existing D1/D2 suites and guard remain green. | Any regression. |
| Upstream differential | Pinned upstream corpus passes; differences encoded. | Unexplained difference or missing case. |
| Hosted matrix | All three RIDs and both TFMs pass. | Failure, cancellation, or pending required cell. |
| Evidence | Durable artifact bound to source, host, native, matrix, checks, cleanup, exclusions. | Unbound or green-tests-only evidence. |
| Independent review | Independent fixtures/controls and disposition. | Producer evidence accepted without review. |
| Owner acceptance | Separate owner record. | Missing acceptance. |
| Publication/release | Separate package/signing/tag/release authorization. | Missing authorization. |

No required gate may be closed by a planned test, a skipped case, a mocked
provider, an upstream mock, a Cedar-to-Cedar result alone, or publish success
alone.

## 15. Upgrade and requalification policy

A change to any of the following requires impact analysis and the listed
requalification:

| Change | Required requalification |
| --- | --- |
| Dogwood revision or version | Full upstream corpus, lowering/export identity, entity/scope/error, temporal/provider where affected. |
| D2 bridge native ABI/operation | Full D1/D2 regression, native identity, D3 bundle/error gates, full matrix. |
| CedarSharp package or Cedar version | CedarSharp/upstream differential, policy-ID/schema/error gates, full matrix. |
| Per-rule export format | ID preservation, body pairing, upstream identity, full diagnostic corpus. |
| Canonical schema form | Schema round trip, entity/hierarchy, scope, full differential. |
| Entity-store mode | Complete entity/scope corpus and upstream expected-difference review. |
| Request-validation behavior | Scope, context, entity, fail-closed, upstream difference review. |
| Temporal/provider pipeline | Temporal/provider trust cases and upstream temporal/provider corpus. |
| Native loading/lifetime | Lifecycle, missing/invalid asset, concurrency, AOT, full matrix. |
| Public API/package shape | Package-only, AOT/trimming, compatibility and owner review. |

A version-pin change MUST be separately identified in source and evidence.
Unrelated changes MUST NOT be bundled with it.

## 16. Explicit exclusions

- No production adapter/API in this document.
- No CedarSharp or upstream source change.
- No dependency upgrade in this document.
- No schema-less fallback or caller-controlled authorization data.
- No full D3 implementation before a separate commission.
- No durable temporal/provider/distributed implementation.
- No package publication, tag, signing, or release.
- No cross-platform claim from a partial matrix.
- No performance, throughput, memory-bound, or general unload guarantee.
- No stable compatibility promise before independent qualification and owner
  acceptance.
