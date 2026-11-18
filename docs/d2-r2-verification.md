# DOGWOOD-SHARP-D2-R2 producer verification

**A. IMPLEMENTED, READY FOR INDEPENDENT REQUALIFICATION**

D2 remains remediation required pending D2-QUAL-02 and separate owner
acceptance.

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

R1 used a lazy default instance alongside a separately callable internal
loader and a disposable native handle. Disposal could release the image
while other code retained its function pointers. The R2 managed loader only
initializes from the default assembly load context.
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
3.22.0) against the R1 Linux producer image. The observed allocation classes
follow. Counts are in bytes. Every C buffer assertion and reset passed.
[R1 producer CI run 44245964368](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44245964368).

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

At 32, 256 and 2,048 measured rounds, every Memcheck process reported zero
definite or indirect loss and zero errors. The sequential mode retained a
constant 13,756 possibly lost bytes and 43,045 still-reachable bytes. The
fixed four-worker mode retained a constant 15,084 possibly lost bytes and
58,637 still-reachable bytes. The original extension records remained
present. The broader workload additionally initialized Cedar's 288-byte
reserved-identifier lookup and regex DFA caches totaling 332 bytes
on the sequential thread. Four added workers accounted for another 1,328
bytes with the same regex stack family. This explains the fixed differences
observed between the measured cases without reclassifying or suppressing
any of them. At the largest Memcheck size each process checked 10,401
releases, including 8,321 populated response buffers.
[Additional resident stacks](evidence/d2-r2/resident-additional-stacks.txt).

Direct native runs at 8,192 and 32,768 rounds extended the allocator
observations. At 32,768 rounds the sequential and concurrent processes
reported 251,056 and 239,808 bytes of glibc in-use heap respectively, with
peak RSS approximately 10 and 11 MiB. Relative to 32 rounds, the heap
figures changed by tens of KiB across a 1,024-fold increase in the
measured workload. The exact Memcheck retained totals stayed flat through
its tested sizes. These results support bounded initialization and
per-worker caches for this workload. The measurements establish neither a
general quota nor a platform-wide memory bound.

## Producer results

The production source and test commit is
`d4d62b4825d2cb0d43554e6bb34b9d6a7dc238fd`, tree
`a582171f4b213464eab3a6a459aa2bc3540e80ab`.

Local Windows verification passed the locked build, format and lint checks,
13 Rust tests, 69 managed tests per framework, 39 D2 differential cases
with 78 replies, and producer JIT and NativeAOT smokes under both frameworks.
The D1 adapter passed 88 C ABI checks, 73 source and 131 wire differential
cases, 114 managed scenarios per framework and both NativeAOT smokes.

The disposable D2 adapter runs the archived oracle, corpus and boundary
controls. It verifies the preserved qualification trees and fails if the
original partial reply is accepted. Its Linux gate retains the old unload
control as a diagnostic. On Windows it passed 35 cases per framework and
execution mode, 70 ABI replies, 13 Cedar round trips, 216 concurrent calls,
26 ABI transport controls, five injected fault detections, and three
managed transport faults per framework. The original decoder mutation
changed from acceptance on R1 to rejection on R2 under both frameworks.
Its three positive controls and 18 other rejection controls passed on both.
[Windows verification excerpts](evidence/d2-r2/local-windows-results.txt).

The R2 managed JIT smoke also ran locally in Ubuntu 24.04 with the verified
R1 native image. The ELF loader recorded a single open with reference count
one per process on .NET 8 and .NET 10.
[Linux managed loader trace](evidence/d2-r2/local-linux-managed-loader.txt).

The hosted producer matrix passed on the source and test commit above.
Its artifact manifests match the downloaded images. The evidence commits
following that revision contain only the report, finding tracking and
supporting evidence.
[Producer CI run 44289567088](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44289567088).

| RID | .NET 8 | .NET 10 | Locked graph | CI artifact |
| --- | --- | --- | ---: | --- |
| `win-x64` | Pass | Pass | 181 packages | [14421682401](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44289567088/artifacts/14421682401) |
| `linux-x64` | Pass | Pass | 177 packages | [14423170564](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44289567088/artifacts/14423170564) |
| `osx-arm64` | Pass | Pass | 179 packages | [14420509954](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44289567088/artifacts/14420509954) |

