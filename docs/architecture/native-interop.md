# Native interoperability design

Proposed, not implemented in D0. Primary engineering reference:
[CS-NATIVE](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/native/src/lib.rs), [CS-LOADER](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/a25181d17603381e42adde60613f7a49d5309a2b/src/CedarSharp/NativeBridge.cs). The empty Rust cdylib scaffold deliberately
exports no pretend authorization operations.

Use a minimal versioned C ABI with fixed-width status/operation numbers and
UTF-8 JSON input/output buffers. Proposed entry points: version, call_v1,
free_v1, session_create_v1, session_call_v1, session_destroy_v1. Exact names and
wire schema freeze in D1. Never exchange Rust/Cedar object pointers between
Dogwood and CedarSharp libraries.

| Boundary | Proposed contract / required test |
| --- | --- |
| Input | Borrowed ptr+length valid for call; strict UTF-8; reject null/nonzero, invalid JSON and oversized payload before parsing |
| Output | Initialize empty before dispatch; native allocates, same library frees exactly once; length authoritative, no NUL assumption |
| Limits | Initial design ceiling 16 MiB input / 64 MiB output, lower configurable policy/event budgets; benchmark before finalizing |
| Errors | Distinguish invalid input, unsupported operation/version, resource limit, disposed/busy session, panic and evaluation result |
| Panic | catch_unwind with unwind build around each ABI operation; no Rust unwind or managed exception crosses C boundary; abort/OOM not recoverable |
| Handles | Opaque native-owned session, managed SafeHandle; disposal waits for active call; no reentrancy; native validates handle state |
| Concurrency | One ordered writer per session; independent sessions may run concurrently after qualification; no implied Sync from Send |
| Callback | Synchronous managed policy engine callback with rooted lifetime; catches managed exceptions; owns/copies result under explicit free contract |
| Managed import | LibraryImport for fixed ABI after setting verified resolver; UnmanagedCallersOnly/function pointer callback if AOT-qualified; justify deviations |
| Version discovery | ABI, bridge version, full Dogwood SHA, resolved Cedar version, Rust toolchain, feature set and target |
| Integrity | Manifest SHA-256 plus source/lock/toolchain/features; trusted package channel/provenance required because editable hash alone is not authenticity |
| Diagnostics | Stable wrapper category + upstream detail + source byte offsets; bound and redact before external logging |

Retain the native library for process lifetime while any handle/callback may exist.
Load only the intended verified runtime asset, without ambient search-path
fallback. Missing/unsupported RID fails explicitly. Native safe ownership tests
must cover repeated frees through the managed wrapper, disposal races, failure
paths, callback rejection and buffer limits; arbitrary invalid native pointers
cannot be made safe by JSON validation.

Pin upstream to the exact SHA in [sources](../research/sources.md); the research
manifest/lock exercise that pin now. D1 adopts it in the bridge. Rust 1.99.1 is
pinned for this foundation. Review features from the resolved graph, since Cargo
feature unification can re-enable unwanted capabilities. Formatter integration
needs its own pinned upstream crate. No native assets are redistributed at D0.
