# Upstream discovery report

Disposition: source investigation and bounded feasibility completed; full
semantic compatibility and production security remain unqualified. All source
identifiers below resolve through [the revision record](sources.md).

## Verified architecture

Dogwood 1.1.2 has one semantic front door, `dogwood-language`, with crate-private
parsing/lowering/interpreter internals. `dogwood-syntax` is shared with the
separate source-preserving `dogwood-formatter`; the CLI and documentation tests
are additional workspace members. A wrapper should use curated public exports,
not copy internal functions. [DW-API](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/lib.rs), [DW-MANIFEST](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/Cargo.toml), [DW-FORMAT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-formatter/src/lib.rs).

The lifecycle is service schema + action schema, parse, lower, validate, construct
authorizer, then ingest events. Parsing and lowering do not replace validation.
Lowering exports policies with stable synthesized IDs, augmented schema text and
JSON, temporal and provider fields, event signatures and rule mapping. Plain
Cedar export is self-contained only when neither temporal nor provider leaves
were hoisted. [DW-LOWER](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/policy_set.rs).

`PolicyEngine::prepare` receives lowered policies/schema; `is_authorized` receives
the complete Cedar request and entities and returns decision, policy IDs and
errors. `TemporalEngine` separates prepare/observe/evaluate and supports optional
partitioning. Both traits require Send; neither establishes arbitrary concurrent
access to a single stateful authorizer. `Authorizer::is_authorized` requires
mutable access and returns None for non-decision events. [DW-ENGINE](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/engine.rs), [DW-AUTH](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/authorize/mod.rs).

The actual decision loop observes each event before evaluating, constructs
temporal/provider context, builds request/entities, and invokes the policy engine.
It includes attributed entities, bare principal/resource entities, and schema
action hierarchies. Preserve this loop through the public PolicyEngine seam.
Recreating it in C# would require reproducing private behavior. [DW-LOOP](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs).

## Source/documentation discrepancies

The engine API's comment says Dogwood supplies empty entities, but `api.rs`
builds attributed entities and action hierarchy edges. Treat the implementation
as evidence of current behavior and qualify it with entity tests before D3.
[DW-ENGINE](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/engine.rs), [DW-LOOP](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs).

The root security text says Rhai limits are not configured. The actual provider
engine configures 1,000,000 operations, call depth 64, strings 64 KiB, arrays/maps
4096 elements and zero loaded modules. It uses a bare engine with selected pure
packages. These are useful bounds, not a wall-clock timeout or whole-process
memory guarantee. The threat model preserves the stronger verified facts without
removing the need for resource isolation. [DW-ROOT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/README.md), [DW-PROVIDER](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/extension/provider/eval.rs).

## Stability and security

Core parser/lowering/operator functionality is exposed in the published 1.1.2
surface; this investigation does not independently certify API stability.
Arithmetic inside temporal comparisons is explicitly experimental and gated.
Provider networking is optional and off by default. The embedded temporal corpus
is optional test data. [DW-MANIFEST](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/Cargo.toml).

The reference interpreter is explicitly unsuitable for production enforcement.
It does not authenticate events, enforce increasing timestamps, bound/evict
history, recover it after restart, or provide transactional spending reservations.
Pins affect semantics; they are not by themselves a tenant storage isolation
boundary. Logged fields and request context are separate channels.
[DW-ROOT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/README.md), [DW-EVENT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/interpreter/value.rs).

Temporal/provider preprocessing failures currently produce diagnostic Deny.
However provider errors are outside the provider language contract, and Cedar
policy evaluation errors retain skip-on-error semantics. An application wanting
strict enforcement must require error-free Allow. Do not rewrite the reference
verdict to hide the distinction. [DW-LOOP](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs), [DW-PROVIDER-CONTRACT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-docs/guide/05-information-providers.md), [CS-RESULT](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/CedarResults.cs).

## Tests inspected and reuse

The manifest exposes passing Cedar-only, temporal-only, provider-only, macro,
mixed, expected-failure, experimental and pending-fix suites. The test tree also
contains parser/validator boundaries, identifier escaping, augmented schema
goldens, partition differential and provider fail-closed regressions. Pending-fix
cases must be classified, not silently counted as supported. The corpus feature
and documentation bundle harness provide reusable inputs. [DW-TESTS](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/tests),
[DW-CORPUS](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/corpus.rs), [DW-DOC-TESTS](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-docs/tests/examples.rs).

This commission ran the six original spike fixtures, not the full upstream
corpora. Corpus import, license inventory and full differential execution are D2-D5
gates. See [capability inventory](capabilities.md) and [spike](feasibility-spike.md).
