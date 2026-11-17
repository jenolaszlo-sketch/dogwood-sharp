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
is 1,048,576 UTF-8 bytes. The encoded response ceiling is 2,097,152 bytes.
An oversized response fails as a whole. The pinned parser uses its default
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
is valid. Schema validation and authorization require later milestones.
