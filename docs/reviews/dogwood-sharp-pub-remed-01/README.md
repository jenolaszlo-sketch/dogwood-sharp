# DOGWOOD-SHARP-PUB-REMED-01 — Publication Infrastructure Remediation

Date: 2026-11-28  
Repository: `jenolaszlo-sketch/dogwood-sharp`  
Scope: publication infrastructure only. D1, D2 and D3 remain closed. The
accepted release candidate and all four publication artifacts are unchanged.
No NuGet package or symbol was uploaded. No production OIDC credential was
acquired.

**Disposition: A — ALL FOUR PUBLICATION DEFECTS REMEDIATED; READY FOR
INDEPENDENT DELTA REQUALIFICATION.**

This record does not self-qualify the successor and does not close the
independent findings. Independent delta requalification and a separate explicit
owner publication authorization remain mandatory.

---

## 1. Accepted baseline and independent evidence

The owner accepted the independent DOGWOOD-SHARP-PUB-QUAL-01 disposition:

**C — NOT SUPPORTED; MANDATORY PUBLICATION INFRASTRUCTURE REQUIREMENTS FAILED.**

The independent package at
`C:\Users\Laszlos\source\repos\Solo\dogwood-sharp-pub-qual-01\` was preserved
without modification. All 42 entries in its `SHA256SUMS.txt` were verified
before and after this remediation. The complete `REPORT.md`, the independent
reproducer evidence, the recovery matrix and the gate assessments were read.

| Subject | Identity |
|---|---|
| Canonical main | `6f21032cf0ff27cd994a79459359c979482546e1` |
| Canonical tree | `c1cc11abc77809072dcda38a03124c179749e12d` |
| Publication workflow revision | `dd7d3f9ea38674f09712ac5c7574654f5a738e62` |
| Publication workflow tree | `81f6f33a477802aa07689ee447a6ffc8902ad85c` |
| Accepted release candidate | `58b0a3332391d85cdf19fd7b794d79516d2ecdfe` |
| Binding package-producing run | `46053558029`, attempt 1 |
| Producer hosted dry-run | `46128901899` |
| Independent hosted dry-run | `46131239101` |

The independent finding identifiers F-01 through F-07 are used unchanged in
this record. F-01 through F-04 are the four authorized blockers. F-05, F-06 and
F-07 are treated as described in section 3; F-07 remains an owner-side
prerequisite.

---

## 2. Independent baseline reconciliation

The exact rejected baseline was reconstructed from Git, not from a working-tree
assumption:

- baseline `Reconcile-Publication.ps1` and the baseline workflow were read from
  commit `6f21032` (workflow revision `dd7d3f9`);
- the four accepted packages were the committed byte copies in
  `docs/reviews/dogwood-sharp-pub-prep-01/artifacts/`;
- the independent 42-entry package was hash-verified;
- all baseline defect reproducers were executed locally with no network
  publication.

Baseline evidence: `evidence/baseline-defect-reproduction.json`.

| Defect | Baseline observation | Reproduced? |
|---|---|---|
| F-01 | An extra `qualification-extra.txt` entry was classified `published-matching` / `skip`. | Yes |
| F-01 | An unsafe `../qualification-escape.txt` entry was classified `published-matching` / `skip`. | Yes |
| F-02 | A simulated HTTP 404 was classified `missing` / `push`, with no durable attempt record. | Yes |
| F-03 | Present `.snupkg` files produced only `symbol-server-verification-required`; no PDB query. | Yes |
| F-04 | The original independent PowerShell expression executed `INPUT_INJECTION_REPRODUCED` while returning the exact valid release-candidate SHA. | Yes |

The baseline missing-entry and duplicate-entry probes were refused, but for
incidental reasons: the baseline detected a changed expected entry, not a
symmetric unexpected-entry inventory or an explicit duplicate identity. The
successor replaces both incidental behaviours with deterministic refusal.

---

## 3. Finding-by-finding root cause and remediation

### F-01 — Payload comparison accepted unexpected content

**Root cause.** `Test-PayloadMatch` iterated only the accepted entry names. It
never enumerated the observed public entry set, so any additional public entry
was invisible. It also excluded `_rels/.rels`, `[Content_Types].xml` and all
core-properties entries unconditionally, and it built a dictionary keyed by
entry name, silently collapsing duplicate identities. Repository signature
verification was optional and not required by default.

**Remediation.** `scripts/publication/PublicationArchive.ps1` now:

- validates every archive entry name (absolute paths, backslashes, drive
  designators, `.`/`..` segments, empty segments, control characters,
  percent-encoded separators, trailing dots/spaces and non-NFC names are
  rejected);
- rejects exact duplicate and case-insensitive ambiguous entry identities;
- compares the complete accepted and public entry sets symmetrically;
- rejects unexpected, missing and changed entries;
- permits exactly one additional `.signature.p7s` entry, only when the package
  is repository-signed, the entry is stored uncompressed, non-empty and
  DER-shaped;
- does not exclude content-type, relationship or core-properties entries;
- verifies the nuspec identity and version;
- runs `dotnet nuget verify --all` with revocation offline and requires a
  trusted repository signature when `-RequireRepositorySignature` is set;
- records the original upload SHA-256 and the public signed SHA-256 separately.

`Reconcile-Publication.ps1` uses these controls for every public download and
turns any archive validation error into a deterministic conflict.

Controls: `evidence/archive-controls.json` — 11/11 passed, covering the
accepted original, valid repository-signing transformation, unexpected entry,
missing entry, modified entry, duplicate entry, unsafe path, incorrect
identity/version, invalid signature, untrusted signature and unexpected
signing-related content. The valid-signature fixture is a local, non-public
fixture derived from a genuine repository-signed package; it is never uploaded.

### F-02 — 404 treated as safe-to-push

**Root cause.** The reconciler had no durable attempt record. Any 404 was
classified `missing` and actionable as `push`, even after an upload attempt
whose response was lost, and even though NuGet indexes asynchronously.

**Remediation.** `scripts/publication/PublicationState.ps1` and
`Invoke-PublicationOperation.ps1` implement:

- a hash-chained, append-only journal;
- durable write-ahead `attempt-initiated` records before every upload;
- GitHub-contents-API persistence on the `publication-state` branch, read back
  and verified before the upload starts;
- bounded read-only polling for package visibility;
- explicit states for absent/not-attempted, attempt initiated, outcome unknown,
  accepted-not-yet-visible, matching, conflicting, unavailable and manual
  reconciliation;
- refusal to retry when a prior durable attempt exists and the registry still
  returns 404;
- no automatic retry after an ambiguous transport response;
- no `--skip-duplicate`, no replacement version and no exactly-once claim.

Controls: `evidence/recovery-controls.json` — 19/19 passed, covering genuine
first publication, delayed indexing, response lost, interrupted upload, runner
termination, 404 after an ambiguous attempt, 429, 503, transport failure,
matching/conflicting visibility during reconciliation, inconsistent journal,
resume after completion and dependency ordering.

### F-03 — Symbols had no recovery state machine

**Root cause.** The reconciler emitted a constant
`symbol-server-verification-required` for every symbol package and never queried
the symbol server. A published package was assumed to imply symbols, and a
missing symbol was not distinguished from an upload that was never attempted,
an accepted-but-indexing upload, or an ambiguous upload.

**Remediation.** `PublicationArchive.ps1` binds expected PDB identities to the
accepted inventory:

- Portable PDB GUID from the `#Pdb` stream;
- matching assembly CodeView GUID;
- assembly PDB-checksum equal to SHA-256 of the PDB with the 20-byte PDB ID
  zeroed;
