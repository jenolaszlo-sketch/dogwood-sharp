# D2 upstream API mapping

Recorded during WP-D2-1 of DOGWOOD-SHARP-D2-IMPL-01 against the pinned
upstream revision
[929f49e7305534ac61ef06cf8e47269c596dc851](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851)
(Dogwood 1.1.2). The pinned implementation is the semantic authority; this
document records the public upstream surface DogwoodSharp calls and the native
and managed operations proposed over it. Lowering and validation are **not**
reimplemented: the bridge calls the upstream crate.

## Upstream surface used

| Upstream API | Crate location | Purpose |
| --- | --- | --- |
| `ServiceSchema::defaults()` / `ServiceSchema::builder().event_schema_str(..).macros_str(..).build()` | `service_schema.rs` | Service-provided half of the schema: macro library and event-schema DSL. Already used by D1 parse. |
| `PolicySchema::from_cedarschema_str(text)` | `policy_schema.rs` | Action schema from Cedar `.cedarschema` text. Not typechecked at construction; typechecking happens during lowering/validation, and over-deep text is refused as `Error::ActionSchema`. |
| `ParsedPolicySet::parse(source, &service)` | `policy_set.rs` | Parse, unchanged from D1 (operation 1). |
| `ParsedPolicySet::lower(&policy_schema)` | `policy_set.rs` | Lower to Cedar: mint policy ids, augment the schema with hoisted `context.<id>` fields, derive the event schema. |
| `ParsedPolicySet::lower_with_distincter(&policy_schema, distincter)` | `policy_set.rs` | Runs lowering with a caller-selected namespace for synthesized policy IDs and generated context fields. |
| `ParsedPolicySet::policies()` / `ParsedPolicy::expanded_source()` / `temporal_count()` / `provider_count()` | `policy_set.rs` | Per-rule source text after macro expansion and extension usage counts, for rule/source mapping. |
| `LoweredPolicySet::as_cedar()` | `policy_set.rs` | `cedar_policy::PolicySet` of lowered policies. Rendered to Cedar text with `Display`. |
| `LoweredPolicySet::cedar_schema_json()` / `cedar_schema_str()` | `policy_set.rs` | Augmented schema in the Cedar JSON schema form and `.cedarschema` text form. |
| `LoweredPolicySet::nonrelativized_temporal_fields()` / `TemporalField::{id,target_actions}` | `policy_set.rs`, `api.rs` | Actual generated names and concrete actions resolved by Dogwood's augmentation scope logic. |
| Cedar `Fragment::from_cedarschema_str` / `to_internal_name_fragment_with_resolved_types` | cedar-policy-core 4.11.0 | Inspect original context records and resolve referenced context names under Cedar's namespace rules. |
| `LoweredPolicySet::is_self_contained_cedar()` | `policy_set.rs` | Whether the exported Cedar artifacts reproduce Dogwood semantics with no extra context (no hoisted temporal/provider leaves). |
| `LoweredPolicySet::rules()` / `rule_ref(id)` / `DogwoodRuleRef` | `policy_set.rs` | Rule index to synthesized Cedar policy id mapping, and reverse lookup. |
| `Validator::new().validate(&lowered)` / `ValidationResult` | `validator.rs`, `error.rs` | Schema-aware validation of the **lowered** set: Cedar findings plus Dogwood dialect findings, each carrying a span in the original `.dw` source. |
| `ValidationError::{Cedar, Extension}` / `ValidationWarning::{Cedar, Extension}` | `error.rs` | Findings carry `code`, `message` and `span`. Both enums are `#[non_exhaustive]`. |
| `dogwood_language::Error` | `api.rs` | The fatal pipeline error: parse, macro, lowering (`Cedarify`), Cedar assembly (`PolicySet`), schema (`CedarSchema`, `SchemaSerialize`, `ActionSchema`, `EventSchema`), extension leaves (`Leaf`) and distincter rejection (`InvalidDistincter`). `#[non_exhaustive]`. |

