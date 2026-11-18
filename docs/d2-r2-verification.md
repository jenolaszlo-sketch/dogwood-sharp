# DOGWOOD-SHARP-D2-R2 producer verification

## Baseline and change scope

Local `main` began clean and matched live `origin/main` at
`b79fdffa07ac45cb49c5ee2ef100299f2d2adcf5`, tree
`52ff328299222707f4e63db0cc355677e83af889`. R1 production revision
`d0dbf762ab1c0d12a3e2ed21e0e338f09170c314` is an ancestor. The archived D1
and D2 qualification tree IDs are respectively
`4cd202db3b567c9893b00ba947acd802fe362456` and
`74870265e31fbe7f5633962174c227159b3ddc87`.

The managed decoder now rejects contradictions between the text and JSON
schema forms. The two forms are serializations of the same Dogwood fragment.
Cedar Core 4.11.0's `validator/cedar_schema/fmt.rs` emits a block for each
named namespace and declarations for common types, entity types and actions
in the unnamed namespace. `validator/json_schema.rs` requires the entity and
action maps and omits an empty common-type map. An empty fragment or an empty
unnamed namespace emits the empty string. A named namespace still emits a
block with zero declarations. The check uses these structural properties
independently of the policy count. Qualification compares the two schema
forms semantically against the trusted bridge.

The managed loader only initializes from the default assembly load context.
Its singleton retains one native reference after ABI and identity checks
succeed and caches an initialization failure after freeing its temporary
reference. Calls continue to copy and free the native response in the
existing `finally` path. The Rust differential consumer also retains its
successful reference. Native consumers own their loader references and must
leave the D2-enabled image resident until process exit. The [owner record](d2-r2-owner-decisions.md)
and [native contract](architecture/native-interop.md#d2-image-lifetime) define
the hosting boundary.

## Original reproductions

The archived `cedar-policy` decoder mutation changed only
`cedarSchemaText` to the empty string. On Windows, the unchanged R1 decoder
accepted the mutated successful export on .NET 8 and .NET 10, while
accepting three valid controls and rejecting 18 other faults. See the
[R1 decoder output](evidence/d2-r2/r1-windows-decoder.txt).

The four original C modes ran in Ubuntu 24.04 (WSL2, glibc 2.39 and Valgrind
3.22.0) against the R1 Linux producer image from run 44245964368. The observed
allocation classes follow. Counts are in bytes. Every C buffer assertion
and reset passed.

| Mode | Calls | Definite | Indirect | Possible | Still reachable | Error contexts |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Loader only | 0 | 0 | 0 | 0 | 0 | 0 |
| D1 parse then unload | 32 | 0 | 0 | 0 | 152 | 0 |
| D1 and D2 resident | 128 | 0 | 0 | 13,136 | 33,515 | 0 |
| D1 and D2 then unload | 128 | 3,460 | 39,456 | 0 | 152 | 120 |

All 50 original possible-loss blocks have allocation stacks rooted in
Cedar's process-global `LazyLock` initialization. The evaluator's extension
objects account for 11,864 bytes in 47 blocks. Its extension lookup tables
account for 712 bytes in two blocks. The validator's extension schema table
accounts for 560 bytes in one block. The stacks pass through Dogwood event
schema derivation and Cedar validation. The classification is consistent with
interior references to retained allocations. It remains a Memcheck
possible-loss classification. The unsupported unload diagnostic also shows
the lazy Cedar schema and entity-name parsers losing their roots.
[Symbolized excerpts](evidence/d2-r2/r1-linux-valgrind-excerpts.txt).

## Resident workload

`eng/d2-r2/resident.c` loads and verifies the image once in each process.
After 32 warm-up rounds it runs 32, 256 or 2,048 rounds on one thread or
across four fixed workers. Every round runs D1 parse, D2 successful
validation and lowering, rejected D2 validation and a malformed envelope.
It checks status and output before releasing each result and checks the
reset immediately. It records native glibc heap statistics and peak RSS
before load, after identity, after first use, after warm-up and after the
workload. Valgrind collects complete stacks separately.

The local investigation used the same R1 image as the original C controls.
R2 changes the managed loader and a Rust documentation comment. The producer
matrix also runs the resident workload on its freshly built R2 image.
[Local measurements](evidence/d2-r2/local-linux-resident.txt).

## Producer results

The implementation, native residency probe and archived corpus adapter
are checkpointed. The full producer matrix and handoff evidence are pending.

## Independent requalification scope

Repeat the original mutation and empty/nonempty schema controls. Exercise
empty named and unnamed namespaces and an empty policy set with a nonempty
schema. Reparse both schema representations and validate them against the
lowered set. Requalify the default-context and process-termination boundaries,
including repeated access under JIT and NativeAOT, custom-context rejection,
concurrent C and managed ownership, and resident Linux memory scaling. Keep
mixed-use unload as a diagnostic. Verify D1 compatibility across the supported
matrix and confirm the warning-location projection during the review.