- NuGet symbol-server key `<GUID>ffffffff`;
- `SymbolChecksum: SHA256:<checksum>` on retrieval, as required by NuGet for
  Portable PDBs;
- retrieved PDB length and SHA-256 compared with the accepted PDB entry.

`Reconcile-Publication.ps1` reports explicit symbol states: not attempted,
accepted, outcome unknown, indexing pending, available and verified,
inconsistent/conflicting, and manual reconciliation. It never treats an
unavailable symbol server as proof of absence and never resubmits after an
ambiguous attempt.

Controls: `evidence/recovery-controls.json` covers symbol accepted, outcome
unknown, available/verified, conflict, first symbol publication and adapter
dependency ordering. `evidence/journal-controls.json` covers complete and
partial sequence completion.

### F-04 — Workflow input expression injection

**Root cause.** String `workflow_dispatch` inputs were interpolated directly
into Bash and PowerShell run-script source. A manually supplied expression was
executed while still evaluating to the valid release-candidate SHA. The same
pattern existed in the `gh api` step, which had a repository token.

**Remediation.** `.github/workflows/publish.yml` now:

- binds every dispatch input to a workflow-level environment variable;
- validates all inputs in `Test-PublicationInputs.ps1` before any shell step;
- enforces exact allowlists and formats (lowercase 40-hex commit, positive
  decimal run/attempt, authorization identifier, `true`/`false`, exact
  environment and destination);
- contains no `${{ ... }}` expression inside any `run:` block;
- uses `GITHUB_REF` and validated environment variables in shell scripts;
- performs no `Invoke-Expression`, `iex`, `eval` or equivalent dynamic
  execution;
