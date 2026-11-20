# DOGWOOD-SHARP-D3 qualification plan

Status: **binding plan for the future D3 qualification**. The small
DOGWOOD-SHARP-D3-PREP-01 calibration is complete, but it is not the D3
qualification and does not authorize an upstream-equivalence claim.

## 1. Pinned upstream identity and execution mechanism

| Item | Pinned value |
| --- | --- |
| Upstream repository | `https://github.com/dogwood-policy/dogwood` |
| Upstream revision | `929f49e7305534ac61ef06cf8e47269c596dc851` |
| Upstream crate | `dogwood-language` 1.1.2 |
| Upstream Cedar | `cedar-policy` / `cedar-policy-core` 4.11.0 |
| Rust toolchain for PREP calibration | 1.99.1, `x86_64-pc-windows-msvc` |
| D2 bridge pin | the same Dogwood revision and Cedar 4.11.0 |
| CedarSharp candidate | 1.0.0 / Cedar 4.13.0 |

The revision is pinned in the oracle's `Cargo.toml`, and the exact Git source
and resolved graph are recorded in
`docs/reviews/d3-prep-01/evidence/upstream-oracle/Cargo.lock`.

The upstream oracle is a small Rust test program that links the actual
`dogwood-language` crate. It drives its public production path:

1. `ServiceSchema` and `PolicySchema` are built from the fixture.
2. `LoweredPolicySet::from_str` or `from_many_str` lowers the Dogwood source.
3. `Authorizer::new` installs upstream's built-in Cedar policy engine and
   in-memory temporal engine.
4. `Event` is assembled from the same scope, context, and entities represented
   in the fixture.
5. `Authorizer::is_authorized` executes the request.
6. The response is normalized to decision, determining rule references,
   upstream errors, and pre-decision failure.

The oracle is not a mock, not a Cedar FFI wrapper, and not a reimplementation
of `build_entities`. Cedar-to-Cedar agreement therefore cannot substitute for
this oracle. The oracle is test tooling and is never referenced by
`DogwoodSharp.slnx`, a production project, or a package.

## 2. PREP-01 calibration status

### 2.1 What was executed

The calibration harness is under
`docs/reviews/d3-prep-01/evidence/probe/`. It executes one fixture set through:

- the D2 lowering plus CedarSharp 1.0.0, with the canonical augmented schema
  and `validateRequest=true`; and
- the upstream Dogwood authorizer oracle at the pinned revision.

Sixteen cases passed exact per-engine expectations on both `net8.0` and
`net10.0` on Windows `win-x64`: seven normalized matches and nine explicitly
encoded expected differences. The runner checks lowering export and rule IDs,
decision, operation-failed state, error presence, determining-ID set,
policy-error-ID set, categorized errors, clean-Allow state, and the exact
versioned difference-register observation for every expected-difference case.
The per-case observations are in `prep-results-net8.0.json` and
`prep-results-net10.0.json`; the reproduction script is `probe/run.ps1`.

The cases cover context Allow/Deny, entity-reference attributes, entity
hierarchy, schema-derived action hierarchy, `has` on an omitted scope
principal, an incomplete supplied scope entity, an absent scope entity with a
required-attribute read, entity schema violation, principal/resource/action
outside schema constraints, a raw Allow carrying a policy error, a missing
context attribute, and malformed policy input.

### 2.2 What the calibration establishes

- The upstream oracle is viable on the local host and can be built offline
  from the pinned Cargo lock.
- The two engines can be driven from one fixture representation.
- The selected schema-carrying mode agrees with upstream on the basic decision,
  hierarchy, and raw-Allow-with-error decision families. The diagnostic
  category of the failing scope-attribute policy remains an explicit
  difference.
- The calibration compares determining and erroring identities as sets, but it
  pairs generated IDs with split concatenated policy bodies by position. Those
  identity observations are calibration-only and do not qualify Q1-01,
  Q1-02, QD3-04, or QD3-05.
- The upstream decision path can allow a principal or resource outside
  `appliesTo`; the schema-carrying path rejects it. This is an explicit
  expected difference and a D3 security property.
- Upstream and CedarSharp reject the same malformed/incomplete inputs but can
  expose them at different stages: upstream commonly returns a fail-closed
  Deny with diagnostics, while CedarSharp can return an operation failure with
  no decision.
- An absent scope principal is bare-present upstream and absent in the
  selected mode. `has` agrees; an attribute read can differ in diagnostic mode
  while remaining fail closed.

