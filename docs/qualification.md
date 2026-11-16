# Qualification strategy and acceptance matrix

Reference conformance asks whether decisions/IDs/diagnostics match the pinned
upstream implementation on valid inputs. Security qualification asks whether the
wrapper and consuming system resist attacks and enforce their stated guarantees.
One does not imply the other. Preserve original fixtures and distinguish accepted,
experimental, pending-fix and unsupported cases in a versioned manifest.

| Gate | Evidence required | Earliest milestone | Acceptance owner |
| --- | --- | --- | --- |
| Build foundation | Locked restore, Release build, formatting and load tests for both TFMs | D0 | Owner after local/CI evidence |
| Parser/schema fidelity | Upstream examples; invalid/adversarial cases; locations and augmented schema goldens | D1-D2 | Independent qualifier then owner |
| Cedar parity | Cedar-only corpus; entity/action hierarchies; default deny/forbid/errors; ID mapping | D3 | Independent qualifier then owner |
| Temporal parity | Pinned temporal corpus; formerly/previous/since, endpoints, aggregation, ordering, pins | D4-D5 | Independent qualifier then owner |
| Mixed/provider parity | Mixed rules/macros; failure/type cases; provider purity; error contract classification | D5-D6 | Independent qualifier then owner |
| Native boundary | Buffer ownership, panic translation, disposal and concurrency, memory checks | D1/D5/D7 | Independent qualifier then owner |
| Security | Forged bindings, malformed/replayed events, tenant leakage, resource exhaustion, diagnostic leakage | D7 | Independent security review then owner |
| Distribution | Fresh package-only consumers, archive/native manifest verification, licenses, symbols, AOT/trim | D8 | Independent qualifier then owner |
| Publication | Exact package hashes, successful qualified run, explicit authorization, OIDC refusal paths | D9-D10 | Owner |

Required release matrix: win-x64/windows-2025, linux-x64/ubuntu-24.04 and
osx-arm64/macos-15, each net8.0 and net10.0. Record actual OS image and compiler
versions, not just labels. No support claim for unexecuted combinations.

For each implemented feature include a positive case, a negative control that
changes the verdict, boundary/invalid input, and differential comparison. Compare
decisions and determining ID sets; compare diagnostic category/source mapping
where available, and record unstable message text separately. Security tests
must also assert state after failure/cancellation, not only the returned result.

Retain commit/tree, source pins, lock hashes, feature set, commands, environment,
fixture counts and pass/fail/skip reasons. Any unexplained divergence blocks the
feature. Skips do not count as passes. Review provider undefined-behavior cases
as implementation regressions/strict-wrapper controls, not language guarantees.

At D0, only foundation smoke tests and six research fixtures have executed
locally. No independent reviewer/subagent was engaged and no production
conformance claim is made. See [verification](verification.md) for actual results.
