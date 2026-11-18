# DOGWOOD-SHARP-D2-QUAL-01

**C. REMEDIATION REQUIRED**

QD2-01 and QD2-03 failed mandatory qualification controls. Defer D2 owner
acceptance until the managed artifact boundary and Linux native lifecycle are
resolved and requalified. DL-01 and DL-05 are recommended for owner closure
within their original scope.

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
- [x] Collect all three RIDs and both frameworks, including failed gates
- [x] Record findings, evidence hashes and owner recommendations

## Executed results

| RID | .NET 8 | .NET 10 | Native qualification |
| --- | --- | --- | --- |
| `win-x64` | Integration passed. QD2-01 reproduced. | Integration passed. QD2-01 reproduced. | Passed |
| `linux-x64` | Integration passed. QD2-01 reproduced. | Integration passed. QD2-01 reproduced. | QD2-03 reproduced |
| `osx-arm64` | Integration passed. QD2-01 reproduced. | Integration passed. QD2-01 reproduced. | Passed |

Each framework and RID ran 44 managed regression tests and 114 D1 consumer
scenarios with zero failures or skips in those suites. The independent
consumer passed 35 corpus cases each under JIT and NativeAOT, including an
immutable snapshot control per mode. The JIT decoder accepted three valid
controls and rejected 18 malformed replies. Three additional transport tests
rejected invalid JSON, invalid UTF-8 and an oversized response. The one
remaining malformed reply was accepted in all six cells and is QD2-01.

Each RID passed 13 native tests, 39 producer D2 cases with 78 ABI replies,
88 version-aware D1 C checks, and the D1 differential of 73 sources and 131
wire requests. Independently authored D2 checks compared 70 ABI replies
across 35 cases, including 12 collision cases. Cedar validated 13 exports
against both schema forms. Another 216 concurrent calls and 26 envelope,
size and lifecycle controls passed per RID. Both the D1 and D2 response
size ceilings were exercised with status 6 and empty output. Locked builds,
formatting, linting, manifest checks, and producer and D1 consumer smokes
passed. The original D2, qualified bridge and oracle resolved the same
third-party dependency graph and features within each RID. The graph had 181
packages on Windows, 177 on Linux, and 179 on macOS.

The [initial run 44258963165](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44258963165)
tested harness commit `838a32d3678dfdc79e769b4c6f2ca58265bdfeeb` and tree
`9599051e64eccc05fbc0606bd049f364ddeb6419`. It completed Windows and macOS.
The Linux memory gate failed before the independent managed consumers ran.
The [Linux follow-up 44280904593](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/44280904593)
tested harness commit `2b55d471df9a91764692d653621b12e1a465f054` and tree
`1619288f1d1a303a796683676cafa0b5074cc750`. It completed the remaining managed
checks and failed the memory gate after recording all controls.
Both checkouts preserve the commissioned production tree. The evidence matrix
records exact toolchains, runner images, native image hashes, and CI artifact
digests. Retained Valgrind excerpts are whitespace-normalized. The original
log hashes are recorded separately. See the [evidence matrix](evidence/ci-matrix.json),
[hash manifest](evidence/SHA256SUMS), and [findings](findings.md).

## Sensitivity

The independent comparator caught all five injected response faults on every
RID. It detected skipped collision checks through the collision stage,
premature export through `artifactsExported`, a dropped distincter through
rule identities, and incorrect dispatch and an omitted artifact through the
operation-specific response shape. QD2-01 exercises a present schema text
field whose value is empty.

## Existing findings and D3 handoff

| Finding | Qualification recommendation |
| --- | --- |
| DL-01 | Recommend closure. Independently reproduced the upstream String-to-Boolean overwrite on valid input. Both wrapper operations rejected this and the equivalent, optional, referenced, inherited and distinctered conflicts. Unrelated declarations remained usable. |
| DL-02 | Retain the documented limits. Verified separate 1 MiB decoded fields, a 256-byte distincter, a 6,291,520-byte encoded request ceiling and the 2 MiB and 8 MiB response ceilings. These are byte limits. |
| DL-03 | Retain the null-warning-location wire behavior. Correct its upstream rationale per QD2-02. |
| DL-04 | Retain the scope limitation. Policy-store conflict handling is outside D2. |
| DL-05 | Recommend closure. Independently reproduced the version-aware adapter and checked its disposable diff. Only the named C, Python and managed control files differ in the copy. The archived D1 tree hash remains `4cd202db3b567c9893b00ba947acd802fe362456`. |

D3 must account for trusted artifact completeness, the native library lifetime
contract, and the separate rule IDs when consuming Cedar text. Reparsed Cedar
text receives fresh parser-generated IDs. Temporal exports still require
trusted generated context values. Runtime authorization, temporal execution
and policy-store integration are outside this qualification.

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
