# D1 native ABI v1

The C ABI uses the platform C calling convention and exports these symbols:

```c
#include <stdint.h>

typedef struct {
    uint8_t *data;
    uint32_t len;
} dogwoodsharp_buffer_v1;

uint32_t dogwoodsharp_abi_version(void);
uint32_t dogwoodsharp_call_v1(uint32_t abi, uint32_t operation,
    const uint8_t *input, uint32_t input_len, dogwoodsharp_buffer_v1 *output);
void dogwoodsharp_free_v1(dogwoodsharp_buffer_v1 *output);
```

Use natural C alignment. On all three D1 targets, the buffer has an 8-byte pointer
at offset 0, a 4-byte length at offset 8 and 4 bytes of tail padding. Lengths,
versions, operation IDs and statuses are unsigned 32-bit values.
`dogwoodsharp_abi_version` returns 1. The dispatch call also requires ABI 1.
Changing a qualified v1 symbol, layout, status, or wire contract requires a new ABI.

Operations 0 and 1 are the D1 contract and keep their exact behavior. D2 adds
operations 2 and 3, and D3 adds operation 4, over the same symbols, which is
backward compatible: a D1-only library answers a newer caller with status 3
(unknown operation), and a D3 library answers D1 and D2 callers exactly as
before. No v1 symbol, buffer layout, status value, or qualified wire contract
changed. R1 revises the provisional D2 records for operations 2 and 3 before
D2 qualification. Pair D2/D3 managed and native components from the same
revision. The D1 identity response is unchanged.

## Call and ownership

- Pass a writable, aligned output slot that holds no live allocation. A null
  output returns status 1. The bridge initializes a supplied slot to
  `{NULL, 0}` before any validation.
- The input is borrowed and read-only for the duration of the call. A supported
  input length must address that many readable bytes. Input and output must
  occupy disjoint storage. Bytes have no terminating-NUL convention and are
  interpreted as strict UTF-8.
- Dispatch validates the ABI, operation, envelope byte ceiling, nonempty pointer
  and length, UTF-8, and JSON shape in that order. All operations accept
  exactly one JSON object with the specified fields. Unknown fields, duplicate
  fields, trailing values, BOMs and unpaired surrogate escapes are rejected.
- On status 0, the output is one nonempty JSON object encoded in UTF-8, and the
  native allocator transfers ownership to the caller. Release the unchanged
  buffer using the same library's `dogwoodsharp_free_v1`. Free resets the slot
  to `{NULL, 0}` and accepts a null pointer or an already empty slot. A copied
  descriptor is an alias and must never be freed. The historical D1 ownership
  rule keeps the image loaded until all live buffers have been released. The
  D2-enabled image adds a process-lifetime obligation.
- Calls may run concurrently with independent buffers and immutable request
  storage. Callers must synchronize access to any shared slot. These contracts
  require valid pointers and cannot validate addresses supplied by a native caller.

### D2 image lifetime

Native C hosts must hold a reference to the D2-enabled library for the life
of the process, alongside releasing each owned response as specified above.
Use a single load for concurrent calls and leave its reference held at
shutdown. D2 can initialize process-global lazy state in the pinned
dependencies. Releasing every response still leaves that state resident.
Closing the last reference after D2 calls can strand dependency allocations
and invalidates the corresponding function pointers. The ABI cannot enforce
the caller's loader behavior. Dynamic unload and reload are unsupported.

The archived D1-only asset retains its accepted historical contract. Use
of D1 operations through the D2-enabled distribution follows the D2 hosting
contract for that image. See the [R2 producer investigation](../d2-r2-verification.md).

The JSON request ceiling is 6,291,520 bytes. The decoded policy source ceiling
is 1,048,576 UTF-8 bytes. Operations 2, 3 and 4 additionally bound each schema
text field (action schema, event schema, macro library) at 1,048,576 UTF-8
bytes and a lowering distincter at 256 bytes. The encoded response ceiling is
2,097,152 bytes for operations 0 and 1, and 8,388,608 bytes for operations 2, 3
and 4, which return several derived artifacts for one call. An oversized
response fails as a whole. The encoded envelope and each decoded field have
independent limits.
JSON escaping, property names, and the other fields determine whether a
maximum-size decoded source fits in the encoded envelope. Exceeding either
limit returns status 6. The pinned parser uses its default
nesting limit of 32 and macro-expansion budget of 1,000,000 nodes. Those budgets and the wire ceilings do
not establish a process memory ceiling or a time bound.

| Status | Meaning |
| --- | --- |
| 0 | Operation completed, including a structured upstream parse failure |
| 1 | Null output or null/empty input |
| 2 | Unsupported ABI version |
| 3 | Unknown operation |
| 4 | Invalid UTF-8 |
| 5 | Invalid JSON or operation envelope |
| 6 | Request, source, or response exceeds its byte limit |
| 7 | Contained Rust unwind |
| 8 | Unexpected parser variant or internal encoding failure |

Every nonzero status leaves a supplied output empty. Call dispatch catches
unwinding panics. Abort, allocation failure, invalid pointers and faults in
foreign code are outside that guarantee. Rust's default panic hook may write
to stderr even when the unwind is caught. The production ABI contains no
panic-injection operation. Managed callers use direct Cdecl function pointers
from the verified image, and there are no managed callbacks.

## Operations

