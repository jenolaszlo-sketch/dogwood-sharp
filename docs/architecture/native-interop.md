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
Changing a v1 symbol, layout, status, or wire contract requires a new ABI.

Operations 0 and 1 are the D1 contract and keep their exact behavior. D2 adds
operations 2 and 3 over the same symbols, which is backward compatible: a
D1-only library answers a D2 caller with status 3 (unknown operation), and a
D2 library answers D1 callers exactly as before. No v1 symbol, buffer layout,
status value, or existing wire contract changed. The identity response is
unchanged, so a D1 consumer cannot see the added operations.

## Call and ownership

- Pass a writable, aligned output slot that holds no live allocation. A null
  output returns status 1. The bridge initializes a supplied slot to
  `{NULL, 0}` before any validation.
- The input is borrowed and read-only for the duration of the call. A supported
  input length must address that many readable bytes. Input and output must
  occupy disjoint storage. Bytes have no terminating-NUL convention and are
  interpreted as strict UTF-8.
- Dispatch validates the ABI, operation, envelope byte ceiling, nonempty pointer
  and length, UTF-8, and JSON shape in that order. Both operations accept
  exactly one JSON object with the specified fields. Unknown fields, duplicate
  fields, trailing values, BOMs and unpaired surrogate escapes are rejected.
- On status 0, the output is one nonempty JSON object encoded in UTF-8, and the
  native allocator transfers ownership to the caller. Release the unchanged
  buffer using the same library's `dogwoodsharp_free_v1`. Free resets the slot
  to `{NULL, 0}` and accepts a null pointer or an already empty slot. A copied
  descriptor is an alias and must never be freed. Keep the library loaded
  until every live buffer has been released.
- Calls may run concurrently with independent buffers and immutable request
  storage. Callers must synchronize access to any shared slot. These contracts
  require valid pointers and cannot validate addresses supplied by a native caller.

The JSON request ceiling is 6,291,520 bytes. The decoded policy source ceiling
is 1,048,576 UTF-8 bytes. Operations 2 and 3 additionally bound each schema text
field (action schema, event schema, macro library) at 1,048,576 UTF-8 bytes and
a lowering distincter at 256 bytes. The encoded response ceiling is 2,097,152
bytes for operations 0 and 1, and 8,388,608 bytes for operations 2 and 3, which
return several derived artifacts for one call. An oversized response fails as a
whole. The whole-request ceiling applies to every operation, so a maximum-size
source leaves no room for a schema; that combination fails as status 6 rather
than truncating. The pinned parser uses its default
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
| 2 | `{"source":"...","policySchema":"...","eventSchema":"...","macros":"..."}` | A validation record (below) |
| 3 | `2` plus `"distincter":"..."` | A lowered record (below) |

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

Both D2 operations share one request shape so a caller cannot construct a
request that is meaningful for one and not the other. `eventSchema`, `macros` and
`distincter` are optional. Operation 2 rejects a request that carries
`distincter` with status 5, because validation does not name hoisted fields;
accepting and ignoring it would discard a caller's collision-avoidance intent.

Both replies keep the outcome of each pipeline stage separately, so no consumer
can mistake a failed stage for a completed one:

| Field | Meaning |
| --- | --- |
| `parsed` | Parsing succeeded. `policyCount` and `parseDiagnostics` are meaningful only then. |
| `lowered` | Lowering succeeded. Everything downstream is meaningful only then. |
| `validationPassed` | Validation produced no errors. Warnings never fail validation. |
| `ruleCount` | The number of lowered rules, or zero when lowering did not run. |
| `selfContainedCedar` | Whether the lowered artifacts reproduce Dogwood semantics with no extra `context`. |
| `rules` | `{"ruleIndex":n,"cedarPolicyId":"..."}` for validation, plus `"sourceText"` for lowering. |
| `validationErrors` / `validationWarnings` | `{"category":..., "message":..., "location":...}` findings in upstream order. |
| `stageError` | null, or `{"stage":"service-schema","category":"schema","message":"...","location":null}` when a stage stopped the pipeline. |

Operation 3 additionally returns `cedarPolicyText`, `cedarSchemaJson` and
`cedarSchemaText`. Every one is empty unless lowering succeeded, so a failed
lowering never returns partial authoritative output.

`category` is `schema` for any schema problem, `policy` for a policy that cannot
be lowered, `distincter` for a rejected lowering namespace, and `internal` for an
unexpected upstream error mapped defensively. `stage` is `service-schema` when
building the service schema failed, and `lower` for the action schema, lowering
and validation path. Upstream reports an unusable action schema through the same
channel as the service schema, so the two are distinguished by `stage` rather
than by `category`.

D2 calls `ParsedPolicySet::parse`, `ParsedPolicySet::lower` or
`ParsedPolicySet::lower_with_distincter`, and `Validator::validate` on the
lowered set. Parsing, lowering and validation are upstream behavior; the wrapper
only projects the results. Validation findings carry a span upstream already
rebases to the Dogwood source, except that upstream does not rebase a stripped
leading UTF-8 BOM, so the wrapper restores those three bytes exactly as parsing
does. Cedar publishes a warning's text but not its span, so a warning's
`location` is always null.

Synthesized Cedar policy ids are `<namespace>_<rule>`, where the default
namespace is `policy` and a supplied distincter replaces it. Rule indices and
ids therefore stay traceable in source order, and a distincter keeps separate
lowering calls from colliding.
