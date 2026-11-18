# Initial threat model and security limitations

D1 implements verified asset selection, parser transport bounds and native
buffer ownership under the [native ABI contract](native-interop.md).

Assets: policy integrity, decision integrity, event history, tenant
secrets, native process memory, package provenance. Adversaries include callers
forging context/events, policy authors causing expensive evaluation, compromised
providers, and substituted native artifacts.

Trust boundaries: consuming application authenticates identities/events; schema
and policy deployment are privileged; the library owns generated bindings and
native handles; provider execution is restricted; diagnostics leave the trusted
process only after redaction. A consumer with arbitrary native process access is
outside the library's security boundary.

| Threat / failure | Proposed contract | Owner / evidence gate |
| --- | --- | --- |
| No matching permit, matching forbid | Preserve default deny and forbid precedence | Library, D3/D5 differential tests |
| Failed parse, lowering, or validation reaches downstream authorization | Export only the exact successfully checked and validated policy/schema pair, with the distincter used for lowering | Library, D2 differential and managed tests |
| Generated temporal field replaces a caller-declared field | Reject actual generated names on their upstream-resolved target actions when originally declared, regardless of attribute type or optionality | Library, DL-01 differential and D2 collision tests |
| Evaluation errors | Preserve raw reference decision/errors; strict enforcement requires clean Allow | Library + consumer, D3 error controls |
| Missing/malformed temporal context | Reject incomplete/wrong-typed backend result before Cedar; never default a missing binding to trusted false | Library, D5 (false can enable negated policies) |
| Caller fabricates generated context | Reject reserved temporal IDs, generated namespaces and providers at public input boundary; construct trusted context internally | Library, D5 collision/forgery tests |
| Forged/malformed/replayed events | Authenticate source, validate schema and unique event identity; replay/dedup policy explicit | Consumer intake + library validation, D4/D7 |
| Timestamp manipulation | Trusted clock/sequence source; enforce strict increase per ordered stream | Consumer + session guard, D4/D7 |
| Cross-tenant/session disclosure | Separate sessions/storage; explicit tenant key; pins are not authentication/storage isolation | Consumer + session API, D7 cross-key tests |
| Provider DoS/network access | net off; pure providers; bounded operations/data; hard time isolation where needed | Library profile + host, D6/D7 |
| Native use-after-free/panic | Owned buffers, panic containment and resident D2 image. Future session handles require their own lifetime and synchronization rules | Library, D1 ownership and D2-R2 residency controls |
| ABI/binary substitution | Check ABI, upstream revision, features, RID and hash before use; trusted distribution required | Library/package pipeline, D8 |
| Diagnostic leakage | Structured codes, bounded messages, opt-in source snippets; redact events/secrets | Library + consumer logging, D7 |
| Parser/history exhaustion | Input/depth/node/event/history/response limits; explicit capacity failures | Library + host, D1/D4/D7 |
| Unsupported features | Reject at load; never silently weaken a policy | Library, D2 feature matrix |
| Concurrent spending | Evaluation does not reserve funds; transactional commit/reservation at effect boundary | Consumer infrastructure, separate roadmap |

The [D2 hosting boundary](native-interop.md#d2-image-lifetime) requires
process-lifetime residency. Concurrent callers retain independent buffer
ownership. Plugin unload and hot reload cannot reclaim native dependency
state. A future isolated worker could provide reclamation through process
termination and bound the impact of native failures. D2 runs in process.

## Reference behavior that must remain visible

Dogwood explicitly excludes production enforcement and lacks event authentication,
timestamp validation and bounded durable history. Its observe-before-evaluate
ordering means denied requests may still enter history; an Allow is not proof of
an action completing. Record trusted response/completion events separately.
[DW-ROOT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/README.md), [DW-LOOP](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs).

Provider failures currently deny in the reference loop, but the provider contract
does not guarantee that outcome for an erroring provider. The implementation has
operation/depth/data-size caps despite stale root documentation; they do not
bound all host functions or establish a hard timeout. No network providers or
experimental temporal arithmetic should ship enabled. [DW-PROVIDER](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/extension/provider/eval.rs),
[DW-PROVIDER-CONTRACT](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-docs/guide/05-information-providers.md), [DW-MANIFEST](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/Cargo.toml).

## Temporal responsibility split

DogwoodSharp should expose explicit session lifecycle, ordered event ingestion,
backend identity, capacity failures and trusted binding construction. The first
backend retains in-memory reference semantics. A future durable backend needs
versioned snapshots, replay checkpoints and policy/schema version identity;
history retention must preserve every reachable temporal window and aggregation.
Do not silently evict data and continue returning authoritative decisions.

Applications own authentication, trusted timestamps, event authenticity, tenant
routing, durable storage deployment, audit retention and atomic effect execution.
Two agents can both pass a spending check against the same history; only a
transactional reservation/commit protocol can prevent their combined overspend.
Shared history requires an ordered coordinator outside the initial release.

Cancellation before admission can stop ingestion; cancellation after observation
cannot pretend the event was never seen. A synchronous native evaluator may not
be interruptible; hard resource isolation may require a worker process. Specify
this before exposing CancellationToken. `TemporalEngine::observe` returns no
Result, so durable failure semantics need a separate D6 design rather than a
blind adapter. [DW-ENGINE](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/engine.rs).