### 2.3 What the calibration does not establish

- No full-equivalence claim.
- No temporal-history, provider, or reserved-field injection case was
  executed.
- No Linux or macOS execution.
- No production adapter exists.
- The PREP managed probe reconstructs rule bodies from the current
  concatenated D2 text export for its bounded corpus. That calibration-only
  path does not qualify the per-rule ID/body pairing required by D3-1.
- No package-only consumption or NativeAOT D3 consumer exists.
- No concurrency, performance, or memory qualification exists.
- The PREP error-category taxonomy is harness-specific and does not yet
  cover the final cross-engine stage taxonomy for every mandatory failure
  family. The mandatory corpus must own and version that normalization.
- The PREP fixture set is not the upstream project's own conformance corpus.

The upstream-corpus gate in section 8 remains pending.

## 3. Fixture format and reproducibility

### 3.1 Common fixture

A qualification fixture is one JSON object with these fields:

```json
{
  "id": "stable-case-id",
  "question": "the behavior being tested",
  "source": "Dogwood policy source",
  "schema": "name of a schema in the fixture schema table",
  "distincter": "q1",
  "event": {
    "actionNamespace": ["App", "Action"],
    "actionId": "read",
    "principal": { "type": "App::User", "id": "alice" },
    "resource": { "type": "App::Doc", "id": "d1" },
    "entities": [],
    "context": { "count": 5 }
  },
  "expectation": {
    "upstream": {
      "failed": false,
      "decision": "allow",
      "hasErrors": false,
      "determiningRules": [
        { "ruleIndex": 0, "cedarPolicyId": "q1_0" }
      ],
      "policyErrorIds": [],
      "errorCategories": []
    },
    "cedarSharp": {
      "decision": "allow",
      "hasErrors": false,
      "operationFailed": false,
      "determiningIds": ["q1_0"],
      "policyErrorIds": [],
      "errorCategories": [],
      "isCleanAllow": true
    },
    "loweringArtifactsExported": true,
    "loweringExportedRuleIds": ["q1_0"],
    "agreement": "match"
  }
}
```

Entities use Cedar entity JSON. Each entity has a `uid`, `attrs`, and direct
`parents`; an entity-reference attribute uses `{"__entity":{"type":...,"id":...}}`.
Context uses the Cedar JSON encoding, including the extension wrappers for
`ipaddr`, `decimal`, and `datetime`.

An expected-difference case names one or more `differenceIds`. Each ID must
resolve to exactly one row in the versioned difference register, and that row
must contain the exact per-case observations and fail-closed effect. A
nonempty ID list by itself is never a passing condition.

The fixture file is the single source for both engines. The oracle does not
receive a precomputed Cedar decision, and the managed path does not receive an
upstream decision. Expectations are reviewed against the authored fixture and
the upstream source before the final evidence run; output from either engine is
not copied into an expectation after the fact.

### 3.2 Entity-store construction under test

The fixture's `entities` array contains only caller-supplied attributed
entities and their direct parents.

The upstream oracle then exercises the actual three-tier construction:

1. schema-check caller entities;
2. add bare omitted scope entities;
3. merge schema action entities and edges.

The selected D3 adapter contract passes the same caller entities unchanged,
attaches the schema, validates the request, and lets CedarSharp derive action
hierarchy. It must not add bare scope entities or synthesize attributes,
parents, or relationships.

The differential records both the final store mode and the result so a fixture
can distinguish:

- omitted scope entity;
- explicitly supplied attribute-less entity;
- explicitly supplied but schema-incomplete entity;
- caller-supplied parent edge;
- schema-derived action hierarchy;
- caller attempt to supply a conflicting action relationship.

### 3.3 Temporal fixture extension

A temporal qualification case extends the common fixture with an ordered
`temporalEvents` array. Each history event contains the upstream event schema
fields, event kind, action, scope, request context, logged context, timestamp,
and stable event identity. The array is replayed into one upstream
`Authorizer` in the stated order; only the final `request` event is a decision
point.

Normalization rules:

1. Use one trusted monotonic timeline derived from the fixture's explicit
   timestamps; never use wall-clock time.
2. Preserve the exact event order and reject duplicate or non-monotonic event
   identity.
3. Compute the temporal binding through upstream's trusted temporal engine for
   the upstream side.
4. Compute the same binding through the future trusted D3 temporal pipeline for
   the adapter side.
5. Compare the normalized Boolean binding before comparing the authorization
   result.
