# DOGWOOD-SHARP-D3-QUAL-01-R4-Q2 — Independent final requalification

**C. REMEDIATION REQUIRED** · 25 November 2026 · Independent qualification

Keep D3 open. The reviewed ED-06 entry correctly authorizes exactly two cases and three comparison fields. Four additional C-23 observations produced a clean upstream Deny and D3 Allow on both frameworks, outside that approval. The harness rejected all four when exercised, but the 97-case corpus omits them. A separate reviewer control returned a passing C-01 family with a clean Deny as its sole direct-Allow observation. The C-14 evidence still lacks a genuine temporal-engine evaluation failure, and the mandatory differential has four pending platform/framework cells. The terminal package matrix and R4 evidence integrity passed.

## 1. Subject and evidence boundaries

| Revision | Commit | Tree |
| --- | --- | --- |
| Reviewed R4 revision on `main` | `a244d65e3c1e803626e796bdee3af08a9ca5e89a` | `90e4262b0fb4d9169b94ae6eae11980f54a964d4` |
| Parent whose tree was supplied for R4 | `554edc1103192e68933ca71d1e03f2af72cb0920` | `1aa73fd2a08e527b5d04c8a5fba487e22160e906` |
| R3 baseline | `898c9916f4038b4f751f38995e114d2a76ab03e1` | `0d173caedb179b114752b4ca986930a9dff7be51` |
| Frozen Q1 qualification subject | `ee870e9fc32b1f20cb20863002bd18f9d4122de1` | `6a5a0d3966e9f9100ebc34cad3dae04b6264cc88` |