- runs `Test-WorkflowInputSafety.ps1` in both jobs;
- revalidates immediately before the credential-bearing publication stage;
- revalidates inventory identity and artifact hashes immediately before each
  upload.

The original independent reproducer was executed against the successor:
the baseline pattern still emits `INPUT_INJECTION_REPRODUCED`; the successor
rejects the same value before it reaches a shell and never emits the marker.
Twenty-one controls cover expression evaluation, command substitution,
backticks, quote breakout, multiline input, malformed identifiers, unexpected
environment/destination values and the valid-input path:
`evidence/workflow-input-safety.json` — 21/21 passed.

### F-05 — Workflow lock is not a separate trust anchor

The lock remains a bounded control against isolated or accidental drift. It is
not an independent integrity root: a repository writer can co-change the
workflow, verifier and lock. The repository owner/admin remains the explicit
trust boundary. This limitation is unchanged and is documented in
`docs/release-operations.md`; stronger separation remains an owner-side
decision.

### F-06 — Repository signature verification was optional

Repository signature verification is now mandatory for live reconciliation:
the public package must contain the repository signature and
`dotnet nuget verify --all` must succeed with a trusted **Repository**
signature. Payload comparison remains independent of signature metadata because
NuGet.org signs after upload. The valid-signature and invalid/untrusted
signature controls are in `evidence/archive-controls.json`.

### F-07 — Owner-side prerequisites

`NUGET_USER`, the NuGet Trusted Publishing policy, package ownership and the
live publication authorization remain owner-side prerequisites. This record
does not create a live owner-publication authorization record and does not
claim those prerequisites are complete.

---

## 4. Integrated publication state machine

The full specification is in `STATE-MACHINE.md`; the durable record format is
in `RECOVERY-RECORD.md`. The essential contract:

- owner authorization binds the exact inventory SHA-256, release candidate,
  source run/attempt, destination, environment and four artifact hashes;
- the journal binding record uses the same inventory hash and authorization ID;
- the workflow-level concurrency group remains publication-wide;
- every upload is preceded by a durable attempt record and an immediate
  re-hash of the complete authorized artifact set;
- unknown state cannot become implicit upload authorization;
- a partial sequence cannot be reported complete (the final workflow step
  requires all four verified records);
- manual recovery cannot bypass package identity or payload verification.

No generalized release framework was introduced. The implementation is five
small PowerShell files plus the workflow.

---

## 5. Four-operation interruption and recovery matrix

Each operation is reconciled independently. The matrix and executed evidence
are in `INTERRUPTION-RECOVERY-MATRIX.md`. The adapter package requires the core
package to be publicly verified matching; adapter symbols require the adapter
package. `evidence/recovery-controls.json` executes the four-operation campaign
and the interruption scenarios with a controlled transport stub and a local mock
registry. No real registry or symbol service was contacted.

---

## 6. Verification executed

| Verification | Result | Evidence |
|---|---|---|
| Independent package 42-entry SHA-256 manifest | Pass, before and after | `evidence/independent-package-verification.json` |
| Baseline defect reproduction | Baseline defects reproduced | `evidence/baseline-defect-reproduction.json` |
| Original 19 producer controls | 19/19 pass | `evidence/producer-controls-successor.json` |
| Archive/payload/signature controls | 11/11 pass | `evidence/archive-controls.json` |
| Publication recovery controls | 19/19 pass | `evidence/recovery-controls.json` |
| Journal and completion controls | 5/5 pass | `evidence/journal-controls.json` |
| GitHub journal provider controls | 2/2 pass | `evidence/github-journal-controls.json` |
| Workflow input safety controls | 21/21 pass | `evidence/workflow-input-safety.json` |
| Workflow lock verification | Pass | `evidence/workflow-lock-verification.txt` |
| Hosted dry-run on the successor | Recorded in `HOSTED-VERIFICATION.md` | `evidence/hosted-*.json` |

Distinction of execution modes:

- **Executed locally:** baseline reproducers, original 19 controls, all new
  archive/recovery/journal/input controls, signature verification through the
  installed .NET SDK, local journal durability and termination simulation.
- **Executed against a controlled substitute:** package and symbol transport,
  registry 404/429/503/transport failures, delayed indexing, response loss and
  runner termination (mock HTTP registry and stub `dotnet`).
- **Inspected only:** the live NuGet Trusted Publishing exchange, the real
  NuGet.org repository signature on the successor packages, the live
  `publication-state` branch, and the protected environment approval.
- **Not executed by design:** any real package or symbol upload, any production
  OIDC credential exchange, release tagging or GitHub release publication.

---