6. The D2-generated context field name comes from the augmented schema/export;
   it is never copied from a caller fixture into a public request.
7. A caller-supplied value for the reserved generated field is a separate
   mandatory rejection case, not an alternate way to supply the binding.

A temporal case is not qualified if the two sides use different history,
timestamps, event kinds, or binding provenance.

### 3.4 Provider fixture extension

A provider case declares the upstream provider schema and a deterministic
resolver outcome: a value, an omitted value, or a runtime failure. The same
normalized provider value or failure is supplied to the future trusted D3
provider boundary. Provider implementation text and side effects are not
part of the authorization differential; the bound input and outcome are.

## 4. Identity and attribution rules

The comparison uses identity, never list position:

- upstream determining results are compared as the set of
  `(rule_index, cedar_policy_id)` pairs;
- CedarSharp determining results are compared as the set of generated policy
  IDs;
- policy-error IDs are compared as sets;
- duplicate IDs, an unknown ID, or a cardinality mismatch fail the case;
- source rule annotations are never accepted as stored IDs;
- rule-index order is not a semantic order;
- diagnostic wording may differ and is retained as evidence but is not an
  equality criterion.

The future D3 adapter must map CedarSharp IDs back to the original D2 rule
indices using the per-rule export. The qualification must include non-
sequential IDs, reordered supply, duplicate-ID rejection, and an injected
unknown ID.

## 5. Decision and error comparison

Each case produces a normalized result with these fields:

| Field | Meaning |
| --- | --- |
| `stage` | `lowering`, `request-validation`, `evaluation`, or `native-transport`. |
| `operationFailed` | No usable authorization result was produced. |
| `decision` | `allow`, `deny`, or null. |
| `determining` | Set of policy/rule identities. |
| `policyErrors` | Set of policy IDs with error-presence/category observations. |
| `operationErrors` | Presence and category of pre-decision or transport errors. |
| `cleanAllow` | True only for an error-free Allow. |
| `expectedDifference` | The fixture's approved difference ID, if any. |

The default equality rule is strict on stage, operation-failed state, decision,
determining identity set, and error presence. Wording is not compared exactly.
An expected difference must state:

- the exact upstream and D3 observations;
- why the difference exists in source semantics;
- the fail-closed/security effect;
- the API contract that exposes it;
- the regression test that pins it.

An unexplained difference fails the qualification. An expected difference MUST
NOT conceal an error-free upstream Allow paired with a D3 rejection unless the
D3 rejection is an explicit owner-approved stricter security contract. ED-04
is the sole such difference in this plan. Every other Allow-versus-rejection
difference fails qualification.

Every `differenceIds` value MUST resolve to exactly one versioned
difference-register row. The mandatory runner MUST validate that row's
expected upstream observation, D3 observation, stage, error category, and
fail-closed effect. A nonempty ID list alone MUST NOT pass an
expected-difference case.

## 6. Mandatory case families

Every required family must have at least one positive control, one negative
control, and relevant malformed/operation-failure controls. A missing or
skipped required case blocks qualification.