Local Git and GitHub agree on the reviewed commit and tree. The named terminal run identifies `a244d65` as its `main` push head. The requested tree belongs to its immediate parent. `git diff 554edc1 a244d65` shows only wording changes in the R4 README, with the same implementation, inputs, manifest and evidence blobs. The named commit and CI binding made the intended review subject identifiable. The stale tree is an editorial identity error in the request. [Reviewed commit](https://github.com/jenolaszlo-sketch/dogwood-sharp/commit/a244d65e3c1e803626e796bdee3af08a9ca5e89a) · [Parent commit](https://github.com/jenolaszlo-sketch/dogwood-sharp/commit/554edc1103192e68933ca71d1e03f2af72cb0920)

At inspection, local and hosted `main` pointed to `9e3417148df085847d6a0638a7a74691d469f507` (`1cc36ecf3d2b21fc1cc2365792142112bfc3d2d7`). Its two post-subject commits touch the R4 README, hosted log, and manifest. GitHub reports a later successful run `45498269100` at that head. The reviewed implementation and terminal verification remain bound to the named earlier commit. [Later evidence run](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45498269100)

## 2. Independent checks

I compared the original Q1 report and its recorded controls to the binding integration contract, qualification plan, R3 delta, R4 evidence and the exact committed harness. I verified Git object identities and ancestry, computed SHA-256 directly over `git cat-file blob` bytes, compared historical paths and input catalogs, and retrieved GitHub Actions run metadata, job statuses and job logs. I rebuilt the pinned upstream oracle from its locked offline graph and restored and ran the unchanged managed probe and production tests from an isolated `git archive` export. I used disposable reviewer inputs to test semantic assertions and compare C-23 encodings. The original evidence and subject files were read-only inputs to the review.

| Check | Independently observed |
| --- | --- |
| Windows differential on .NET 8.0.31 and 10.0.15 | 97/97 each, zero pending, 26 named families and zero validator coverage errors. Each run produced 62 agreements, 24 registered differences, two registered fail-closed normalizations and nine operation failures. Per-case IDs, classes and verdicts matched the published evidence. |
| Lifecycle and actual C-20-04 failure | 6/6 per TFM. Hash mismatch, absent asset, foreign RID and incompatible ABI were isolated child-process outcomes. The native gate rejected the declared faults before authorization. Residency passed. |
| Managed CedarSharp tests | 80/80 per TFM, including 28/28 `C23TypedContextTests` per TFM. No skips. |
| Harness self-tests | 16/16 with the verified native pair staged, including all seven R4 controls. The first clean-export run yielded 15/16. Section 4 explains the difference. |
| Committed evidence | 11/11 R4 entries, 11/11 R3 entries, and 8/8 original Q1 reviewer files. R2's six historical manifest mismatches reproduced. |
| Hosted verification | 12/12 jobs and 6/6 package-consumer cells at `a244d65`. Both JIT and NativeAOT executed in all six cells. |

Local host was Windows x64 build 26200, Git 2.55.0.windows.3, PowerShell 7.6.9, repository-pinned .NET SDK 10.0.404 and Rust 1.99.1. The staged Windows bridge hash `94811767dc92488b2f47ef6d6434677c03798eb9d0e7f26fc56f47610131bda5` matched the manifest and the producer's recorded prebuilt image. Its Cargo lock hash was `ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa`. The rebuilt oracle lock hash was `3b448aeadc587c2c211ae7dee59476ef551c270e51a11298cf301c8315f090f0`. The probe reported bridge ABI 1, upstream Dogwood 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851`, and upstream Cedar 4.11.0. The CedarSharp 1.0.0 Windows native image hash was `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895`. The bridge was reused after identity verification. The archive export generated SourceLink warnings because it had no `.git` metadata. Local locked restores used cached packages with NuGet audit disabled. The hosted verify jobs independently show successful dependency audit steps.

The main local commands were `cargo tree --locked --offline` and `cargo build --locked --release --offline` for the R4 oracle, `dotnet restore --locked-mode` and `dotnet build -c Release --no-restore` for the probe and test project, and `dotnet test --no-build --no-restore -c Release -f <TFM>` for each TFM after native staging. The unchanged R4 `run.ps1` executed `PrepProbe.dll self-test` and `PrepProbe.dll differential` with the exact committed inputs and isolated lifecycle directories. Review inputs exercised the same `differential` entry point. [Corpus audit](evidence/corpus-audit.log) · [Windows execution transcript](evidence/r4-run-console.log) · [Reviewer controls](evidence/reviewer-controls.json)

## 3. QD3Q-03 and ED-06

**Authorized cases pass.** On each framework, `R3-C23-08-ipaddr-typed-context` and `R3-C23-09-datetime-typed-context` observed upstream clean Deny with no determining rule and D3 clean Allow with `r3c23h_0` and `r3c23i_0`, respectively. Both engines reached evaluation, with no operation failure, diagnostics or policy-error IDs. The changed field set was exactly `decision`, `determining` and `cleanAllow`. The R3 and R4 event payloads match. The pinned source has a decimal runtime representation and lacks equivalent IP and datetime variants. [Pinned upstream value model](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/interpreter/value.rs)

**The committed register is enforced.** The positive pair passed. A third case that referenced the unchanged ED-06 row failed at evaluation because the row listed neither its ID nor an exact observation. Narrowing the comparator field list in reviewer input caused explicit out-of-scope failures. Adding an unchanged field caused an explicit missing-difference failure. A changed IP value made both engines return clean Deny, which the harness rejected with an empty observed difference set and the three expected fields absent. An incorrect intended stage failed even though the ED-06 checks themselves had no errors. Inspection found no new suppression or compatibility exception elsewhere in the checked-in catalog. The machine enforces the versioned register as input, so a future coordinated catalog and fixture change still requires authorization review.

**The 28 controls exercise D3 semantics.** Matching values allowed with the expected rule, mismatched values denied, IP methods and datetime ordering reached evaluation, and the boundary values authorized. Malformed values returned no decision and no determining identity. The companion managed controls recorded expected diagnostic categories and no caught exception. A valid control on the same schema and native pair establishes dispatch was available. Missing typed context, an extra field, and a caller-supplied `providers` field failed at request validation. Those three controls establish request/schema rejection in this self-contained bundle. The trusted-runtime reserved-field tests cover the actual trusted binding boundary separately. The generic operation-error category on several malformed extension inputs has limited diagnostic specificity.

**The compatibility ceiling remains open.** I independently ran the plain-string constructor and implicit extension-object encoding for both types, with valid schema and the same typed comparisons. All four observations reached evaluation with clean Deny upstream and clean Allow on D3, on both TFMs. The altered fixture runs exited 1 and reported `unexpected-difference` on the exact three decision fields. The plain-string limitation was disclosed by R4 §5.5. The equivalent implicit extension-object observations extend the independently verified evidence. These are valid Cedar inputs and fall outside the exact two `__extn` cases authorized. Plan §§5–8 and contract §12 gate 14 require agreement or an encoded owner-approved difference. A documentation entry alone leaves this qualification gate open. This finding concerns compatibility against the pinned reference. D3's typed evaluations followed the verified Cedar semantics. [Reviewer controls](evidence/reviewer-controls.json)

## 4. QD3Q-07 and harness coverage

The source and executed self-tests confirm the normal dual-failure path compares stage, operation failure, decision, determining identity, errors, clean Allow and policy-error IDs. When upstream provides categories, mismatched categories are visible. The upstream-empty category exception is broader than exact category equality and remains outside ED-06. C-14's duplicate-identity check exercises that exception. Equivalent twin operation failures are classified `operation-failure`. In the ordinary equality path, two absent decisions without an operation failure trigger the vacuity predicate. The two `FCN-01` provider cases are explicitly registered, with independent failure observations. I verified the recorded intended stages and ran a negative demonstrating that the stage guard defeats an otherwise valid approval.

| R4 self-test | Observed detection or acceptance |
| --- | --- |
| Exact scoped difference accepted | The positive passed with the bridge staged. The independently executed ED-06 pair also passed with exactly three changed fields. |
| Out-of-scope field rejected | The check requires the scope-specific error. The reviewer run isolated `cleanAllow` and `determining` as outside its deliberately narrowed scope. |
| Authorized field must actually differ | The check requires the missing-field error. The reviewer run isolated unchanged `stage` as the missing difference. |
| Vanished difference rejected | The self-test passed. The reviewer run established evaluation on both sides, identical clean Deny and zero observed differences. |
| Intended-stage mismatch cannot be masked | The self-test passed with a mismatch flag. The reviewer run showed that the row validation passed while the case failed specifically on wrong intended stage. |
| Scoped row must stand alone | The catalog control requires the diagnostic for combining a scoped row with another approval. |
| Unknown comparison field rejected | The catalog control requires the unknown-field diagnostic. |

**The family validator accepts a false pass.** `ValidateFamilyCoverage` tests a required control label and whether the family saw a decision or managed control. The decision-bearing branch does not require the expected decision for a role. In a two-sided reviewer run using the single C-01 direct-Allow control with its condition false, both engines returned clean Deny at evaluation, `coverageErrors` was empty and the executable exited 0. A real shipped C-01 observation remained a clean Allow in the untouched corpus. The false pass demonstrates that the validator cannot machine-enforce the required positive observation, even after the R4 hardening. The all-operation-failure self-test covers a narrower condition. [Reviewer controls](evidence/reviewer-controls.json)

**The one-command runner needs prior native staging.** Its `self-test` call precedes the first native copy into the probe outputs. A clean export with the correct native pair present only in the repository artifact directory reported 15/16 and stopped. The positive exact-scope test was the sole failure. Staging the identical pair in the paths used later by `run.ps1` reproduced 16/16 and the full differential on both TFMs with unchanged source and inputs. Several negative self-tests also passed on the unstaged run, illustrating why the staged positive and the independent failure reasons matter. This is a reproducibility defect in the documented clean-run sequence. Successful independent execution closes the evidence gap for the tested subject. [First self-test](evidence/clean-export-self-test.json) · [Staged self-test](evidence/r4-harness-self-test.json)

## 5. Findings matrix

| Finding | Q1 severity | Q2 disposition and basis |
| --- | --- | --- |
| QD3Q-01 | Critical | **Remediated.** The committed `httpFetch` declaration parses, the oracle sees the upstream `value` envelope, and the rerun shows Allow for a matching value and clean Deny for a variant. Omission and resolver failure use exact `FCN-01` observations. Missing provider fails at trusted binding. |
| QD3Q-02 | Critical | **Remediated.** Both local TFMs reran C-20-04 and all C-26 scenarios. Isolated child results tie failure to the declared interventions, including real native hash mismatch and distinct ABI rejection. |
| QD3Q-03 | Critical | **Partially remediated — blocking.** Exact ED-06 pair and D3 regression controls passed. The four observed alternate-encoding divergences exceed the approved scope and keep the C-23 compatibility gate open. |
| QD3Q-04 | High | **Remediated.** C-19 returns raw Allow with error attributed to the forbid `r3c19_0` on both sides. The result is fail closed. The inherited ED-01 and ED-03 rows pin the expected observation, and the production test preserves the error and identity. |
| QD3Q-05 | High | **Partially remediated — blocking.** Empty and non-decision history reached evaluation and returned clean Deny. Duplicate identity failed at trusted binding. The 3600/3601-second boundary controls returned Allow/Deny. No committed C-14 observation establishes a genuine temporal-engine evaluation failure. Duplicate identity rejection occurs before it, and a false binding is a valid evaluation outcome. Plan §6 and Q1's minimum remediation both require that missing observation. |
| QD3Q-06 | High | **Remediated.** The literal 1,000-character payload reached evaluation and authorized on both sides. A reviewer truncation check returned clean Deny on both sides. |
| QD3Q-07 | High | **Partially remediated — blocking.** Staged self-tests and independent scope/phase checks passed for their stated mechanisms. The evaluator still accepts a failed semantic coverage role as seen in C-01. |
| QD3Q-08 | High | **Remediated for R4.** All 11 committed evidence blobs matched at the reviewed commit. The six frozen R2 discrepancies remain part of the preserved historical record. |
| QD3Q-09 | Medium | **Remediated.** GitHub confirms the terminal commit, R3 baseline and frozen Q1 bindings. The stale tree in the request and stale section references are editorial. |
| QD3Q-10 | Medium | **Remediated.** R3 §3.4 records the native, managed, test, package, CI and docs changes in the actual R2 implementation delta. The inventory agrees with the `17b905f..8d261db` path comparison. |
| QD3Q-11 | Medium | **Not corrected — documented.** Managed diagnostic normalization still depends on message wording. A pinned-engine wording change could shift categories, with residual risk to diagnostic and comparison fidelity. Authorization decisions remain separately classified. |
| QD3Q-12 | Medium | **Partially corrected — retained.** The errored-forbid and added C-23 controls ran. Section 6 assesses the five production-suite omissions individually and identifies a committed C-11/C-21 evidence limitation. |
| QD3Q-13 | Low/Medium | **Not corrected — documented.** `InternalsVisibleTo("PrepProbe")` remains an unsigned simple-name grant. The probe uses the internal export path, and the CLR grant applies to the assembly's internal surface. This expands local access to internals and does not change the reviewed public entry points. |
| QD3Q-14 | Low | **Not corrected — documented.** The bridge mirrors only the current request's `input` group on the empty-string sentinel. Explicit logged JSON suppresses mirroring. The observed temporal rows establish the tested bindings, with limited coverage of current-event logged-record compatibility. |
| QD3Q-15 | Low | **Not corrected — documented.** The native trusted-binding capture engine obtains enriched context and returns a synthetic deny. It cannot report errors from upstream Cedar policy evaluation. The final CedarSharp evaluation and binding-error channel have distinct responsibilities. |
| QD3Q-16 | Info | **Not corrected — accepted design.** The shipped adapter retains the source/schema/provider data needed for per-request re-lowering. UID validation depends on the pinned engine. Runtime residency and resource conclusions remain bounded by the reviewed evidence. |

## 6. QD3Q-12 production-suite omissions

Q1's minimum remediation marks production-suite additions as recommended. The underlying behavior and executable controls specified by the binding plan remain mandatory. A missing production test can coexist with qualifications backed by other evidence. A mislabeled control cannot establish its asserted behavior.

| Missing in-tree control | Evidence and residual risk | Scope judgment |
| --- | --- | --- |
| Unknown diagnostic ID | The committed C-21 probe induced `UnknownPolicyIdentity` and `unknown-policy-id`, with no decision. Source rejects the unknown ID before exposing an authorization result. | Low regression risk from the absent routine test. Compatible with the original scope. |
| Duplicate and mismatched exported IDs | The C-21 duplicate control throws `DuplicatePolicyId`. The managed bundle constructor checks mismatch separately. I verified the mismatch guard by source review. The bounded rerun did not inject a mismatched export. | Low residual coverage risk for the separate mismatch branch. The duplicate obligation has execution evidence. Compatible as a retained production-test gap. |
| Caller action-entity conflict | C-07-03 supplied a conflicting `read2` action entity. Upstream denied with entity-conformance diagnostics. D3 rejected in request validation under ED-02. | Medium regression risk since routine tests omit it. The committed differential exercises the mandatory conflict under the original scope. |
| Multi-action `appliesTo` canonicalization | The producer cites C-11-05 and C-11-07. C-11-05 uses a one-action schema. C-11-07 exercises membership. C-11-06 also checks only `read`. A reviewer check on Windows .NET 10 exercised independent `read` and `archive` principal, resource and context constraints and passed. | Low observed implementation risk. The original C-11 mandatory corpus claim remains incomplete because the committed control lacks multiple complete actions. |
| Non-empty `CurrentLoggedJson` suppression | Source mirrors caller input only when the raw current logged value is the empty-string sentinel. The in-tree test pins the empty sentinel and exercises current-event history. It gives no production regression coverage of explicit non-empty data. | Low residual regression risk within the original D3 scope. Retain the documented limitation alongside QD3Q-14. |

The committed C-21 reorder fixture reverses one policy, so its success cannot establish reorder behavior. The Q2 reviewer rerun with two exported rules observed indices `[1, 0]` under actual reversal on Windows .NET 10. This is positive adapter evidence and leaves the committed mandatory corpus control incomplete. The two supplemental managed checks should be considered within the scope of their one observed platform and framework. [Reviewer controls](evidence/reviewer-controls.json)

## 7. Evidence integrity and reporting

- The frozen Q1 commit and tree, the Q1 evidence inventory, and the historical R2 and R3 paths were preserved. `ee870e9` and `898c991` are ancestors of `a244d65`. The Q1 worktree resolved to the frozen identity and was clean. All eight listed reviewer evidence hashes matched. Executable and published R4 fixtures and registers matched byte-for-byte. The R2 manifest still mismatches its six committed blobs, as found in Q1. The R3 and R4 manifests pass independently. [Committed-blob audit](evidence/git-evidence.log)
- The R3-to-R4 tracked delta is additive, confined to the R4 evidence record and harness plus the new 315-line `C23TypedContextTests.cs`. Production source, native source, package configuration and pins, CI workflows and qualification criteria are identical to the frozen subject. R3 added its own harness and evidence and changed the managed adapter test file. The R4 probe's oracle manifests and lock and the managed probe lock match R3. The catalog retains all 92 R3 case IDs and adds five managed controls. Only ED-06 metadata changes on the two inherited cases. ED-01 through ED-05 rows match as raw blocks and `FCN-01` is unchanged. [Catalog audit](evidence/corpus-audit.log)
- The three green runs in the producer README describe the three successful workflow runs recorded before the terminal run at `a244d65`. The final handoff counts that terminal run as the fourth. These are four distinct runs. Attempt 2 of `45472926028` counts once. The R3 and frozen Q1 runs are separate baselines. The later `9e34171` evidence continuation is outside the named subject and named terminal run.
- Cross-reference issues are editorial where their targets remain identifiable. R4 §5.5 points to §9.4 for the residual risk table in §9.5. The hosted log's §9.1 reproduction pointer and the R3 §9.2 reference to frozen CI resolve correctly. The R4 §10.2 table at `a244d65` uses `this commit` for accumulated evidence-only changes across intermediate commits. The actual parent delta modifies only the README. Git history and the exact-head run resolve the ambiguity. `git diff --check` reports one extra blank line at the end of the hosted log. It has no evidence impact.

## 8. Hosted CI and package traceability

GitHub's run metadata reports `head_sha=a244d65e3c1e803626e796bdee3af08a9ca5e89a`, `head_branch=main`, event `push`, attempt 1, conclusion `success`. The 12 jobs were three platform verification jobs, a pack job, an independent package inspection job, six consumers, and the summary. The summary log printed `D3_PACKAGE_CONSUMER_SUMMARY=PASS CELLS=6`. [Terminal run](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45490253791) · [Summary job](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45490253791/job/138807972934)

| Platform | .NET 8 job | .NET 10 job | Observed in each cell |
| --- | --- | --- | --- |
| Windows x64 | `138801909781` | `138801909642` | JIT execution, native generation/publish and executable execution, package-only PASS twice |
| Linux x64 | `138801909646` | `138801909618` | JIT execution, native generation/publish and executable execution, package-only PASS twice |
| macOS ARM64 | `138801909648` | `138801909611` | JIT execution, native generation/publish and executable execution, package-only PASS twice |

The pack job uploaded the candidate bundle (`14967727300`), original CedarSharp dependency (`14967293892`) and consumer kit (`14968475401`). The inspection job downloaded them as applicable, ran strict package identity/graph/native-pair verification, and uploaded its report. Consumer jobs had no checkout step. Their logs show downloads of those same run-scoped artifacts and `D3_CONSUMER_MODE=PACKAGE_ONLY` before each PASS. The exact workflow and kit code require isolated restore paths, localized package sources and exact package graph/content checks. The kit rejects project or binary references and native overrides, then checks the selected native assets against the restored package. The JIT DLL and native publish executable are run separately. This verifies consumption of packed candidates through the hosted execution and its enforced provenance checks. I did not independently rerun the terminal packages locally or rehash downloaded binaries. [Pack job](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45490253791/job/138800681856) · [Inspection job](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45490253791/job/138801407161) · [Consumer runner](https://github.com/jenolaszlo-sketch/dogwood-sharp/blob/a244d65e3c1e803626e796bdee3af08a9ca5e89a/qualification/D3.PackageConsumer/run.ps1)

| R4 chain | Head | GitHub result |
| --- | --- | --- |
| [45467056283](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45467056283) | `bb9e1c9` | Success, first attempt |
| [45472926028](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45472926028) | `c115707` | First attempt failed, second attempt 12/12 successful |
| [45483027001](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45483027001) | `103081f` | Success, first attempt |
| [45490253791](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45490253791) | `a244d65` | Success, first attempt |

In the first attempt of `45472926028`, nine jobs succeeded, the two macOS consumer jobs were cancelled before any runner assignment or steps, and the summary rejected the aggregate `abandoned` result. This directly supports the infrastructure-acquisition classification. The cancelled jobs had no downloadable log, so I could not corroborate the exact producer-quoted annotation or attribute the capacity problem more specifically. The same workflow run succeeded on attempt 2. The frozen subject and R3 runs independently resolved to success at their corresponding heads. [Failed attempt](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45472926028/attempts/1) · [Retry](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45472926028/attempts/2) · [R3 baseline run](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45357936457) · [Frozen Q1 run](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45127492373)

The differential run is a separate obligation. R4 `run.ps1` stages a Windows image and invokes a Windows oracle executable. The unchanged hosted jobs cover platform regression and package execution and never invoke the R4 oracle or corpus. Producer evidence and independent execution show the locked differential only on Windows. Qualification plan §§9 and 11 call for the same locked oracle, fixtures and difference register on each cell and say an unavailable cell is pending. The four `linux-x64` and `osx-arm64` differential cells are pending under that written plan. I found no authorized exception for them in the supplied commission or committed qualification records. [Pinned qualification plan](https://github.com/jenolaszlo-sketch/dogwood-sharp/blob/a244d65e3c1e803626e796bdee3af08a9ca5e89a/docs/d3-qualification-plan.md#9-matrix-and-execution-order)

## 9. New evidence and qualification impact

| Observation | Severity | Qualification impact |
| --- | --- | --- |
| C-23 valid alternate encodings produce the same unapproved decision divergence on both TFMs | Critical, extends QD3Q-03 | Gate 14 remains open. This includes confirmation of the previously disclosed short form and independent observations for the second encoding. |
| C-01 required Allow can pass as clean Deny | High, extends QD3Q-07 | Gate 14 remains open. The check is insufficient for the positive-control obligation established before R4. |
| C-11 single-action and C-21 single-rule tests overstate committed corpus coverage | Medium, corpus evidence | The mandatory observations are missing from the committed corpus. Q2's Windows .NET 10 controls provide direct supporting evidence for the implementation. |
| Four mandatory oracle/differential cells lack execution evidence | High, qualification evidence | Hosted package matrix passed gate 15. Gate 14 and the matrix evidence for gate 16 are incomplete under the binding plan. |
| Self-test executes before staging the native pair | Medium, reproducibility | The documented one-command clean run stopped. Q2 isolated the setup error and generated the full evidence without changes to the subject. |
| Requested tree belongs to the parent, plus CI count and reference discrepancies | Information, editorial | The exact Git identities, revision delta and hosted run metadata resolve these discrepancies. No separate remediation is warranted. |

## 10. Disposition

**C. REMEDIATION REQUIRED.** I recommend withholding D3 owner acceptance and keeping qualification open. The mandatory blockers are the ED-06 compatibility boundary (QD3Q-03), the still-missing C-14 temporal-engine failure (QD3Q-05), semantic family enforcement and the incomplete C-11/C-21 corpus controls (QD3Q-07 and QD3Q-12), and the four pending differential cells under the written plan. The five routine production-suite omissions remain qualifications at their assessed risks. Hosted package results and the R4 blob manifest independently pass.

The local evidence covers the checks described above. Other producer-only execution claims, wider concurrency bounds, and non-Windows differential behavior were outside the observed local evidence. The report records independent verification once under the finite evidence-chain rule. No acceptance, release or roadmap decision was taken.

## Evidence inventory

- [Windows .NET 8 differential](evidence/r4-differential-net8.0-results.json) · [Windows .NET 10 differential](evidence/r4-differential-net10.0-results.json)
- [Windows .NET 8 lifecycle](evidence/r4-lifecycle-net8.0.json) · [Windows .NET 10 lifecycle](evidence/r4-lifecycle-net10.0.json)
- [Windows .NET 8 managed tests](evidence/managed-net8.0.trx) · [Windows .NET 10 managed tests](evidence/managed-net10.0.trx)
- [Exact-head hosted metadata and log observations](evidence/terminal-ci.json)
- [Reviewer evidence SHA-256 manifest](evidence/manifest.sha256)

The isolated source export, compiled review outputs and transport archive were removed after evidence verification. The lifecycle scratch directory was absent after the successful run. The reviewer evidence remains with this report. The record is uncommitted.
