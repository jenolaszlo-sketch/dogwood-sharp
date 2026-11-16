# CedarSharp compatibility assessment

Disposition: **conditionally supported**. No CedarSharp change is needed for
the executed six-case spike. No full-language or production-equivalence claim.

| Concern | Verified finding | Required qualification |
| --- | --- | --- |
| Versions/features | Dogwood research graph Cedar 4.11.0; published CedarSharp 1.0.0 Cedar 4.13.0; both datetime/decimal/ipaddr | Corpus across versions; pin supported pairs |
| Policy wire format | CedarSharp accepts staticPolicies text or ID-to-text dictionary; Dogwood exposes policy IDs/text | Preserve each ID; do not concatenate and renumber |
| Schema | Dogwood exports augmented JSON/text; CedarSharp accepts both | All injected fields, action groups, enums and extensions |
| Context | Cedar JSON entity/extension encoding; JsonElement snapshots available | Null/missing/type/decimal edge cases and provider records |
| Entities/hierarchies | CedarSharp accepts attributes/parents; Dogwood includes bare scope entities and schema actions | Preserve exact entity set, mixed conformance and action edges |
| Validation | Separate policy validation; schema-aware request validation defaults true in CedarSharp | Match Dogwood validation/lifecycle without silently strengthening raw verdicts |
| Decisions | Cedar default deny, forbid precedence and skip-on-error | Invalid-policy plus matching-permit negative controls |
| Diagnostics | Determining IDs, policy errors and raw response exposed | Map IDs to rules; preserve errors; do not compare unstable wording only |
| ABI | CedarSharp C ABI v1, JSON byte buffers with native free | No Rust object exchange across libraries |
| Concurrency | CedarSharp retains native library for process lifetime; requests are immutable snapshots | Dogwood session calls serialized; dispose/callback races tested |
| Loading | Manifest hash, RID, ABI, SDK/toolchain/features checked | Equivalent Dogwood manifest; authenticate distribution chain |
| AOT/trimming | CedarSharp marks compatibility; JSON/metadata APIs avoid reflection; arbitrary object overloads have warnings | Actual two-framework package consumer/AOT runs for new adapter |

Evidence: [CS-INPUT](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/CedarInputs.cs), [CS-RESULT](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/CedarResults.cs), [CS-NATIVE](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/native/src/lib.rs),
[CS-LOADER](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/NativeBridge.cs), [CS-LOCK](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/native/Cargo.lock), [CS-MANIFEST](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/native/Cargo.toml), [CS-PROJECT](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/CedarSharp.csproj),
[DW-LOWER](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/policy_set.rs), [DW-LOOP](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs).

## Mapping contract proposal

Before prepare, serialize augmented schema via LoweredPolicySet, enumerate each
policy into an ID/text map, and retain ID-to-DogwoodRuleRef. During the upstream
policy-engine callback, export principal/action/resource, complete context and
complete entities; preserve extension values with Cedar's JSON encodings.
Providers run upstream so `context.providers` is computed rather than caller
authored. Temporal bindings remain internal to the trusted pipeline. Return
raw decision, determining IDs and all errors, then let Dogwood map rule identities.

Do not use request context to inject trusted temporal booleans in the eventual
public API. The spike's manual binding is explicitly research-only.

## Separate proposed CedarSharp work items

1. CS-P1: qualify a Cedar engine version matching Dogwood's resolved dependency,
   or document and prove a compatible version pair. No automatic downgrade.
2. CS-P2: reproduce bare scope entities + separately validated attributed entity
   semantics; investigate a prepared/prevalidated entity input only if existing
   APIs cannot preserve the contract. Avoid a general validation-bypass switch.
3. CS-P3: benchmark a prepared policy/schema handle if callback serialization or
   repeated parsing is material. This is optimization, not a D0 prerequisite.
4. CS-P4: consider stable diagnostic codes/source mapping only if D3 proves the
   current raw diagnostics insufficient; preserve original policy IDs regardless.

These are proposed investigations, not established mandatory code changes.
CedarSharp was not modified.
