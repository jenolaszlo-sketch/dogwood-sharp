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
