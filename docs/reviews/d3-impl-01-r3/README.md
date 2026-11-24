# DOGWOOD-SHARP-D3-IMPL-01-R3 — Remediation implementation record

**Disposition: B. IMPLEMENTATION PARTIAL — BLOCKING GAPS REMAIN**

This record is the producer response to the independent Q1 qualification of
`DOGWOOD-SHARP-D3-IMPL-01` (disposition **C. REMEDIATION REQUIRED**). It records
what was remediated, how it was verified, and which findings remain open. It
does **not** grant independent qualification, owner acceptance, or any
publication, tag, or release authorization.

| Item | Value |
| --- | --- |
| Commission | `DOGWOOD-SHARP-D3-IMPL-01-R3` |
| Frozen Q1 subject | commit `ee870e9fc32b1f20cb20863002bd18f9d4122de1`, tree `6a5a0d3966e9f9100ebc34cad3dae04b6264cc88` (unchanged) |
| Q1 evidence | `C:\Windows\Temp\opencode\d3-qual-01-q1-evidence\` (reviewer-owned, outside this repository, preserved unmodified) |
| Q1 report | `DOGWOOD-SHARP-D3-QUAL-01-Q1-report.md` (46/46 reviewer controls pass; blockers are corpus/evidence integrity) |
| R2 historical record | `docs/reviews/d3-impl-01-r2/` — preserved byte-for-byte; corrections are recorded here, not by editing R2 |
| Pinned upstream | Dogwood 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851`; Cedar 4.11.0 |
| R3 harness | `docs/reviews/d3-impl-01/evidence/differential/r3/` (evidence tooling only; never a production execution path) |
| R3 evidence | `docs/reviews/d3-impl-01-r3/evidence/` |
| R3 implementation commit | `4c4dcc92c91068f8c754884f1d28312b988b92c8`, tree `e329fabcf112de86ea9d0db2156e36a8eb2da98f` |

---

## 1. Summary

Every Q1 *critical* and *high* finding that was about the producer's evidence is
repaired and re-verified by execution: the provider differential now runs
against a valid declaration with the upstream envelope's own `value` transport;
the lifecycle controls actually execute their native-failure scenarios in
isolated processes (plus the process-lifetime residency control); the C-19, C-14, C-23 and C-24 fixtures now exercise the
behavior they claim; and the runner can no longer report a false green.

Two findings remain open and are **blocking**:

1. **QD3Q-03 (C-23 typed context values).** The pinned upstream value model has
   no `ipaddr` or `datetime` variant, so an `ipaddr`/`datetime` value carried in
   the request context cannot reach the upstream authorizer. The upstream
   comparison is a clean Deny while D3 evaluates the Cedar extension. The
   qualification plan requires the two to agree and no owner-approved
   difference covers it, so the two cases fail and family C-23 coverage is
   incomplete.
2. **QD3Q-12 (in-tree coverage of the mandated negatives) is partially
   repaired.** The errored-forbid and extension-value controls are now pinned in
   the production test suite; four other recommended in-tree controls remain
   unimplemented (see §6).

Because the differential corpus still contains two failing cases, the
qualification gate "full D3 differential, both TFMs" is **not** green, and the
disposition is **B**.

---

## 2. Commission requirement reconciliation