Every cell passed 69 managed and 114 version-aware D1 scenarios with zero
failures or skips, the producer JIT and NativeAOT smokes, and 35 archived
D2 corpus scenarios in each execution mode. Per RID, 13 native Rust tests,
88 D1 C checks, the 73-source and 131-request D1 differential, and the
39-case D2 differential passed. The archived D2 C comparison passed 70 ABI
replies, 13 Cedar round trips, 216 concurrent calls, 26 transport controls
and five sensitivity controls. In all six cells the managed boundary accepted
three valid controls and rejected 18 other malformed replies, three transport
faults and the original QD2-01 mutation. Locked dependency comparisons, Rust
and .NET formatting, lint, build and image verification passed.
[Matrix evidence](evidence/d2-r2/ci-matrix.json).

The hosted Linux build had the same native SHA-256 as the R1 image used in
the local investigation. All six hosted resident sweeps passed, with zero
definite or indirect loss and zero Memcheck errors. The sequential possible
and still-reachable totals stayed at 13,756 and 42,461 bytes respectively.
With four workers they stayed at 15,084 and 58,053 bytes. Peak native RSS
was 16,348 KiB in the largest sequential run and 17,116 KiB in the largest
concurrent run. The managed ELF loader trace counted one open. The largest
C runs each verified 10,401 releases. None of these runs used custom
suppression rules.

| Finding and control | Unchanged R1 reproduction | R2 producer reproduction |
| --- | --- | --- |
| QD2-01 partial-schema mutation | Accepted on both local Windows frameworks and all six archived cells | Rejected with `MalformedResponse` on all six cells. Valid empty and nonempty bundles pass. |
| QD2-03 original mixed workload held resident | 0 definite or indirect bytes, 13,136 possibly lost bytes | 0 definite or indirect bytes, the same 13,136 possibly lost bytes. The larger supported workloads retained stable totals. |
| QD2-03 original mixed workload followed by unload | 3,460 definite and 39,456 indirect bytes after every release passed | The same two byte totals after every release passed. Kept as an unsupported diagnostic. |

Memcheck remains a bounded empirical check under the measured Linux
workloads. The hosting contract keeps the library resident and the
qualification continues to record the upstream unload losses.
[Linux residency and diagnostic evidence](evidence/d2-r2/ci-linux-resident.json).

The archived D1 and D2 qualification trees, D1 acceptance, R1 verification
and archived workflows match the starting revision. All ten archived D2
evidence checksums verified, and the committed lockfiles match the baseline.
[Retained R2 evidence checksums](evidence/d2-r2/SHA256SUMS),
[full local log digests](evidence/d2-r2/local-raw-SHA256SUMS).

## Reproducing the producer checks

Use the pinned toolchains on the host for the target RID. Run
`eng/Verify.ps1`, `eng/Verify-D1OnD2.ps1` and `eng/Verify-D2R2.ps1` in
that order with the same `-RuntimeIdentifier`. The last script expects a
clean checkout and reuses the verified asset. If NativeAOT restore added a
RID-specific section to the library lock, restore that generated change
before the archived corpus run.

On Linux, install Valgrind and its matching libc debug symbols. Build and
run the resident consumer with the asset produced by `eng/Verify.ps1`.

```bash
mkdir -p artifacts/d2-r2/resident
cc -std=c11 -D_GNU_SOURCE -O2 -Wall -Wextra -Werror eng/d2-r2/resident.c \
  -ldl -pthread -o artifacts/d2-r2/resident/probe
python3 eng/d2-r2/measure-resident.py artifacts/d2-r2/resident/probe \
  artifacts/native/runtimes/linux-x64/native/libdogwoodsharp_bridge.so \
  artifacts/d2-r2/resident
```

## Independent requalification scope

Repeat the original mutation and empty/nonempty schema controls. Exercise
empty named and unnamed namespaces and an empty policy set with a nonempty
schema. Reparse both schema representations and validate them against the
lowered set. Requalify the default-context and process-termination boundaries,
including repeated access under JIT and NativeAOT, custom-context rejection,
concurrent C and managed ownership, and resident Linux memory scaling. Keep
mixed-use unload as a diagnostic. Verify D1 compatibility across the supported
matrix and confirm the warning-location projection during the review.
