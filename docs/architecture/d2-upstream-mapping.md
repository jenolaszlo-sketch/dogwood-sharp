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
| `ParsedPolicySet::lower_with_distincter(&policy_schema, distincter)` | `policy_set.rs` | Same lowering, but namespacing synthesized ids and hoisted field names under a caller-supplied Cedar identifier so separate lowering calls cannot collide. |
| `ParsedPolicySet::policies()` / `ParsedPolicy::expanded_source()` / `temporal_count()` / `provider_count()` | `policy_set.rs` | Per-rule source text after macro expansion and extension usage counts, for rule/source mapping. |
| `LoweredPolicySet::as_cedar()` | `policy_set.rs` | `cedar_policy::PolicySet` of lowered policies. Rendered to Cedar text with `Display`. |
| `LoweredPolicySet::cedar_schema_json()` / `cedar_schema_str()` | `policy_set.rs` | Augmented schema in the Cedar JSON schema form and `.cedarschema` text form. |
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
5. Validation is a separate pass over the lowered set. Errors carry a span in
   the original Dogwood source; warnings do not expose a span through the
   public surface, so only their message is exported.
6. An empty policy set lowers to zero rules and validates successfully.
7. An invalid distincter is rejected with `Error::InvalidDistincter` and never
   silently renamed.

## Mapping to DogwoodSharp

### Native (ABI v1 extension, no breaking change)

Operations 0 and 1 keep their exact existing symbols, statuses and wire
contract. Two operations are added:

| Operation | Upstream call | Response |
| --- | --- | --- |
| 2 `validate` | `ParsedPolicySet::parse` then `ParsedPolicySet::lower` then `Validator::validate` | Parse/lower/validation stage flags and structured findings |
| 3 `lower` | `ParsedPolicySet::parse` then `ParsedPolicySet::lower_with_distincter` | Cedar policy text, augmented Cedar schema JSON, rule/source mapping |

Both share one request shape so a caller cannot construct an inconsistent one:

```json
{"source":"...", "policySchema":"...", "eventSchema":"...", "macros":"...", "distincter":"..."}
```

`eventSchema`, `macros` and `distincter` are optional. Operation 2 rejects a
request that carries `distincter`, because validation does not name hoisted
fields; silently ignoring a caller's collision-avoidance input would be worse
than failing.

Both responses carry the same stage flags so no consumer can mistake a failed
stage for a completed one:

| Field | Meaning |
| --- | --- |
| `parsed` | Parsing succeeded. `policyCount` and `parseDiagnostics` are meaningful only then. |
| `lowered` | Lowering succeeded. Everything downstream is meaningful only then. |
| `validationPassed` | Validation ran and produced no errors. Warnings never fail validation. |
| `lowerError` | The fatal pipeline error when `parsed` is true and `lowered` is false. |

### Managed

| Managed API | Native operation |
| --- | --- |
| `Dogwood.Validate(source, schema)` → `DogwoodValidationResult` | 2 |
| `Dogwood.Lower(source, schema)` → `DogwoodLoweredPolicySet` | 3 |
| `DogwoodSchema( PolicySchema, EventSchema?, Macros?, Distincter? )` | request fields |
| `DogwoodBridgeException` (existing) | unchanged failure surface |

The managed layer adds no parsing, lowering or validation logic: it serializes
the request, enforces the same argument ceilings the native bridge enforces, and
converts the JSON reply into immutable snapshots. A distincter that is not a
valid Cedar identifier is rejected with `ArgumentException` before any native
call, and again natively.

### Clear separation of outcomes

`Dogwood.Parse` (D1) reports parsing only. `Dogwood.Validate` reports parsing,
lowering and validation separately. `Dogwood.Lower` reports parsing and
lowering and returns a bundle only when both succeeded. Authorization is not
implemented and is not representable in the D2 API.