## 7. Artifact identity verification

The four accepted artifacts are unchanged. Their committed Git blobs and
SHA-256 values were verified after remediation:

| Artifact | SHA-256 |
|---|---|
| `DogwoodSharp.0.1.0-preview.1.nupkg` | `384e013a236bfcb9b761a92ff861f0df54b2e8a736a5f2c1fb778268307b1911` |
| `DogwoodSharp.0.1.0-preview.1.snupkg` | `e35ae30ef409b44402f32361dcec800a57b7499cd610e2345773d6adafbad563` |
| `DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg` | `3d882e6be062ed998cf05e485b06dee4868819785e22fdf702d5050ba306daa4` |
| `DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg` | `49cefe0d69af687659c60decb141c56a1773b0583784586cd3ee09e5ea1803fd` |

No rebuild, repack, re-signing or artifact transformation was performed. The
product source, native source, product tests, dependency versions and
dependency locks were not modified.

---

## 8. Modified-file inventory

Publication infrastructure:

- `.github/workflows/publish.yml`
- `scripts/publication/PublicationArchive.ps1` (new)
- `scripts/publication/PublicationState.ps1` (new)
- `scripts/publication/Invoke-PublicationOperation.ps1` (new)
- `scripts/publication/Initialize-PublicationJournal.ps1` (new)
- `scripts/publication/Test-PublicationInputs.ps1` (new)
- `scripts/publication/Test-WorkflowInputSafety.ps1` (new)
- `scripts/publication/Test-PublicationCompletion.ps1` (new)
- `scripts/publication/Reconcile-Publication.ps1` (rewritten)
- `docs/reviews/dogwood-sharp-pub-prep-01/publication-workflow.lock.json`
  (successor publication blobs)

Publication-specific tests and operational documentation:

- `scripts/publication/tests/Mock-PublicationRegistry.ps1`
- `scripts/publication/tests/Test-PublicationArchiveControls.ps1`
- `scripts/publication/tests/Test-PublicationRecovery.ps1`
- `scripts/publication/tests/Test-PublicationJournalControls.ps1`
- `scripts/publication/tests/Test-PublicationGitHubJournal.ps1`
- `docs/release-operations.md`
- `docs/reviews/dogwood-sharp-pub-remed-01/` (this append-only record)

The four artifacts, the publication inventory, the accepted package inventory,
the accepted verification report, the CedarSharp hash manifest and the dry-run
authorization are unchanged.

---

## 9. Remaining owner-side prerequisites

1. Configure NuGet Trusted Publishing for repository
   `jenolaszlo-sketch/dogwood-sharp`, workflow `.github/workflows/publish.yml`,
   environment `nuget-publication`, for the two package IDs.
2. Set `NUGET_USER` to the NuGet profile username.
3. Confirm account ownership or first-publication permission for
   `DogwoodSharp` and `DogwoodSharp.CedarSharp`.
4. Preserve the original source Actions artifact before 2027-02-25, or qualify
   an authenticated archival fallback.
5. Protect `main` and the publication workflow/verifier/lock/environment
   settings against unauthorized writes; decide whether to retain administrator
   environment bypass and owner self-approval.
6. Create a live `OWNER-AUTHORIZATION-PUBLICATION.json` only after the
   successor has passed independent delta requalification and the owner
   separately decides to publish.
7. Do not create a long-lived NuGet API key as a fallback.

---

## 10. Independent delta requalification handoff

The independent requalifier should:

1. verify the 42-entry independent package manifest again and confirm it is
   unchanged;
2. confirm the successor publication workflow and lock blobs;
3. rerun the baseline reproducer against the rejected baseline revision;
4. run the six evidence scripts in `scripts/publication/tests/` and compare the
   pass counts;
5. run the archive controls against the accepted artifacts and the genuine
   repository-signature fixture;
6. run the recovery campaign with the controlled transport stub and mock
   registry, including the termination scenario;
7. verify the hosted dry-run recorded in `HOSTED-VERIFICATION.md` on the exact
   successor workflow revision, including that the publish job was skipped and
   no credential was acquired;
8. verify the four accepted artifact hashes and the absence of product-source
   changes;
9. independently decide whether the successor is ready for a separate owner
   publication authorization.

This record does not self-qualify the successor and does not close F-01 through
F-07.

---

## 11. Authority exclusions

This commission did not and does not authorize:

- actual NuGet publication;
- symbol publication;
- production OIDC credential acquisition;
- live owner-publication authorization record creation;
- release tagging;
- GitHub release publication;
- product requalification;
- dependency upgrades;
- new development stages;
- destructive repository cleanup.

No public package or symbol was uploaded. No production credential was
acquired.
