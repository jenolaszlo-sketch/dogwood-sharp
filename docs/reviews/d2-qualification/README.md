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
- [ ] Reproduce producer verification and D1 consumer regressions
- [ ] Independent conformance, collision and exact-bundle checks
- [ ] Managed response trust boundary and immutable snapshots
- [ ] Native resource limits, concurrent ownership and sensitivity controls
- [ ] Complete the three-RID, two-framework qualification matrix
- [ ] Record findings, evidence hashes and owner recommendations