| Requirement | Status | Evidence |
| --- | --- | --- |
| R3-01 — repair the C-16/C-17 provider differential (valid `httpFetch` declaration, `value` vs `valueJson` transport, real upstream observations, harness regression for malformed shared inputs) | **Done** | `r3/fixtures.json` (`providerDeclarations.httpFetch` is valid JSON; outcomes carry `value`), `r3-differential-*-results.json` cases `R3-C16-01`, `R3-C16-02`, `R3-C17-01…03`; harness self-test check `malformed-shared-input-cannot-pass` |
| R3-02 — execute the declared lifecycle scenarios with machine-verifiable observations in isolated subprocesses; make C-20-04 a genuine native-failure path; publish `lifecycle-results.json`; no unloading/hot replacement | **Done** | `r3-lifecycle-net8.0.json`, `r3-lifecycle-net10.0.json` (six executed scenarios, per-scenario checks and child-process observations); probe modes `lifecycle-probe` and the staged-copy runner |
| R3-03 — repair C-23: investigate the Cedar/Dogwood type contract, distinguish bad fixtures from genuine product limits, add no unapproved difference, report unresolved gaps as blocking | **Done (one blocking gap remains)** | `r3/fixtures.json` (separate decimal/ipaddr/datetime schemas, lowercase Dogwood type names); `r3-differential-*-results.json` cases `R3-C23-01…09`; §5 of this record |
| R3-04 — fix C-19 (real erroring forbid), C-14 (duplicate identity + envelope mismatch + meaningful temporal boundaries), C-24 (`repeat` → contract-authorized literal) | **Done** | `R3-C19-01-errored-forbid` (forbid reads a declared-but-absent attribute and errors), `R3-C14-01-empty-history` / `R3-C14-02-history-identity-duplicate` / `R3-C14-03-non-decision-event`, `R3-C12-02` / `R3-C13-02` window boundaries at 3600 s and 3601 s, `R3-C24-04-bounded-payload` (1000-character literal) |
| R3-05 — harden the runner: compare stage/categories on dual failure, preserve ED-01–ED-05, reject vacuous agreement and unexpected differences, semantic family coverage, enforce the intended execution stage; add a harness regression for each false-green mechanism | **Done** | `r3/probe/Program.cs`; `r3-harness-self-test.json` (9/9); §4 of this record |
| R3-06 — regenerate the evidence manifest against the exact committed Git blobs and record the hashing procedure | **Done** | `evidence/manifest.sha256`, §7 of this record |
| R3-07 — record hosted CI, correct the R2 production delta, link each finding to its remediation and verification evidence | **Done** | §6, §8 of this record |
| R3-08 — assess QD3Q-11…16 individually, implement the low-risk fixes, document the uncorrected findings, residual risk and qualification impact | **Done** | §6 of this record |

---

## 3. What changed in R3

### 3.1 Production code

**No production code changed in R3.** The production tree at the frozen subject
is byte-identical to the R3 tree except for the two test files listed below.
This is deliberate: the commission forbids a redesign of the implementation,
and every Q1 finding was about the producer's evidence, not about the shipped
behavior.

### 3.2 Production tests (additive only)

| File | Change |
| --- | --- |
| `tests/DogwoodSharp.CedarSharp.Tests/CedarSharpAdapterTests.cs` | Added `ErroredForbidPreservesItsErrorAndIdentity` (C-19), `ValidExtensionValuesEvaluateIdentically` (C-23, three cases), `InvalidExtensionValuesFailClosedAtBundleConstruction` (C-23, three cases) |

No existing assertion was changed, relaxed, or removed.

### 3.3 Evidence tooling (new, `docs/reviews/d3-impl-01/evidence/differential/r3/`)

| Path | Purpose |
| --- | --- |
| `fixtures.json` | The R3 corpus: 92 cases, 26 families, `intendedStage` on every differential case |
| `differences.json` | ED-01…ED-05 plus the `failClosedNormalizations` register |
| `upstream-oracle/` | Fork of the R2 oracle that also runs the pinned upstream's schema-aware `Validator` phase |
| `probe/` | Hardened managed probe (strict comparison, intended-stage enforcement, semantic family coverage, executed lifecycle scenarios, harness self-test) |
| `run.ps1` | One-command reproduction of the whole R3 evidence run |

### 3.4 Corrected R2 production delta (QD3Q-10)

The R2 record's "Production changes in R2" section listed three items; the R2
commit `8d261db` changed more. The complete delta is:

- **Native:** `native/dogwoodsharp-bridge/src/d2.rs`,
  `native/dogwoodsharp-bridge/src/d2/runtime.rs`,
  `native/dogwoodsharp-bridge/src/d2/runtime_tests.rs`,
  `native/dogwoodsharp-bridge/src/lib.rs`.
