# DOGWOOD-SHARP-D2-QUAL-01

Qualification session `DOGWOOD-SHARP-D2-QUAL-01 / 2026-11-18 / Codex follow-up`.
This qualification is performed in a separate review turn in the producer's
Codex conversation. The reviewer has the producer context. Independence applies
to the test corpus, oracle, consumers and acceptance criteria. The review uses
the production bridge only as the subject under test and treats producer reports
and regressions as supporting evidence. This is an AI qualification review.

## Baseline

| Item | Identity |
| --- | --- |
| Starting commit, local and remote `main` | `d0dbf762ab1c0d12a3e2ed21e0e338f09170c314` |
| Starting tree | `21ffc920e01a0c9875e33c572a947aff502d9b25` |
| Original D2 | `8f394da2b501ea496ffa96856357f87fd9538c45` |
| Dogwood | 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851` |
| Cedar | 4.11.0 |
| Rust / .NET SDK | 1.99.1 / 10.0.404 |
| Producer CI | [44245964368, attempt 1](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44245964368) |

The repository was clean. All three producer jobs succeeded for the expected
commit. The accepted D1 records and historical qualification directory match
the original D2 baseline. The only bridge lockfile delta from original D2 is
a Cedar Core edge from the bridge to the already locked package.

## Progress

- [x] Repository and producer CI preflight
- [x] Reproduce producer verification and D1 consumer regressions on Windows
- [x] Validate the independent corpus, collision and exact-bundle checks locally
- [x] Exercise the managed response boundary and immutable snapshots locally
- [x] Exercise native resource ceilings, concurrency and sensitivity controls locally
- [ ] Complete the three-RID, two-framework qualification matrix
- [ ] Record findings, evidence hashes and owner recommendations

## Reproduction

Start with a clean qualification checkout based on the commissioned production
tree. Use the pinned Rust and .NET toolchains, Python 3.11 or later, PowerShell
7, and a C compiler. Configure MSVC and the Windows SDK on Windows. Install
Valgrind on Linux. The run assembles reproducible local evidence under the
ignored `artifacts/d2-qualification/<rid>/` directory.

```powershell
./docs/reviews/d2-qualification/run.ps1 -RuntimeIdentifier win-x64
```

Use `linux-x64` or `osx-arm64` on the corresponding host. For a local rerun,
`-UseExistingProducerAssets` reuses the freshly verified producer image and
managed assemblies. Qualification CI runs the full regression and harness
sequence with a read-only workflow token.

`harness/corpus.py` contains independently authored requests and intended
outcomes. The Rust oracle derives concrete target actions and field names,
inspects typed original and augmented contexts, and uses Cedar to reparse and
validate exports in both schema formats. The Python consumer compares both C
ABI operations with the oracle and the R1 restrictions. The public managed
consumer runs the same qualified cases under JIT and NativeAOT. Its JIT
decoder mode probes the real response reader, and an isolated native
substitute checks UTF-8, JSON, and response-size rejection at the transport.

The sensitivity checks alter disposable copies of observed replies. The
independent comparator must detect each injected fault. The qualification
workflow never mutates production code. The managed boundary evidence reports
the observed outcome of the partial-schema case explicitly. See [findings](findings.md).
