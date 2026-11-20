# DOGWOOD-SHARP-D3-E1 compatibility report

**B. COMPATIBLE WITH QUALIFICATIONS**

CedarSharp 1.0.0 (Cedar 4.13.0) can evaluate validated Dogwood lowering output
produced by the D2 bridge (Cedar 4.11.0) without a CedarSharp change, within the
bounded scope recorded here. The executed corpus found no semantic difference.
The qualifications are adapter responsibilities, not observed engine defects.

Decision scale used in this report:

| Decision | Meaning |
| --- | --- |
| A | Compatible as-is. Existing public inputs suffice with no adapter-side semantic work. |
| **B** | **Compatible with qualifications.** Adapter-side work is required for identity, schema/request transport, entity-store construction, temporal trust and lifecycle. No CedarSharp source change is required for the executed scope. |
| C | Not compatible within bounded work. A CedarSharp change or a different execution backend is required. |

This delivery is feasibility evidence only. It adds no production API, no
package and no public guarantee. It does not authorize full D3 implementation.

## 1. Scope and baseline

The commission asked for an investigation of CedarSharp interoperability, a
bounded executable prototype with focused tests, a differential corpus against
the pinned Cedar version, an entity/request export study, one temporal wiring
experiment and a callback/lifecycle assessment.