- **Managed production:** `src/DogwoodSharp/Dogwood.cs`,
  `src/DogwoodSharp/NativeBridge.cs`, `src/DogwoodSharp/DogwoodSharp.csproj`,
  `src/DogwoodSharp.CedarSharp/DogwoodCedarAuthorizer.cs`,
  `src/DogwoodSharp.CedarSharp/DogwoodSharp.CedarSharp.csproj`,
  `src/DogwoodSharp.CedarSharp/DogwoodTrustedRuntime.cs`.
- **Production tests:** `tests/DogwoodSharp.CedarSharp.Tests/TrustedRuntimeTests.cs`,
  `tests/DogwoodSharp.Tests/ParsingTests.cs`.
- **Packaging and CI:** `.github/workflows/ci.yml`, `NOTICE`,
  `eng/Build-Native.ps1`, `eng/CedarSharp-1.0.0.package-hashes.json`,
  `eng/DogwoodSharp.CedarSharp.nuspec`, `eng/Pack-PackageCandidates.ps1`,
  `eng/PackageNormalizer/*`, `eng/Test-NativePackageAssets.ps1`,
  `eng/Test-PackageCandidates.ps1`, `qualification/D3.PackageConsumer/*`.
- **Documentation:** `docs/architecture/native-artifacts.md`,
  `docs/ci-publishing.md`, `docs/d3-implementation.md`, `docs/managed-api.md`,
  `src/DogwoodSharp.CedarSharp/README.md`.
- **Evidence (not production):** `docs/reviews/d3-impl-01-r2/*` and
  `docs/reviews/d3-impl-01/evidence/differential/r2/*`.

The R2 record itself is not edited; this section is the correction of record.

---

## 4. Comparison discipline (R3-05)

The R3 runner applies the qualification plan §5 default equality rule without
the R2 exemptions:

- **Compared fields:** stage, operation-failed state, decision, determining
  identity set, policy-error ids, error presence, clean-allow flag, and error
  categories whenever the upstream side reports any.
- **Dual failure is compared, not normalized.** When both sides fail closed, the
  runner compares stage, operation-failed state, decision, determining set,
  error presence, clean-allow flag, policy-error ids and — when the upstream
  reports categories — the error categories. The R2 behavior of emptying the
  difference list on dual failure is removed.
- **No vacuous agreement.** A case in which neither side produced a decision and
  neither side failed is classified `unexpected-difference`.
- **Intended stage is enforced.** Every differential case declares the exact
  upstream stage it must reach; a case that never reaches that stage fails.
- **A harness fault is never a conformance claim.** An upstream envelope fault
  (a malformed shared fixture input) is reported as a harness fault and fails
  the case; it can never be classified as agreement.
- **An unexpected difference always fails**, even when the fixture names a
  classification, so a corpus cannot bless its own divergence.
- **Register rows are machine-verified.** A case carrying an expected
  difference ID must match that row's exact per-case upstream and D3
  observations; a nonempty ID list alone never passes a case.
- **Family coverage is semantic.** `familyRequirements` now carries
  `requiredControls` plus a `decisionCoverage` role
  (`required` / `managed-control-only` / `exempt`). A family whose only cases
  fail before a decision no longer satisfies a decision-bearing family; this is
  the exact false-green mechanism QD3Q-07 identified.
- **ED-01–ED-05 are preserved.** ED-02, ED-04 and ED-05 are byte-identical to
  the R2 rows apart from the mechanical `R2-` → `R3-` case-id re-binding that
  the new corpus ids require; ED-01 and ED-03 gain exactly one case
  (`R3-C19-01-errored-forbid`) with its exact per-case observations. No row's
  agreement class, upstream/D3 observation, basis or fail-closed effect changed,
  and no new approved difference was added.

Approved-difference usage in the R3 corpus (both TFMs, identical):

