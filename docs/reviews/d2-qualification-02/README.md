# DOGWOOD-SHARP-D2-QUAL-02

## Reviewer and baseline

Reviewer session `DOGWOOD-SHARP-D2-QUAL-02 / 2026-11-18 / Codex follow-up`.
This review follows R2 in the same conversation and has access to the
producer context. The reviewer is an AI. Independence is at the level of
test design, fixtures, consumers, upstream oracle and observed evidence.
Producer results serve as supporting material.

| Item | Identity |
| --- | --- |
| Commissioned and live `main` at preflight | `8ff7344432be652d907598cf1d3b862a5d527222` |
| Commissioned tree | `273ba9a8d1b16449690a118fe66e7dcfffc1f5bc` |
| Producer-tested R2 | `d4d62b4825d2cb0d43554e6bb34b9d6a7dc238fd` |
| Producer-tested tree | `a582171f4b213464eab3a6a459aa2bc3540e80ab` |
| R1 production / Q1 final | `d0dbf762ab1c0d12a3e2ed21e0e338f09170c314` / `b79fdffa07ac45cb49c5ee2ef100299f2d2adcf5` |
| Dogwood / Cedar | `929f49e7305534ac61ef06cf8e47269c596dc851` (1.1.2) / 4.11.0 |
| Producer CI | [44289567088](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44289567088) |

The checkout was clean. Each listed historical revision is an ancestor in
the commissioned order. The producer-to-commissioned delta contains only
the producer report, finding tracking and R2 evidence. All 19 entries in the
two historical SHA-256 manifests verified. The D1 and Q1 qualification trees
retain their exact IDs `4cd202db3b567c9893b00ba947acd802fe362456` and
`74870265e31fbe7f5633962174c227159b3ddc87`. The three supplied producer native
images matched their manifests and the hosted producer run completed for the
correct commit.

## R2 delta

The managed implementation changed two affected requirements. The lower
decoder checks that the JSON structure and Cedar text agree on whether
schema text is required. The verified native load is a singleton restricted
to the default managed load context. Disposal was removed from the internal
bridge. A failed load releases its temporary reference and the initialization
failure remains cached. The Rust differential loader now follows the
resident contract. The last native production source delta is a comment
clarifying warning projection. The C symbols, layout, operations, public
managed surface, dependency versions and feature selections retain the R1
contract. The D2 status documents and producer CI changes are within the
commissioned scope. DL-01 and DL-05 retain their owner closure.

The requalification gates cover artifact completeness, schema equivalence
under the pinned Cedar parser, original mutation sensitivity, default and
custom loader contexts, response ownership on all return paths, native
residency and memory scaling, warning diagnostics, and preservation of D1
and the archived D2 corpus. The archive and production source are exercised
through a separate detached checkout of the commissioned revision.

## Progress checkpoint

- [x] Verify repository identity, ancestry, live synchronization and historical evidence.
- [x] Review the producer delta and inspect pinned Cedar serialization and regex cache source.
- [x] Exercise the new C worker probes locally, including expanded thread churn.
- [x] Complete local Windows JIT, NativeAOT, Cedar, and managed sensitivity checks.
- [ ] Complete and attest the hosted six-cell matrix.

Local Windows reproduced QD2-01 on separately built R1 for both frameworks.
R2 passed seven real policy/schema fixtures and eight accepted replay
controls and rejected 35 responses in each tested mode. The local Linux
sweep completed with zero definite and indirect loss and zero errors in
resident processes. The additional thread-churn runs plateaued after the
64-worker sample through 1,024 completed workers, with at most two live
workers. The original unsupported mixed-unload diagnostic reproduced
3,460 definite and 39,456 indirect bytes on the original image.
[Windows observations](evidence/local-windows.json), [Linux observations](evidence/local-linux.json).

The hosted matrix was dispatched on the qualification harness revision
`67674b1b58678ce686ea31ba734a735e7a47bd6c` and tree
`b33d704f3ab01da8770d0a50ee99b32e5ac6987b`. It uses a separate worktree at
the commissioned production revision.
[Qualification CI run 44333575005](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44333575005).

## Schema completeness and warning projection

Cedar Core 4.11.0's fragment formatter prints a block for each named
namespace, including one with zero declarations. The unnamed namespace
prints its actions, entities and common types. An empty unnamed namespace
and an absent one both produce empty text. Its JSON serialization requires
the action and entity maps and omits an empty common-type map. Those facts
were read in the pinned `validator/cedar_schema/fmt.rs` and
`validator/json_schema.rs`.

The new oracle parsed both native schema forms into Cedar fragments and
compared their canonical structures. It also normalized the semantically
empty unnamed entry in a separately constructed positive control. It
compiled the two schemas independently and validated the corresponding
Cedar policy sets against both. It compared the two policy bodies and
verified rule indexes, returned IDs, and corresponding authored source
texts. The only policy body normalization removes an explicit
`when { true }` tautology. Reparsed Cedar uses fresh IDs. The reviewed
decoder performs the cheaper structural completeness check at the response
boundary.

The warning-only fixture succeeded. Re-keyed Cedar validation emitted a
warning with a diagnostic label. The native and managed results retained
the Cedar category and exact message and exposed a null location. The
corrected documentation explains that source provenance and byte-offset
rebasing must be verified before mapping these labels. The bridge Rust
delta for this behavior consists of a comment.

## Lifetime and sensitivity