| Item | Identity |
| --- | --- |
| Starting `main` | `28073db77f355ef81d8689354181b285fe9bdb64` |
| Starting tree | `f385b8e880d0bf0be8e406af0a8a2d43a7f02be8` |
| Upstream Dogwood | 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851` |
| Dogwood bridge | 0.1.0, ABI 1, `win-x64` |
| CedarSharp | 1.0.0, packaged commit `a25181d17603381e42adde60613f7a49d5309a2b` |
| Cedar in the bridge | 4.11.0 |
| Cedar in CedarSharp | 4.13.0 |
| Cedar language version | 4.5 on both engines |
| Enabled extension features | `datetime`, `decimal`, `ipaddr` on both engines |
| D1 qualification tree | `4cd202db3b567c9893b00ba947acd802fe362456` |
| D2 qualification tree | `74870265e31fbe7f5633962174c227159b3ddc87` |

Preserved boundaries:

- CedarSharp was not modified. No CedarSharp repository, package or lock was
  changed.
- No production source, public API, dependency version, workflow or package
  metadata was changed. The research projects are outside `DogwoodSharp.slnx`
  and are not packable.
- The D1 and D2 qualification trees and evidence are unchanged.
- Temporal evaluation, event storage, providers, D4/D5, Hufu integration,
  NuGet publishing, tags and releases are out of scope.
- Execution was on Windows `win-x64` only. Linux and macOS are not qualified
  by this record.

## 2. Deliverables and method

| Artifact | Path | Purpose |
| --- | --- | --- |
| Executable prototype | `research/CedarSharp.E1Prototype/` | Corpus, packaging, differential, ID, schema, entity, temporal and negative tests. |
| Cedar 4.11 oracle | `native/dogwoodsharp-e1-ref/` | Calls Cedar 4.11.0's own FFI with the same JSON envelopes CedarSharp receives. Test-only. |
| NativeAOT consumer | `research/CedarSharp.E1AotConsumer/` | Publishes and runs both native images in one trimmed AOT process on both TFMs. |
| Verification record | [d3-e1-verification.md](d3-e1-verification.md) | Commands, environment identity, hashes, observed counts, cleanup and exclusions. |

The reference oracle is deliberately not a reimplementation. It forwards
`is_authorized_json_str`, `validate_json_str`, `check_parse_policy_set_json_str`,
`check_parse_schema_json_str`, `check_parse_entities_json_str` and
`check_parse_context_json_str` to `cedar-policy = 4.11.0` with the same features
as the bridge. CedarSharp receives the same policy text/ID map, schema,
entities, context and request flags. The comparison therefore isolates the
4.13.0-versus-4.11.0 version pair rather than a wrapper's interpretation.

Each reference call is a separate process. This avoids sharing a Cedar runtime
with either engine and keeps the oracle outside every production path.

## 3. E1-1 API matrix

| Requirement | DogwoodSharp export | CedarSharp input | Observed result | Adapter qualification |
| --- | --- | --- | --- | --- |
| Policy set text | `DogwoodLoweredPolicySet.CedarPolicyText` | `CedarPolicySet.FromText` | Parses, but IDs become positional `policy0..policyN`. **Not usable for identity.** | Use an ID-to-text map. |
| Policy set with IDs | `Lowered.Rules[].CedarPolicyId` plus per-rule text | `CedarPolicySet.FromPolicies(IEnumerable<KeyValuePair<string,string>>)` / dictionary | Every exported ID retained; determining IDs and policy errors use them. | Required path. Pair bodies to IDs without relying on position. |
| Policy bodies | Concatenated display text | Per-policy text | A lossless string/depth-aware splitter recovers 1..N bodies and reconstructs the input exactly. | Prototype-only. Replace with a per-rule export before production. |
| Schema, JSON | `CedarSchemaJson` | `CedarSchema.FromJson` | Parses and validates policies; both engines agree. | Required. Schema must travel with each request for action hierarchy. |
| Schema, text | `CedarSchemaText` | `CedarSchema.FromText` | Parses; validation results equal the JSON form on all 30 cases. | Either form; keep both round-trippable. |
| Request principal/action/resource | Caller data | `CedarEntityUid` | Accepted. | Adapter maps its own identifiers. |
| Context | Caller/provider data with Cedar JSON encodings | `contextJson` | Scalars, nested records, `ipaddr`, `decimal` and `datetime` round trip. | Preserve Cedar's `__extn` encoding; do not stringify extension values. |
| Entities | Caller data | `entitiesJson` / `CedarEntity.ToJson` | Attributes, `__entity` references and parents round trip. | Preserve exact entity set and hierarchy. |
| Action hierarchy | Augmented schema `memberOf` edges | Schema-derived action entities | Membership resolves from the schema alone; merging the same entities into the payload is redundant. | Always pass the schema; do not depend on transporting action entities. |
| Validation | `Lowered.Validation` already gates export | `ValidatePolicies`, `CheckSchema` | Validated artifacts revalidate identically; the schema's presence is what validates request context. | Keep D2 validation as the gate. Do not re-interpret raw success. |
| Authorization decision | N/A | `CedarEngine.Authorize` | Decision, determining IDs and policy errors are exposed. | Map IDs by identity to Dogwood rules. |
| Clean Allow | N/A | `IsCleanAllow`, `PolicyErrors`, `Errors` | Raw Allow can carry policy errors; it is not a clean Allow. | Enforce clean-Allow or explicit fail-closed policy at the adapter boundary. |
| Request validation | N/A | `validateRequest` | On: invalid principal type fails the call. Off: the same request evaluates. | The flag is not a security bypass; schema context parsing fails regardless. |
| Diagnostics | Rule/source mapping | Raw response plus typed errors | Policy IDs, per-policy messages and operation errors are available. | Preserve IDs and classifications; do not compare wording only. |
| ABI and ownership | D2 bridge | CedarSharp ABI 1 | Both native images load and serve in one process. | Keep both resident for process lifetime. |
| AOT/trimming | D2 NativeAOT consumer precedent | CedarSharp trim/AOT annotations | A full-trim NativeAOT host publishes and runs on both TFMs with both natives. | Package shape is viable; no arbitrary-object overloads used. |
| Callback seam | N/A | None | CedarSharp has no callback that supplies entities/context during evaluation. | Materialize a complete immutable request before the call. |

## 4. E1-2 version pair

| Component | Version | Identity |
| --- | --- | --- |
| Dogwood | 1.1.2 | `929f49e7305534ac61ef06cf8e47269c596dc851` |
| Dogwood bridge | 0.1.0 | ABI 1; SHA-256 `8ABF40D2526EAB650003E7EE1C05D97AA82CBEB69890849E867333EA54914D63` |
| Bridge Cedar | 4.11.0 | Same `datetime`, `decimal`, `ipaddr` features |
| CedarSharp | 1.0.0 | Native manifest SHA-256 `77DE6976A7AE5B961920BB6843D39B32F3FAF285F16B723581680E9C06C03895`; native source commit `324D3C09FC94AB91464EC340EB81E4B167DEB6F5` |
| CedarSharp Cedar | 4.13.0 | Language 4.5; ABI 1; same three extension features |
| E1 reference | cedar-policy 4.11.0 | SHA-256 `D617B02A12C511A67ADBB03F1F0388D19926664D5C4EAF916500AFF431E7EBB8`; reports SDK 4.11.0 / language 4.5 |

The two SDK patch/minor versions differ. The executed evidence covers the
language and feature surface exercised by the corpus, not the whole Cedar
language or every SDK behavior between 4.11 and 4.13. The version pair remains a
qualified pair, not an equivalence claim.

## 5. E1-3 executable prototype

The prototype flow is:

1. Parse and validate Dogwood source against a `DogwoodSchema`.
2. Lower with `Dogwood.Lower` and require a complete `ArtifactsExported` result.
3. Split the exported Cedar text into exact per-rule bodies.
4. Pair each body with `Lowered.Rules[].CedarPolicyId` and build
   `CedarPolicySet.FromPolicies`.
5. Attach the exported augmented schema, context and entities to a
   `CedarAuthorizationRequest`.
6. Authorize through `CedarEngine`, then map determining and erroring IDs back
   through `E1Bundle.RuleIndexFor`.
7. Compare the normalized semantic result with the Cedar 4.11 oracle.

The prototype rejects an incomplete export instead of forwarding empty
artifacts. It also checks that CedarSharp returned exactly the exported ID set
before any authorization call.

## 6. E1-4 differential corpus

Thirty cases cover the required families: direct permit and default deny;
matching forbid and forbid precedence; multiple determining permits; twelve
distinct IDs; namespaced entity attributes; escaped and Unicode IDs; action
hierarchy with and without schema and with merged action entities; entity
parents; optional attributes; skip-on-error; required-attribute errors; raw
Allow with policy errors; nested context records; all three extension types;
request-validation on/off; missing schema-required context; bare scope entities;
and caller-supplied non-conforming entities.

The comparison is semantic, not textual. It compares success/operation-failure,
raw decision, the determining ID set, per-policy error counts keyed by policy
ID, operation-error presence and warning counts. Diagnostic wording is printed
but not normalized. Validation outcome and entity-parse outcome are also
compared per case.

**All 30 cases agreed exactly between CedarSharp 4.13 and Cedar 4.11.**

| # | Case | Hand expectation | Observed in both engines |
| ---: | --- | --- | --- |
| 1 | `permit-direct` | Allow | `allow`, reasons `e1_0`, no errors |
| 2 | `default-deny` | Deny | `deny`, no reasons, no errors |
| 3 | `matching-forbid` | Deny | `deny`, reasons `e1_0`, no errors |
| 4 | `permit-forbid` | Deny | `deny`, reasons `e1_1` (forbid), no errors |
| 5 | `multiple-permits` | Allow | `allow`, reasons `e1_0,e1_1`, no errors |
| 6 | `distinct-ids-12` | Allow | `allow`, reasons `e1_0..e1_4`, no errors |
| 7 | `namespaced-entity-match` | Allow | `allow`, reasons `e1_0`, no errors |
| 8 | `namespaced-entity-mismatch` | Deny | `deny`, no reasons, no errors |
| 9 | `escaped-ids` | Allow | `allow`, reasons `e1_1`, no errors; one validation warning on both schema forms and both engines |
| 10 | `action-hierarchy-with-schema` | Allow | `allow`, reasons `e1_0`, no errors |
| 11 | `action-hierarchy-without-schema` | Deny | `deny`, no reasons, no errors |
| 12 | `action-hierarchy-with-merged-entities` | Allow | `allow`, reasons `e1_0`, no errors; same as case 10 |
| 13 | `entity-parents-match` | Allow | `allow`, reasons `e1_0`, no errors |
| 14 | `entity-parents-miss` | Deny | `deny`, no reasons, no errors |
| 15 | `optional-secret-forbid` | Deny | `deny`, reasons `e1_0`, no errors |
| 16 | `optional-secret-absent` | Allow | `allow`, reasons `e1_1`, no errors |
| 17 | `forbid-error-skipped` | Allow with forbid error | `allow`, reasons `e1_1`; error `e1_0`: entity `App::Session::"s1"` does not exist; not a clean Allow |
| 18 | `missing-attribute-error` | Deny with policy error | `deny`, no reasons; error `e1_0`: entity `App::Session::"s1"` does not exist |
| 19 | `allow-with-policy-errors` | Allow with policy error | `allow`, reasons `e1_0`; error `e1_1`: entity `App::Session::"s1"` does not exist; not a clean Allow |
| 20 | `context-record` | Allow | `allow`, reasons `e1_0`, no errors |
| 21 | `context-record-false` | Deny | `deny`, no reasons, no errors |
| 22 | `extension-values` | Allow | `allow`, reasons `e1_0`, no errors |
| 23 | `extension-values-false` | Deny | `deny`, no reasons, no errors |
| 24 | `validate-request-true-rejects` | Observed failure mode | `operation-failure`: principal type `App::Doc` is not valid for `App::Action::"read"` |
| 25 | `validate-request-false-evaluates` | Allow | `allow`, reasons `e1_0`, no errors |
| 26 | `context-missing-required-attribute-fails` | Observed failure mode | `operation-failure`: expected context attribute `count`, even with request validation off |
| 27 | `bare-scope-entity-match` | Observed mode | `allow`, reasons `e1_0`, no errors; identity comparison succeeds without the entity being present |
| 28 | `bare-scope-required-attr-read` | Observed mode | `deny`; error `e1_0`: entity `App::Session::"s1"` does not exist |
| 29 | `supplied-incomplete-entity` | Observed mode | `operation-failure`: entity `App::Session::"s1"` lacks required attribute `token`; the whole call fails closed |
| 30 | `scope-entity-present-missing-attribute` | Observed mode | `deny`; error `e1_0`: `App::Session::"s1"` does not have the attribute `token`; distinct from case 28 |

Additional differential checks passed for every case:

- Policy validation outcome and validation-error ID sets.
- Validation warning counts (one in total: case 9).
- Entity-parse outcome type, including the non-conforming entity at case 29.

Negative controls also passed: schema-invalid policy rejected before export;
unguarded optional-attribute access rejected by the Cedar validator; syntax
errors reported as parse diagnostics; malformed bodies attributed to a policy
ID; a matching valid permit beside an unparsable policy produces an operation
failure, never an Allow; the splitter rejects trailing content and preserves
semicolons inside strings and braces.

## 7. Policy-ID preservation

Dogwood exports generated Cedar IDs separately from rendered Cedar text. The
evidence establishes:

- `CedarPolicySet.FromPolicies` retains all 12 exported IDs `e1_0..e1_11`.
- `CedarPolicySet.PolicyIds` enumerates the managed set in supplied order, while
  the native Cedar policy map is ordered by ID internally. Mapping from a
  determining/erroring ID to a Dogwood rule must therefore use ID identity, not
  list position.
- Reparsing the concatenated export with `FromText` assigns `policy0..policyN`
  positionally. For a permit/forbid pair the deny reports `policy1`, which maps
  to no exported rule identity. This is the concrete failure mode the adapter
  must avoid.
- A source annotation `@id("audit-note")` remains an ordinary Cedar annotation
  on the body associated with stored ID `e1_0`. It is **not** the stored policy
  ID and never appears in determining IDs.
- The default bridge distincter still yields `policy_0`/`policy_1`; those IDs
  are preserved and mapped normally.

The prototype obtains per-rule bodies by splitting the concatenated display
output. This is lossless for formatter output and is checked by exact
reconstruction, but its pairing to rule IDs relies on the bridge inserting
policies in Dogwood source order. Tests verify that pairing, including source
annotations and twelve rules. Production D3 should not keep this implementation
property: it should export one body per rule, or return a body-to-ID mapping
natively, and then call `FromPolicies`.

## 8. E1-5 entity and request export

CedarSharp's existing inputs represent the required data without new
abstractions:

- Entities round trip through both raw Cedar JSON and `CedarEntity`/
  `CedarValue` helpers.
- `__entity` references preserve an entity UID as an attribute value.
- `parents` preserve hierarchy edges used by `principal in Group`.
- `__extn` preserves `ip`, `decimal` and `datetime`; `CedarValue.Ip`, `.Decimal`
  and `.Datetime` produce the same encodings.
- Context records and nested records are accepted unchanged.
- Both exported schema forms parse and validate identically on all 30 cases.

The action hierarchy is carried by the augmented schema, not the entity store.
With the schema attached, Cedar adds the schema's action entities and ancestor
edges, so `action in App::Action::"ReadGroup"` resolves with an empty entity
payload. Merging those action entities into the payload changes nothing. Without
the schema the membership is absent and the same policy denies. **The request
must carry the schema.**

### Entity-model difference to carry into D3

The upstream Dogwood authorizer builds its store in three tiers: caller-supplied
entities are schema-checked; scope principal and resource are then added bare
and unchecked if the caller did not supply them; finally schema action entities
are merged. A CedarSharp request is a flat entity list plus an optional schema,
and the schema conformance-checks every supplied entity. CedarSharp does not
inject the bare scope entities.

The corpus confirms the consequences in the direct-Cedar comparison:

- Identity comparison against an absent scope entity succeeds (`bare-scope-entity-match`).
- Reading a required attribute of an absent scope entity errors with
  `entity ... does not exist` (`bare-scope-required-attr-read`).
- The same entity supplied without the attribute errors differently:
  `... does not have the attribute ...` (`scope-entity-present-missing-attribute`).
- A caller-supplied entity that violates the schema fails the whole call closed
  (`supplied-incomplete-entity`).

Both the 4.11 oracle and CedarSharp behave the same on these inputs. The
difference is therefore between a direct Cedar request and Dogwood's own
authorizer, which injects the bare scope entities. A D3 adapter that promises
upstream authorizer equivalence must reproduce that store construction itself or
document the changed diagnostic/error mode. E1 did not calibrate a differential
against the upstream Dogwood authorizer; that remains open.

## 9. E1-6 temporal wiring and trust boundary

One lowered temporal policy was exercised:

```cedar
permit(principal, action == Test::Action::"Read", resource)
when { context.e1t_0__temporal_0 };
```

The augmented schema declares `e1t_0__temporal_0` as a Boolean even though the
authored action schema does not. Supplying `true` produces `allow` with reason
`e1t_0`; supplying `false` produces `deny`. The Cedar 4.11 reference produces
the same decision for both values. The lowering reports
`SelfContainedCedar=false`, which is the expected marker that a generated
context value is still required.

**Trust boundary:** the experiment injects the generated Boolean directly into
request context. That is a test harness only. In production the generated field
must be reserved and computed inside Dogwood's trusted pipeline (`decide_at` /
temporal engine), with a temporal-evaluation failure denying closed. No public
API may let an untrusted caller supply a value that stands in for a temporal
evaluation. This experiment demonstrates wiring feasibility, not temporal
evaluation or a production boundary.

## 10. E1-7 callback and lifecycle assessment

- **No callback seam exists in CedarSharp.** Authorization takes a complete
  request snapshot. The adapter must run upstream providers and temporal
  bindings first, materialize policy set, schema, context and entities, then
  call CedarSharp. There is no managed callback during Cedar evaluation to
  supply lazy values.
- **Both native images coexist.** After the Dogwood lowering call and a
  CedarSharp authorization call, the process module list contains both
  `dogwoodsharp_bridge.dll` and `cedarsharp_native.dll`. Two different Cedar
  versions serve different calls in one process.
- **Process lifetime is required.** CedarSharp retains its native library for
  the process lifetime, matching the D2 bridge contract. The adapter must not
  promise unload, replacement, collectible-context reclamation or hot reload of
  either image. Process isolation remains the future option for those needs.
- **The packaging shape works.** The research AOT consumer lowers through the
  bridge and authorizes through CedarSharp in one fully trimmed NativeAOT
  executable on `net8.0` and `net10.0`, with the built D2 bridge asset staged
  beside it. It prints `lowered rule id = aot_0`,
  `cedarsharp decision = Allow`, `determining policies = [aot_0]` and
  `AOT consumer OK`.
- **Concurrency was not qualified.** The prototype used one engine instance per
  test process and made sequential calls. CedarSharp's documented immutable
  snapshots and the D2 serialized-session discipline are inputs to a future
  design, not evidence of concurrent safety in this delivery.

## 11. Qualifications required for D3

1. Export policy bodies per rule and preserve exported IDs with
   `CedarPolicySet.FromPolicies`. Never reparse concatenated text for identity
   and never treat a source `@id` annotation as the stored policy ID.
2. Map determining and erroring IDs back by identity. Do not rely on policy-set
   enumeration order.
3. Always transport the augmented schema with each request. Action hierarchy is
   schema-derived; merging action entities is redundant.
4. Reproduce the upstream entity-store construction, including bare scope
   entities, or explicitly document the different fail-closed mode. Do not
   forward caller entities as if they were the complete upstream store.
5. Enforce a clean-Allow policy or an explicit fail-closed rule at the adapter
   boundary. A raw Allow carrying policy errors is not clean.
6. Preserve the temporal generated-field trust boundary. Reserved fields are
   computed only by the trusted pipeline; caller-supplied temporal values are
   rejected.
7. Keep both native images resident until process exit and keep the adapter out
   of unload/replacement promises.
8. Re-qualify the version pair when either Cedar pin changes. This report
   qualifies 4.11.0 against 4.13.0 for the executed corpus only.

## 12. Risks and open items

| Item | Status |
| --- | --- |
| Whole-language parity between Cedar 4.11 and 4.13 | Not established. The corpus covers the required families, not every Cedar feature or SDK behavior. |
| Differential against the upstream Dogwood authorizer | Not executed. E1 compares CedarSharp with the pinned Cedar FFI; entity-store semantics still need authorizer-level calibration. |
| Policy-body-to-ID export | Prototype splitter relies on source-order insertion. Replace with a per-rule native export. |
| Bare scope entity semantics | Reproduce upstream injection or document the difference before promising equivalence. |
| Open-ended, provider, datetime-boundary and large-scale cases | Not in the 30-case corpus. Additional cases belong in the D3 qualification corpus. |
| Concurrency, throughput and memory | Not measured. No performance or concurrent-safety claim. |
| Linux and macOS | Not executed. `linux-x64` and `osx-arm64` remain pending. |
| NativeAOT with arbitrary CedarSharp object overloads | Not used. The consumer exercises only JSON/typed inputs; warning-free publication is not a general trimming guarantee. |

## 13. Recommendation

Adopt **decision B** for the Cedar evaluation layer: proceed toward a
DogwoodSharp-on-CedarSharp adapter that uses the D2 bridge for parse, validate
and lower, and CedarSharp for the final Cedar decision, with the qualifications
above implemented and requalified. Retain the native reference path for
differential testing and for the trusted temporal/provider pipeline. Do not
attempt a full native Dogwood authorizer wrapper (option A) on the current
evidence, and do not require a CedarSharp change (option C) for the executed
scope.

The next commission should be a bounded D3 implementation with its own
qualification matrix. It must not inherit this report's Windows-only evidence
as cross-platform qualification.