| ED | Cases |
| --- | --- |
| ED-01 | 5 |
| ED-02 | 8 |
| ED-03 | 7 |
| ED-04 | 6 |
| ED-05 | 1 |

### 4.1 The one registered fail-closed normalization

`FCN-01` in `differences.json` reconciles the provider omission/failure cases.
It is **not** an approved difference and grants no permission: it records that
the pinned upstream itself declares the resolver-error decision shape to be
undefined behavior (`engine.rs`: `Some(Err(_))` is "undefined behavior for the
decision outcome"; `tests/fail_closed.rs`: "no semantic guarantee exists"). The
row is machine-verified on every run: both sides must fail closed, neither may
allow, and the exact upstream and D3 observations must match the register. The
runner rejects a normalization whose observations do not match.

---

## 5. Blocking gaps

### 5.1 QD3Q-03 — `ipaddr` / `datetime` typed context values (C-23)

**Observed (both TFMs, both sides):**

| Case | Upstream | D3 |
| --- | --- | --- |
| `R3-C23-08-ipaddr-typed-context` | evaluation, clean Deny | evaluation, clean Allow |
| `R3-C23-09-datetime-typed-context` | evaluation, clean Deny | evaluation, clean Allow |

**Investigation.** The corrected C-23 fixtures are split per extension type and
use the lowercase Dogwood builtin type aliases (`decimal`, `ipaddr`,
`datetime`), so the R2 "failed to resolve type" fixture defect is gone. With
those schemas the corpus now exercises real values:

- decimal typed context values agree on both sides (`R3-C23-01`,
  `R3-C23-02`), and an invalid decimal literal fails the validated lowering on
  both sides (`R3-C23-03`);
- `ipaddr` and `datetime` **literal** comparisons agree on both sides
  (`R3-C23-04`, `R3-C23-06`), and invalid literals fail the validated lowering
  on both sides (`R3-C23-05`, `R3-C23-07`);
- the **typed context** form cannot agree (`R3-C23-08`, `R3-C23-09`).

The upstream reason is a product limit of the pinned reference, not a fixture
defect: the upstream `Value` model has no `ipaddr` or `datetime` variant, so an
extension value supplied in the request context cannot reach the upstream
authorizer and the comparison is a clean Deny. Three different encodings of the
same value (`fn: "ip"`, `fn: "ipaddr"`, plain string) were executed against
the oracle and all produced the same clean Deny, so the divergence is not an
encoding typo. D3 evaluates the Cedar extension and allows.

**Why it is not remediated here.** Closing the gap needs either an
owner-approved difference (the register may not gain one under this
commission) or a change to the shipped decision contract (a redesign, which
this commission forbids). It is therefore reported as a blocking gap, exactly as
R3-03 requires.

**Residual risk.** A request whose context carries an `ipaddr` or `datetime`
value is authorized by D3 and denied by the upstream reference. Because the
comparison is on a value that the reference cannot represent, the practical
exposure is limited to policies that read those typed attributes, but it is a
real difference in the authorization outcome and it is not approved.

**Qualification impact.** Family C-23 is incomplete and the "full D3
differential, both TFMs" gate is red. Requalification needs an owner decision
on the ceiling (approve a documented difference, or scope C-23 to the
representable subset, or extend the upstream value model).

### 5.2 QD3Q-12 — in-tree controls for the mandated negatives (partial)

The errored-forbid and extension-value controls are now pinned in the
production suite. Four recommended controls remain unimplemented and are
documented rather than silently closed:

| Recommended control | State | Residual risk |
| --- | --- | --- |
| Unknown diagnostic id | Not implemented | Low. The D3 export maps every diagnostic id to a source rule; the differential corpus observes determining identity on every evaluation-stage case, but no in-tree control pins an unknown id. |
| Duplicate / mismatched exported ids | Not implemented | Low. The export identity is exercised in `CedarSharpAdapterTests.DeterminingIdentityMapsBackToTheSourceRuleIndex` and `TheExportedBodiesRebuildTheExportedPolicySetByIdentity`, but not for a duplicate or mismatched id. |
| Caller action-entity conflict | Not implemented | Medium. The differential corpus covers the caller-conflict case (`R3-C07-03`, approved difference ED-02); no in-tree control pins it. |
| Multi-action `appliesTo` canonicalization | Not implemented | Low. The differential corpus covers the multi-action case (`R3-C11-05`) and the canonical-schema case (`R3-C11-07`); no in-tree control pins them. |
| Errored forbid | **Implemented** | — |
| Extension values | **Implemented** | — |
| Non-empty `CurrentLoggedJson` suppression | Not implemented | Low. `TrustedRuntimeTests.EmptyCurrentLoggedRecordMirrorsOnlyTheCurrentInputGroup` pins the empty sentinel; the non-empty path is not pinned in-tree. |

---

## 6. Qualification finding reconciliation (QD3Q-01 … QD3Q-16)

| Finding | Severity | Status | Remediation and verification |
| --- | --- | --- | --- |
| QD3Q-01 | Critical | **Remediated** | The shared `providerDeclarations.httpFetch` fixture is now valid JSON and the outcomes use the upstream envelope's `value` field. Real observations exist for value, variant, omission, failure and missing outcomes (`R3-C16-01`, `R3-C16-02`, `R3-C17-01…03`, `R3-C16-03`). The harness self-test check `malformed-shared-input-cannot-pass` proves a malformed shared input can no longer pass. |
| QD3Q-02 | Critical | **Remediated** | `RunLifecycleProbe` now executes the declared scenarios. Each destructive scenario runs in a child process against a private staged copy of the probe host directory with exactly one injected fault; the parent re-checks the child's fail-closed observation. C-20-04 is a real native-failure injection. `r3-lifecycle-net8.0.json` and `r3-lifecycle-net10.0.json` are published and hashed. |
| QD3Q-03 | Critical | **Partially remediated — BLOCKING** | The fixture defect is repaired and the family now evaluates real values (decimal typed context, `ipaddr`/`datetime` literals, invalid encodings). The upstream typed-context ceiling cannot be closed without an owner-approved difference or a contract redesign and is reported as a blocking gap (§5.1). |
| QD3Q-04 | High | **Remediated** | `R3-C19-01-errored-forbid` uses a forbid that reads a declared-but-absent attribute, so the rule errors: the raw Allow is preserved, the error and the forbid identity survive, and the outcome is not a clean Deny. Pinned in-tree by `ErroredForbidPreservesItsErrorAndIdentity`. |
| QD3Q-05 | High | **Remediated** | The mislabeled controls are replaced: `R3-C14-01-empty-history` is a real temporal-engine evaluation path, `R3-C14-02-history-identity-duplicate` is the duplicate-identity rejection, `R3-C14-03-non-decision-event` covers the non-decision event, and C-12/C-13 carry meaningful temporal window boundaries at exactly 3600 s (allow) and 3601 s (deny). |
| QD3Q-06 | High | **Remediated** | `R3-C24-04-bounded-payload` compares a 1000-character literal payload by value on both sides (equal payload allows, truncated payload denies). The `repeat` expression is gone. |
| QD3Q-07 | High | **Remediated** | Dual-failure comparison is strict, vacuous agreement is rejected, unexpected differences always fail, family coverage is semantic, and the intended execution stage is enforced. Nine harness regression controls in `r3-harness-self-test.json` recreate each false-green mechanism and assert that the runner now fails it. |
| QD3Q-08 | High | **Remediated** | The R3 manifest is computed from the committed Git blobs (`git cat-file blob`), so it matches a clean checkout byte-for-byte; the procedure and the verification command are in §7. The R2 manifest is left untouched as a historical record, with the correction recorded here. |
| QD3Q-09 | Medium | **Remediated** | This record binds the R3 revision to its own hosted CI run (§8). The Q1 run `45127492373` at the frozen revision `ee870e9` is recorded here as the frozen-subject CI result. |
| QD3Q-10 | Medium | **Remediated** | The complete R2 production delta is enumerated in §3.4. |
| QD3Q-11 | Medium | **Not corrected — documented** | The adapter still derives some diagnostic categories from engine message wording, which the contract declares non-normative. Changing the category derivation would alter the observable diagnostic surface of the shipped package — a contract-visible change beyond the evidence scope of this commission, with a real risk of misclassifying an existing diagnostic. **Residual risk:** a CedarSharp wording change could move a diagnostic between categories. The compared surface is unaffected: the differential corpus compares normalized categories, and a category change would surface as an unexpected difference rather than a silent pass. **Qualification impact:** the categories are evidence, not the authorization outcome; no decision in the corpus depends on a category change alone. |
| QD3Q-12 | Medium | **Partially corrected — documented** | The errored-forbid and extension-value controls are now in the production suite. Four further recommended controls remain unimplemented and are listed with their residual risk in §5.2. |
| QD3Q-13 | Low/Medium | **Not corrected — documented** | `InternalsVisibleTo("PrepProbe")` is still a name-based grant on the stable package. The R3 probe needs the internal D3 export surface, so the grant cannot be withdrawn while the evidence tool is in the repository. **Residual risk:** any assembly named `PrepProbe.exe`/`.dll` can reach the internal surface of the stable package. **Qualification impact:** the grant exposes no production execution path; the internal surface is the read-only export/lower API that the evidence tool consumes. Removing it requires shipping the evidence tool under a package-private identity and is left to the owner as a follow-up. |
| QD3Q-14 | Low | **Not corrected — documented** | The current-event logged-record mirroring (the empty `CurrentLoggedJson` sentinel mirrors the request input) still differs from the upstream and stays outside the difference register. **Residual risk:** a temporal expression that reads the current event's logged record observes the mirrored input rather than an upstream-shaped logged record. The corpus observes the mirroring behavior identically on both sides for the temporal families, so no current case is affected. **Qualification impact:** documentation-level; it should be registered before the owner extends the corpus to current-event logged-record semantics. |
| QD3Q-15 | Low | **Not corrected — documented** | The trusted binding's capture engine cannot surface upstream-side Cedar policy errors; the split is deliberate and stays undocumented in the contract. **Residual risk:** a policy error raised inside the trusted binding path is reported through the capture engine's own error channel rather than as a Cedar policy error. **Qualification impact:** the corpus compares the resulting decision and error presence, and both sides are affected identically in the observed cases; the contract should record the split. |
| QD3Q-16 | Info | **Not corrected — accepted** | Per-request re-lowering retains caller source/schema/provider data for the request lifetime and UID validation relies on the engine. This is the shipped architecture and is unchanged by R3. **Residual risk:** memory residency scales with the retained per-request lowering; UID validation correctness is delegated to the pinned engine. |

---

## 7. Evidence and manifest procedure (R3-06)

### 7.1 Files

| File | Content |
| --- | --- |
| `evidence/manifest.sha256` | SHA-256 of every R3 evidence file, computed from the **committed Git blob** |
| `evidence/r3-differential-net8.0-results.json` | Full per-case differential result, net8.0 |
| `evidence/r3-differential-net10.0-results.json` | Full per-case differential result, net10.0 |
| `evidence/r3-lifecycle-net8.0.json` | Executed lifecycle observations, net8.0 |
| `evidence/r3-lifecycle-net10.0.json` | Executed lifecycle observations, net10.0 |
| `evidence/r3-harness-self-test.json` | The nine harness regression controls |
| `evidence/r3-differential-fixtures.json` | Published copy of the corpus |
| `evidence/r3-differential-differences.json` | Published copy of the register |
| `evidence/r3-differential-net8.0.log`, `…-net10.0.log` | Console output of each run |
| `evidence/r3-differential-run.log` | Command list, upstream pin and per-TFM summary |
| `evidence/r3-hosted-ci-run.log` | Hosted CI run and job identifiers for the R3 revision and for the frozen Q1 subject |

### 7.2 Hashing procedure

The manifest hashes the **committed blob bytes** of each evidence file, not the
working-copy bytes. The repository forces LF (`.gitattributes`: `* text=auto
eol=lf`), so hashing the working copy on Windows would hash CRLF bytes and
would not match any checkout. The procedure is:

```powershell
# for each path listed in evidence/manifest.sha256
$blob = git cat-file blob "HEAD:<path>"      # exact committed bytes
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes(($blob -join "`n")))).ToLowerInvariant()
```

`git cat-file blob` emits the blob content; the manifest is regenerated from
the committed tree after the evidence commit, and the manifest itself is
committed in the following evidence commit. Verification from a clean checkout:

```powershell
Get-Content evidence/manifest.sha256 | ForEach-Object {
    $expected, $path = $_ -split '\s+', 2
    $actual = (git cat-file blob "HEAD:$path" | Out-String)
    # hash the bytes exactly as git reports them and compare with $expected
}
```

The manifest covers every published evidence file except itself and the
verification script that produces it.

### 7.3 Verification

`docs/reviews/d3-impl-01-r3/evidence/verify-manifest.ps1` recomputes every hash
from the committed blob and compares it with the manifest:

```powershell
pwsh -File docs/reviews/d3-impl-01-r3/evidence/verify-manifest.ps1
# manifest verified against the committed blobs: 10 files
```

`-Write` regenerates the manifest from `HEAD` after the evidence commit.

---

## 8. Verification

### 8.1 Local (win-x64, .NET SDK 10.0.404, Rust 1.99.1)

| Gate | Result |
| --- | --- |
| Rust `cargo fmt --check` (bridge, D2 differential, R3 oracle) | Pass |
| Rust `cargo clippy --locked --release --offline --all-targets -- -D warnings` (bridge, D2 differential, R3 oracle) | Pass |
| Rust `cargo test --locked --release --offline` (bridge) | Pass |
| .NET locked restore (`--locked-mode`), Release build, `dotnet format --verify-no-changes` | Pass, 0 warnings |
| DogwoodSharp tests (net8.0 / net10.0) | 71/71 each |
| CedarSharp adapter tests (net8.0 / net10.0) | 52/52 each (45 R2 controls + 7 R3 controls) |
| D2 conformance differential | 39/39 pass |
| FoundationSmoke (net8.0 / net10.0) | Pass |
| NativeAOT FoundationSmoke (net8.0 / net10.0) | Pass |
| R3 harness regression self-test | 9/9 pass |
| R3 upstream differential (net8.0 / net10.0) | 92 cases: 90 pass, **2 fail** (the recorded blocking gaps of §5.1) |
| R3 lifecycle execution (net8.0 / net10.0) | 6/6 scenarios executed and verified per TFM |

### 8.2 Hosted CI

See §9 for the run and job identifiers of the R3 revision.

---

## 9. Hosted CI record

### 9.1 R3 revision

| Item | Value |
| --- | --- |
| Workflow | `DogwoodSharp producer verification` (`.github/workflows/ci.yml`) |
| Event | `push` to `main` |
| Run | **45345013721** — <https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45345013721> |
| Revision | `17b327adf0c5c1ee24ea85e3cd6417df3c41a701` (tree `58d67505df28b16a245e67af176754da5dfe5fe3`) |
| Started / finished | 2026-11-24T07:03:13Z / 2026-11-24T07:28:15Z |
| Result | **success** (12/12 jobs) |

| Job | ID | Result |
| --- | --- | --- |
| Verify osx-arm64 / net8.0 and net10.0 | 138277350519 | success |
| Verify linux-x64 / net8.0 and net10.0 | 138277350895 | success |
| Verify win-x64 / net8.0 and net10.0 | 138277351016 | success |
| Pack D3 package candidates | 138286552245 | success |
| Verify D3 package candidates | 138287071877 | success |
| Package consumer / linux-x64 / net10.0 | 138287351048 | success |
| Package consumer / osx-arm64 / net8.0 | 138287351117 | success |
| Package consumer / osx-arm64 / net10.0 | 138287351124 | success |
| Package consumer / win-x64 / net8.0 | 138287351133 | success |
| Package consumer / linux-x64 / net8.0 | 138287351142 | success |
| Package consumer / win-x64 / net10.0 | 138287351162 | success |
| D3 package consumer summary | 138287811175 | success |

The R3 revision under verification is the evidence-recording commit
`17b327a`; its content is the implementation commit `4c4dcc9` plus the
evidence manifest commit. The upstream differential corpus is not part of the
hosted matrix — it is producer evidence executed by `run.ps1` on win-x64 (see
§8.1 and §10, item 4).

The same record is published as a machine-readable evidence file:
`evidence/r3-hosted-ci-run.log`.

### 9.2 Frozen Q1 subject

| Item | Value |
| --- | --- |
| Run | **45127492373** — <https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45127492373> |
| Revision | `ee870e9fc32b1f20cb20863002bd18f9d4122de1` (the frozen Q1 subject) |
| Started / finished | 2026-11-23T06:55:57Z / 2026-11-23T07:22:51Z |
| Result | **success** (12/12 jobs) |

| Job | ID | Result |
| --- | --- | --- |
| Verify osx-arm64 / net8.0 and net10.0 | 137535096987 | success |
| Verify win-x64 / net8.0 and net10.0 | 137535097253 | success |
| Verify linux-x64 / net8.0 and net10.0 | 137535097265 | success |
| Pack D3 package candidates | 137543897347 | success |
| Verify D3 package candidates | 137544437612 | success |
| Package consumer / linux-x64 / net10.0 | 137544742479 | success |
| Package consumer / win-x64 / net8.0 | 137544742504 | success |
| Package consumer / win-x64 / net10.0 | 137544742509 | success |
| Package consumer / linux-x64 / net8.0 | 137544742516 | success |
| Package consumer / osx-arm64 / net10.0 | 137544742588 | success |
| Package consumer / osx-arm64 / net8.0 | 137544742618 | success |
| D3 package consumer summary | 137545087467 | success |

This run is the hosted-CI binding for the frozen Q1 subject that QD3Q-09
reported as recorded nowhere; the two earlier runs cited by the R2 record
(`45121260293` at `aa9731f`, `45124288319` at `c2fb5ba`) are superseded by it.

### 9.3 R2 historical record

The R2 README cites run `45121260293` at revision `aa9731f` while the R2
evidence log's final entry cites `45124288319` at `c2fb5ba`; the run at the
frozen revision was `45127492373`. The R2 record is left byte-for-byte
unchanged as a historical artifact; the complete R2 CI history is:

| Run | Revision | Result |
| --- | --- | --- |
| 45116965132 | `6bedf858fcdb2a95140ac484d8a7b8dfdc6a9d28` | failure (pre-R2 development push) |
| 45121260293 | `aa9731fe1ba6b29eab8f81700694c45f82256c33` | success |
| 45124288319 | `c2fb5ba5ea37b472d422a6fcd0750db974e70669` | success |
| 45127492373 | `ee870e9fc32b1f20cb20863002bd18f9d4122de1` | success (frozen Q1 subject) |

---

## 10. Exclusions and limits

1. **Independent requalification is not performed here.** This record is
   producer evidence only; an independent reviewer must re-execute the corpus.
2. **No owner acceptance is granted or implied.**
3. **No publication, package push, tag, or release is authorized.** The R3
   changes do not alter the shipped package surface.
4. **Local execution covered win-x64 only.** The hosted CI matrix covers
   linux-x64 and osx-arm64 for the standard suites; the upstream differential
   corpus itself was executed on win-x64 only, because the upstream oracle
   links the pinned upstream crate for the host platform.
5. **The two failing C-23 cases are intentional.** They are the machine-verified
   record of an unresolved blocking gap, not an oversight.
