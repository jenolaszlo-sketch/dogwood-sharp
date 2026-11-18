# DOGWOOD-SHARP-D2-R1 producer verification

## Baseline and pins

| Item | Value |
| --- | --- |
| Starting `main` and `origin/main` | `8f394da2b501ea496ffa96856357f87fd9538c45` |
| Starting tree | `6455d06c1a14bc77ebc3f751931e2dcc8a55fecd` |
| Locally verified implementation | `d66d22519f76f8816e9f323c675a09cf3ba5c086` |
| Locally verified implementation tree | `ee3d93853575cf653176ad9c54f234f2093adc9f` |
| Dogwood | 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851`, with no Dogwood features |
| Cedar | 4.11.0 with datetime, decimal and ipaddr |
| Rust | 1.99.1 (`58cf8f9c3`), host `x86_64-pc-windows-msvc` |
| Windows local | 10.0.26200, .NET SDK 10.0.404, .NET runtimes 8.0.34 and 10.0.15 |
| Bridge lockfile SHA-256 | `ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa` |
| Local bridge image SHA-256 | `7b40e9a32fc90753748887965ea7728ad7721991dba4455cc25b3b58b60f5972` |

The starting checkout was clean and its commit and tree matched the owner
baseline. R1 added a direct dependency on Cedar Core 4.11.0, already resolved
through Dogwood, for access to Cedar's typed schema resolution. The dependency
versions and resolved features match the baseline.

## Windows producer checks

`./eng/Verify.ps1 -RuntimeIdentifier win-x64`

| Check | Result |
| --- | --- |
| Native build, lock and feature checks | Pass |
| Rust formatting and Clippy, bridge and differential | Pass |
| Rust Release tests | 13 passed (5 D1, 8 D2), 0 failures, 0 ignored |
| Locked managed restore, Release build and format check | Pass, 0 build warnings |
| Managed .NET 8 | 44 passed (13 D1, 31 D2), 0 failures, 0 skips |
| Managed .NET 10 | 44 passed (13 D1, 31 D2), 0 failures, 0 skips |
| D2 differential | 39 upstream cases and 78 ABI replies, 0 failures |
| Producer JIT and NativeAOT smokes | Pass on both frameworks |

The differential checks the original 37 expectations against the pinned
upstream crate. The original DL-01 fixture still shows an upstream String
declaration replaced with a required Boolean. Both wrapper operations return
the collision diagnostic for `policy_0__temporal_0` on `App::Action::"read"`.
The exported rule mapping and Cedar strings are empty. Two added cases compare
a validated temporal export with a distincter and successful validation with
warnings. Managed checks cover distincter failures and successes, a no-export
result after successful lowering, parsed and action-schema stage outcomes,
immutable snapshots, concurrent calls, and contradictory native replies.

## D1 consumer regression

`./eng/Verify-D1OnD2.ps1 -RuntimeIdentifier win-x64`

The archived D1 qualification harness, workflow, reports, and acceptance
record are byte-for-byte identical to the R1 baseline. The adapter copies the
tracked harness into ignored artifacts and edits the following controls there.

| Historical control | D2 adaptation |
| --- | --- |
| C `reject_operation_two` | Require status 3 for operation 9 and status 5 for a D1-shaped operation-2 envelope. Add operation-2 empty-input status 1 and a valid distinctered validation call with buffer release and no export. |
| C `reject_operation_precedes_input` | Use operation 9 to exercise rejection ahead of empty input. Also require operation 9 to precede the encoded size ceiling. |
| Python `wire-operation-2` | Exercise operation 9 with status 3 and the operation-2 envelope with status 5. |
| Managed `AssertPublicSurface` | Require the original D1 types and members plus the exact D2 additions. Retain the D1 immutability assertions. |

All other D1 harness controls run against the D2 asset. Local results follow.

| Check | Result |
| --- | --- |
| Independent manifest and image hash verification | Pass |
| C ABI consumer, ownership and adapted controls | 88 passed, 0 failures |
| D1 parser differential | 73 sources, 131 wire requests, 0 failures, 0 message or wire-length mismatches, response ceiling exercised |
| D1 managed and loader consumer | 114 passed on .NET 8, 114 passed on .NET 10, 0 failures |
| D1 consumer NativeAOT | Pass on both frameworks |

Reproduce historical D1 at implementation commit
`85298534e55ad0a5403277c54872842960a516e0`. In an isolated checkout of that
revision, place the exact preserved `docs/reviews/d1-qualification/harness`
directory from the R1 baseline at the same path. Run the
[historical reproduction commands](reviews/d1-qualification/README.md#8-reproduction)
against assets built from that revision. The D2 adapter is for R1 assets.

## CI and independent review

The [producer CI workflow](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/workflows/ci.yml)
runs the producer and D1 consumer checks on Windows x64, Linux x64 and macOS
Arm64 under both frameworks. Per-RID artifacts record the exact revision, OS
image, test counts, locked graph, manifest and D1 regression reports. The Linux
job runs Valgrind on the D1 and D2 native buffer tests and the C consumer.

Independent D2 qualification should rederive the upstream expectations,
exercise explicit, transitive group and unconstrained scopes with shared and
optional fields, and challenge the exact policy/schema/distincter export gate
with errors and malformed native replies. Verify warning preservation, D1
wire and ownership compatibility, resource limit independence, and JIT and
NativeAOT on all six platform/framework combinations. DL-01 and DL-05 have
producer evidence and await independent review and owner acceptance.