| ID | Mandatory family | Required observations |
| --- | --- | --- |
| C-01 | Direct Allow | Error-free Allow, correct determining ID, clean Allow. |
| C-02 | Direct/default Deny | Error-free Deny, default deny when no permit applies. |
| C-03 | Forbid precedence | A satisfied forbid wins over permit; determining identity is the forbid. |
| C-04 | Multiple permits | All applicable determining IDs are preserved as a set; order is not assumed. |
| C-05 | Entity attributes | Required and optional attributes, entity references, escaped/Unicode IDs, missing referenced entity. |
| C-06 | Entity hierarchy | Caller-supplied direct parent, transitive membership, missing parent, wrong parent type. |
| C-07 | Action hierarchy | Schema-derived action membership; schema-carrying versus explicit action entity control; caller conflict attempt. |
| C-08 | Missing entities and attributes | Omitted scope entity, explicit attribute-less entity, explicit incomplete entity, missing referenced entity. |
| C-09 | Bare scope entities | `has`, identity comparison, required-attribute read, and explicit upstream bare-tier versus D3 absent-mode difference. |
| C-10 | Schema violations | Wrong attribute type, missing required attribute, unknown entity type, invalid parent, malformed entity JSON. |
| C-11 | Scope applicability | Principal outside `appliesTo`, resource outside `appliesTo`, action not declared, declared-but-inapplicable types, a multi-action schema with different complete `appliesTo` constraints, exact canonical-schema comparison proving every principal/resource/context constraint survives, an action-group member and schema-only group-declaration case, and an action whose `appliesTo` would be omitted or replaced. |
| C-12 | Temporal binding true | Trusted history produces true; both sides authorize identically and attribute the same rule. |
| C-13 | Temporal binding false | Trusted history produces false; both sides deny cleanly. |
| C-14 | Temporal failure | Missing/malformed history, temporal evaluation failure, and non-decision event handling fail closed or produce no decision as contracted. |
| C-15 | Reserved-field injection | Caller context containing the D2-generated temporal field is rejected before dispatch; the trusted value is not replaceable. |
| C-16 | Provider value | Deterministic provider value is used identically; provider field ownership remains internal. |
| C-17 | Provider failure | Missing or failed provider evaluation fails closed and never falls back to caller data. |
| C-18 | Raw Allow with policy errors | A valid permit beside a failing policy yields a raw Allow with an error; enforcement is not a clean Allow. |
| C-19 | Errored forbid | An errored forbid does not silently become a clean Deny; the error and identity are preserved. |
| C-20 | Malformed policy/input | Parse, lowering, schema, context, entity, request, and native-operation failures are classified without an Allow. |
| C-21 | ID preservation | Non-sequential IDs, reordered supply, source `@id` annotation, duplicate ID, unknown diagnostic ID. |
| C-22 | Schema transport | JSON canonical form, text diagnostic form, both parse, only JSON is the canonical identity/cache value. |
| C-23 | Extension values | `ipaddr`, `decimal`, and `datetime` valid values agree; invalid encodings fail closed. |
| C-24 | Unicode and escaping | Escaped IDs, Unicode IDs, semicolons/braces in strings, and bounded payloads are compared by identity and result. |
| C-25 | Concurrency | Repeated and concurrent requests have the same normalized result and no shared mutable state. |
| C-26 | Native/lifecycle | Missing, mismatched, wrong-RID, and incompatible native assets fail before target dispatch. |

The 16-case PREP corpus is a calibration probe, not mandatory-family
coverage. It partially probes C-01/C-02, C-05 through C-11, C-18, and C-20.
It contains no C-03 forbid-precedence case, no C-04
multiple-determining-permit case, no C-21 ID-preservation controls, and no
C-22 two-form schema controls, and it omits required controls from the
partially probed families. It therefore satisfies no complete
mandatory-family gate and does not satisfy the temporal, provider,
concurrency, lifecycle, or full matrix gates.

## 7. Difference register

The PREP calibration produced these bounded differences. The executable
versioned register is `probe/differences.json`; the runner verifies each
listed case's exact decision, error-presence, stage, error-category, and
fail-closed observation. Each row must become an explicitly encoded expected
difference in the mandatory corpus.

| Difference ID | Agreement class | PREP cases | Upstream observation | Selected D3 observation | Basis and required contract |
| --- | --- | --- | --- | --- | --- |
| ED-01 | Expected difference | P1-08, P1-11 | An omitted scope entity is added bare. | The entity remains absent. | Upstream `build_entities` tier 2. Identity and `has` usually agree; attribute reads can differ. D3 must not synthesize a bare entity. |
| ED-02 | Expected difference | P1-07, P1-09 | An incomplete or invalid supplied entity produces a fail-closed Deny with diagnostics. | Schema validation produces an operation failure with no decision. | Both reject; failure stage differs. D3 exposes validation separately from evaluation. |
| ED-03 | Expected difference | P1-08, P1-11 | A missing required attribute on an absent scope entity is reported as a missing attribute on the upstream bare entity. | The entity is reported as absent. | The failing policy does not authorize; diagnostic category is normative and wording is not. |
| ED-04 | Expected difference | P1-10a–P1-10d | The upstream decision path can allow a principal or resource outside `appliesTo`; an undeclared action is only denied by policy scope. | Request validation rejects the out-of-`appliesTo` scope or undeclared action. | Upstream does not perform the D3 scope-conformance check. D3 is intentionally stricter. |
| ED-05 | Expected difference | P1-12 | Missing required context can surface as a policy-evaluation error. | Schema context parsing can produce an operation failure. | Both fail closed; stage differs. |

P1-13 is a normalized match: both paths reject malformed policy input before
an authorization decision. It is not an expected difference and has no
difference ID.

No expected difference permits a schema-less fallback, caller-controlled
authorization data, or an error-free Allow on one side paired with rejection
on the other without an explicit owner-approved contract. ED-04 is the sole
such stricter difference in this plan.