## Upstream behaviours confirmed before design

Executed against the pinned revision (scratch harness, not part of the
repository). These are the facts D2 must preserve:

1. Lowering augments the action schema: a temporal policy adds a
   `context.policy_<k>__temporal_<j>` Boolean leaf to the action's context, and
   `is_self_contained_cedar()` becomes `false`.
2. Synthesized Cedar policy ids are `<namespace>_<k>`; the default namespace of
   `lower` is `policy`, and `lower_with_distincter` uses the supplied Cedar
   identifier. `rules()[i].cedar_policy_id` is the id Cedar itself reports.
3. A source `@id("...")` annotation is preserved verbatim in the exported Cedar
   policy text, but Cedar policy identity is the synthesized id, so two rules
   annotated with the same `@id` still lower to distinct policy ids.
4. An invalid action schema fails **lowering** with a fatal `Error`, not with a
   validation finding. Missing or malformed schema information therefore never
   produces a bundle.
5. Validation is a separate pass over the lowered set. The wrapper projects
   warning locations as null and preserves category and message. Pinned Cedar
   warnings can expose diagnostic labels. Projecting those labels requires
   verifying the source provenance and rebasing byte offsets into the authored
   Dogwood source.
6. An empty policy set lowers to zero rules and validates successfully.
7. An invalid distincter is rejected with `Error::InvalidDistincter` and never
   silently renamed.

## Mapping to DogwoodSharp

Operations 2 and 3 share a pipeline. Each constructs the service schema, parses
the source, constructs the action-schema input, lowers with the requested
distincter, checks generated fields, and validates that exact result. Operation
3 exports only after those stages succeed. The [native ABI](native-interop.md#d2-operations)
and [managed API](../managed-api.md#d2-managed-api) describe the response.

The collision check uses `nonrelativized_temporal_fields()`. The executable
`temporal_fields()` view copies the same ID and target actions when it rewrites
the condition for event pins. `leaf_map()` serves runtime dispatch using the
already augmented schema. The original declarations are read from a Cedar
fragment parsed from the caller's action schema. The checker reads inline
record keys directly, including a key whose original type is undefined and
would disappear in augmentation. For a referenced context it asks Cedar to
resolve all names, then follows the qualified common-type references to the
outer record. This uses the same Cedar version and features as Dogwood.

`TemporalField.target_actions` comes from the same upstream scope resolution
that augmentation uses for explicit actions, action lists, action groups and
unconstrained scopes. The checker examines each generated name only on those
concrete actions. It compares key presence, irrespective of type or
optionality. It reports one deterministic field and action in upstream field
order and sorted target order. A missing original target or unresolvable
record stops the pipeline at the collision check.

The pinned fixture `temporal-policy-conflicting-declaration` records Dogwood's
replacement of a caller-supplied String with a required Boolean. Its
`wrapperCollision` annotation records the intentional DogwoodSharp rejection.
This boundary protects caller-declared fields without reserving a name prefix.
R1 also suppresses all artifact export following failed validation. This is an
export contract over unchanged upstream validation findings.

## Qualification requirements for an independent D2 reviewer

A subsequent independent qualification should re-derive its own expectations
from the pinned crate rather than trust `lowering-v1.json`, and cover at least:

- the three runtime identifiers x `net8.0` and `net10.0`;
- parser/validation failure isolation (an invalid policy never yields a validated
  bundle or authoritative artifacts);
- augmented-schema structure, generated field types, and conflicting generated
  fields (DL-01);
- rule/source identity preservation, including explicit `@id` annotations and
  generated ids;
- native limits (schema, source, request, response) and their failure statuses;
- concurrency and immutability of returned collections;
- parity with the existing D1 qualification evidence for operations 0 and 1.

The qualification gates this milestone adds are recorded in the acceptance
matrix of [qualification.md](../qualification.md).