| ID | Request | Response on status 0 |
| --- | --- | --- |
| 0 | `{}` | Native identity described in the [manifest specification](native-artifacts.md) |
| 1 | `{"source":"..."}` | `{"success":true,"policyCount":1,"diagnostics":[]}` or a failed-parse record |
| 2 | D2 request below | Validation outcome |
| 3 | Same D2 request | Validation outcome and, on export success, Cedar artifacts |
| 4 | Same D2 request | Operation 3 response plus the D3 per-rule Cedar export |

A failed parse has `success:false`, `policyCount:null` and one or more ordered
diagnostics. Each has `category` (`syntax` or `macro`), `message` (upstream
display text) and `location` (null or `{"offset":n,"length":n}`). Numbers are
nonnegative integers. Locations identify UTF-8 bytes in the supplied source.
Locations are rebased to the original source when Dogwood strips a leading
UTF-8 BOM. A one-byte span starting at EOF is an upstream end-of-input marker.
Locations for a different source or outside that contract are null. Message
wording may change and can contain caller-supplied policy text.

D1 calls `ParsedPolicySet::parse` with `ServiceSchema::defaults()`. Results
describe syntax, structural checks and macro expansion. An empty policy set
is valid. D2 adds schema validation and lowering.

## D2 operations

Operations 2, 3 and 4 use the same request. `source` and `policySchema` are
required. `eventSchema`, `macros` and `distincter` may be omitted or null.

```json
{"source":"...", "policySchema":"...", "eventSchema":"...", "macros":"...", "distincter":"..."}
```

A distincter selects the namespace for the actual lowering that is checked
and validated by either operation. Omitting it selects `policy`. A distincter
must match `[_A-Za-z][_A-Za-z0-9]*`. Native rejects an invalid name at the
lowering stage.

Both operations return these required fields in status-0 replies. Flags report
successful completion in order. After a failure, later flags are false.

| Field | Meaning |
| --- | --- |
| `serviceSchemaBuilt` | The service schema was constructed. |
| `parsed` | The policies parsed successfully. |
| `actionSchemaBuilt` | Dogwood constructed its action-schema input; Cedar resolves and checks it during lowering. |
| `lowered` | Dogwood produced a lowered policy set and augmented schema in memory. |
| `collisionCheckPassed` | Generated temporal fields were checked against the original contexts of their target actions. |
| `validationPassed` | Dogwood validated the same lowered set with zero errors. Warnings are preserved. |
| `artifactsExported` | Operation 3 or 4 produced the complete validated bundle. Always false for operation 2. |
| `policyCount`, `parseDiagnostics` | Successful parse count or null, and ordered diagnostics for a failed parse. Diagnostic shape matches operation 1. |
| `ruleCount`, `selfContainedCedar` | Intermediate lowering metadata. Zero and false before successful lowering. |
| `collision` | Null, or `{"field":"...","actionNamespace":"...","actionId":"..."}`; a null namespace identifies a top-level action. One deterministic conflicting field and concrete action is reported. |
| `validationErrors`, `validationWarnings` | Ordered findings with `category`, `message` and `location`. Both are empty when validation did not run. |
| `stageError` | Null, or an error with `stage`, `category`, `message` and `location`. |
| `rules` | Source-ordered `{"ruleIndex":n,"cedarPolicyId":"..."}` for validated operation 2, with `sourceText` added for exported operations 3 and 4, and `cedarBody` added for operation 4. Empty on failure. |

Operations 3 and 4 also require `cedarPolicyText`, `cedarSchemaJson` and
`cedarSchemaText`. All are empty unless `artifactsExported` is true. A valid
empty set can have empty policy text. The validated policy and schema always
come from the same lowering call. Operation 2 returns findings and validated
rule identities. A failed parse reports parse diagnostics, a collision
reports the structured `collision` plus a stage error, and failed validation
reports validation errors. Collision and validation failures suppress all
exported artifacts.

## D3 export operation

Operation 4 runs the identical pipeline as operations 2 and 3, so their stage
flags, findings, rule identities, expanded source, policy text and schema
artifacts always agree. Its reply is the operation 3 reply plus two additive
export fields. They follow the artifact discipline: present on every operation 4
reply, with empty values when the export did not commit.

| Field | Meaning |
| --- | --- |
| `rules[].cedarBody` | The exact Cedar body the lowered Cedar set stores under that rule's `cedarPolicyId`, rendered from the compiled `cedar_policy::Policy`. Never split from `cedarPolicyText`, so `(cedarPolicyId, cedarBody)` is an identity-bearing pair. |
| `generatedContextFields` | The trusted-pipeline-owned reserved context field names: each hoisted temporal boolean's top-level `context.<id>` attribute, plus the single reserved `providers` record when any provider field is hoisted. Names come from the lowered artifact, never from the request. Empty exactly when `selfContainedCedar` is true. |

Operation 3 keeps its qualified reply shape exactly: no `cedarBody` and no
`generatedContextFields`. The D3 per-rule export is a separate operation rather
than an extension of operation 3, so the archived D2 corpus, the D2
differential, and D2 consumers are unaffected. A D2-only library answers
operation 4 with status 3; a D3 consumer must treat that as an unavailable
operation and never substitute the operation 3 reply.

The `stageError.stage` values are `service-schema`, `action-schema`, `lower`,
`collision-check` and `artifact-export`. Stable categories are `schema`,
`policy`, `distincter`, `generated-context-field-collision` and `internal`.
Failure to resolve an expected original context fails the collision check.

Validation spans address UTF-8 policy source. The bridge restores the three
bytes of a stripped leading UTF-8 BOM to upstream offsets, as for parsing.
Cedar validation warnings have null locations in the wire record. Pinned
Cedar warnings can expose diagnostic labels. Mapping these labels requires
source provenance and offset-rebasing verification.