Eight simultaneous callers raced the first managed version query, then
each real process completed 128 repetitions of mixed D1 and D2 access.
Both JIT context variants attempted a private copy after default-context
initialization and verified continued default-context service. NativeAOT
exercised the concurrent initialization and lifetime with no custom-context
requirement. Windows and macOS use module presence checks. Linux uses
the ELF loader trace to observe a single open and finalization at process
exit. The availability probes use `RTLD_NOLOAD` with balanced observer
references on POSIX runners.
[glibc loader flags](https://raw.githubusercontent.com/bminor/glibc/release/2.39/master/bits/dlfcn.h),
[Darwin loader flags](https://raw.githubusercontent.com/apple-oss-distributions/dyld/main/include/dlfcn.h).
The local Linux JIT runs independently reproduced the single-open trace
on both runtimes. [Local Linux loader traces](evidence/local-linux-jit.json).

In each fault process the independent substitute audited response pointers
and lengths. There were 44 allocated and released populated buffers,
including the identity and fault paths. A malformed native identity was
freed and its temporary image reference released. Replacing the bad
identity with a valid one during that process left the same cached
exception across nine total attempts and one native initialization.

| Sensitivity control | Independent detection |
| --- | --- |
| Original incomplete-schema acceptance | Rebuilt R1 accepted the archived mutation. R2 reports `MalformedResponse` through the public API. |
| Private-context initialization | Removing the guard in a disposable build reached the private-context acceptance assertion. |
| Omit one response release | An oversized reply was rejected by the mutated managed call, but the independent auditor observed exactly its missing release. |
| Unload after mixed D2 calls | The isolated C negative control released all buffers before unloading. Memcheck then reported definite and indirect loss. |
| Archived Q1 response faults | The archived comparator covers collision bypass, early export, lost distincter, wrong dispatch and a partial artifact set. |

## Native memory

The independent C workload uses an unnamed schema with scoped Cedar
policies and exercises D1 parse, successful validation and lowering,
rejected validation and malformed input. Each successful call transfers
an independent buffer and each call verifies the output reset after
release. The largest sequential and fixed-worker resident processes each
checked 10,401 releases after 32 warm-up rounds, including 8,321 populated
buffers. They held one loader reference. The old mixed-unload reproduction
and the new negative control use separate processes.

Locally, sequential and four-fixed-worker modes showed stable Memcheck
classifications at 32, 256 and 2,048 rounds. The tables of extension
objects, extension lookups and schemas account for the original 13,136
possibly lost bytes. The broader workload reaches Cedar's reserved IDs
and regex cache paths. In the churn test, the 8- and 64-worker process
totals differed despite the same cap of two concurrent workers. From 64
through 1,024 completed workers, the possible-loss and still-reachable
totals were constant at 19,732 and 99,243 bytes. The possible-loss
increase at the earlier samples is in regex cache stack families.

Pinned `regex-automata` 0.4.18 uses a fast-path owner and eight cache
stacks. Worker IDs select a stack by modulo and the acquisition path only
allocates a cache on an empty stack. The guard returns reusable caches
to a stack. This accounts for caches acquired as new workers visit the
stacks and is consistent with the observed plateau. It does not attribute
the growth to unreleased response buffers. Direct native heap and peak
RSS are recorded separately. No custom Memcheck suppressions were used.
The results characterize these measured workloads and worker bounds.

The documented process-lifetime contract is consistent across the managed
API, native ABI, owner decision, security and roadmap records. The host
shares DogwoodSharp from the default managed load context. Native callers
keep the D2-enabled library loaded through process termination and release
each response immediately. Unloading or dynamically replacing an initialized
image, loading a private managed copy, and in-process reclamation are
outside the supported boundary. Process isolation is a future hosting option.

## Qualifications to carry into D3

- Preserve default-context managed sharing and process-lifetime native
  residency for the D2 image. Plan a separate process boundary if a host
  needs replacement or native-memory reclamation.
- Preserve null warning locations until a separately verified mapping can
  establish authored-source provenance and rebase UTF-8 byte offsets.
- Preserve each 1 MiB decoded source or schema text limit, the 256-byte
  distincter limit, the separate 6,291,520-byte encoded request limit, and
  the 2 MiB D1 and 8 MiB D2 response limits. The limits and measured
  workloads establish no general memory quota or wall-time guarantee.
- Use the returned rule IDs and mappings. Cedar policy text can be reparsed
  and validated with either schema representation, and reparsing assigns
  new IDs. A temporal export that requires generated context still needs
  trusted generated values.

## Reproduction

Create a clean detached worktree at the commissioned R2 revision. From a
qualification checkout run the following with the pinned Rust and .NET
tools, Python 3, PowerShell, and the native compiler and linker for the host.
Install Valgrind with matching libc debug symbols on Linux.

```powershell
git worktree add --detach ../dogwood-q2-production 8ff7344432be652d907598cf1d3b862a5d527222
./docs/reviews/d2-qualification-02/run.ps1 -Repository ../dogwood-q2-production -RuntimeIdentifier linux-x64 -Sensitivity
```

Use the corresponding RID on Windows or macOS. `-UseExisting` reuses a
previously verified local build and archived fixtures at the same revision
for faster managed and oracle iteration. Without that switch the runner
repeats the managed regressions, version-aware D1 regression, Rust tests
and D2 differential, and archived D2 corpus. The qualification workflow
publishes logs and manifests for each RID. The mutators build source from
disposable copies and execute in isolated test processes.