## 8. Mandatory upstream corpus gate

The upstream authorizer corpus must be executed before D3 can claim upstream
compatibility or be recommended for owner acceptance. The gate requires:

1. every mandatory family in section 6;
2. both positive and negative controls;
3. all approved expected differences encoded by ID;
4. no skipped required case;
5. the upstream corpus built from the pinned Cargo lock;
6. exact source, native, host, RID, TFM, and command identity;
7. cleanup evidence and explicit exclusions.

The upstream project's own corpus may be added as supporting evidence, but it
does not replace the D3 corpus and cannot be used to weaken a required D3
case.

## 9. Matrix and execution order

The minimum D3 matrix is:

| OS | RID | TFMs |
| --- | --- | --- |
| Windows | `win-x64` | `net8.0`, `net10.0` |
| Linux | `linux-x64` | `net8.0`, `net10.0` |
| macOS | `osx-arm64` | `net8.0`, `net10.0` |

The upstream oracle and the D3 adapter must be built from their locked graphs
for each cell. The same fixture and expected-difference manifest must be used
on every cell. A cell that cannot be run is **pending**, never passed.

Recommended order:

1. Restore/build and identity checks.
2. Static semantic corpus and upstream calibration.
3. Temporal/provider corpus.
4. Identity, error, and lifecycle negatives.
5. Concurrency.
6. NativeAOT/trimming and package-only consumption.
7. Hosted RID/TFM matrix.
8. Independent qualification.
9. Separate owner acceptance and release decision.

## 10. Reproduction commands

The PREP calibration can be reproduced after the repository native gate has
produced the verified bridge asset:

```powershell
$env:Path = "$env:USERPROFILE\.cargo\bin;$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"
& "$env:LOCALAPPDATA\PowerShell\pwsh.exe" -NoLogo -NoProfile -NonInteractive `
  -File docs\reviews\d3-prep-01\evidence\probe\run.ps1
```

The script builds the upstream oracle with the pinned lock, restores/builds the
managed probe, stages the verified D2 bridge beside CedarSharp's native
asset, and runs both TFMs. It fails if any case or TFM fails.

The Rust-only format and lint checks are:

```powershell
cargo fmt --manifest-path docs\reviews\d3-prep-01\evidence\upstream-oracle\Cargo.toml --check
cargo clippy --locked --release --offline --all-targets `
  --manifest-path docs\reviews\d3-prep-01\evidence\upstream-oracle\Cargo.toml -- -D warnings
```

The mandatory D3 corpus will have its own immutable revision, runner,
expected-difference manifest, and evidence package. The PREP harness is not
silently extended into a production or release gate.

## 11. Evidence and cleanup requirements

For every future qualification run, record:

- exact source revision and tree;
- branch/worktree identity and clean-tree state;
- upstream revision and Cargo lock hash;
- D2 bridge and CedarSharp native hashes/manifests;
- host OS, architecture, Rust, .NET, PowerShell, and Git identities;
- RID and TFM for every cell;
- exact commands and case IDs;
- normalized per-case results and expected-difference IDs;
- required cases run, passed, failed, and pending;
- disposable fixture, temporary output, and native-build cleanup;
- explicit exclusions and any unavailable native qualification.

Missing/skipped required cases and unavailable native cells block completion.
A green local suite, a Cedar-to-Cedar match, or a successful publish is not
owner acceptance.

## 12. Current oracle readiness

| Capability | Status | Qualification consequence |
| --- | --- | --- |
| Build pinned upstream crate offline | Ready | Proven locally with Cargo 1.99.1. |
| Execute static upstream decisions | Ready | 16-case PREP calibration passed on two TFMs. |
| Preserve determining rule identity | Calibration only | Upstream `(rule_index, cedar_policy_id)` is captured and the bounded corpus compares ID sets, but the calibration-only positional body/ID pairing does not qualify production identity. D3-1 must provide exact per-rule export and `FromPolicies`. |
| Capture upstream fail-closed errors | Ready | Error strings/categories are captured for the static corpus. |
| Replay temporal history | Pending | Must add ordered history events and trusted timestamp normalization. |
| Exercise provider success/failure | Pending | Must add a deterministic resolver fixture. |
| Execute Linux/macOS cells | Pending | Hosted matrix gate remains. |
| Run the upstream project's full corpus | Pending | Mandatory supporting corpus gate remains. |

This readiness statement authorizes no D3 implementation. It identifies what
the PREP oracle proved and what the future D3 qualification must still build.
