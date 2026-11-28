# Release operations â€” exact-artifact NuGet publication

**Status**: publication infrastructure implemented and producer-verified under
`DOGWOOD-SHARP-PUB-PREP-01`. Actual publication is **not authorized** by this
document or by that commission.

The binding publication inventory is
`docs/reviews/dogwood-sharp-pub-prep-01/publication-inventory.json`. The
current release candidate is `58b0a33` / tree `4c535ac8`; the binding source
run is `46053558029`.

---

## 1. Approved artifacts

Exactly four artifacts are authorized for the current release candidate:

| Artifact | SHA-256 |
|---|---|
| `DogwoodSharp.0.1.0-preview.1.nupkg` | `384e013a236bfcb9b761a92ff861f0df54b2e8a736a5f2c1fb778268307b1911` |
| `DogwoodSharp.0.1.0-preview.1.snupkg` | `e35ae30ef409b44402f32361dcec800a57b7499cd610e2345773d6adafbad563` |
| `DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg` | `3d882e6be062ed998cf05e485b06dee4868819785e22fdf702d5050ba306daa4` |
| `DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg` | `49cefe0d69af687659c60decb141c56a1773b0583784586cd3ee09e5ea1803fd` |

The external `CedarSharp.1.0.0.nupkg`
(`99cc3659e750f3d5187d7b8c35e84359811a7af1247b3673d893eafc679bf835`) is a
verified dependency and is **not** a publication artifact. It must never appear
in the publication set.

The four artifacts are durably retained under
`docs/reviews/dogwood-sharp-pub-prep-01/artifacts/` as exact byte copies of the
`dogwoodsharp-d3-candidates` artifact (`id 15226757191`,
digest `sha256:86df4ea69bd39c5ad798b8cd34bc5d32b6dfed1436f85bdad37269f0772ec6ad`)
from run `46053558029`, attempt 1. The original CI artifacts expire on
2027-02-25; the repository copies are the durable source.

---

## 2. Workflow invocation

Workflow: `.github/workflows/publish.yml`
Environment: `nuget-publication`
Trigger: **manual `workflow_dispatch` only**. It does not run on push, pull
request, tag or schedule.

Required inputs:

| Input | Current value |
|---|---|
| `release_candidate` | `58b0a3332391d85cdf19fd7b794d79516d2ecdfe` |
| `source_run_id` | `46053558029` |
| `source_run_attempt` | `1` |
| `owner_authorization_id` | dry-run: `DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1`; live: the owner-created publication authorization ID |
| `dry_run` | `true` for the producer dry-run; `false` only for an authorized live attempt |
| `publication_environment` | `nuget-publication` |
| `destination` | `https://api.nuget.org/v3/index.json` |

The verifier rejects any other release candidate, source run, attempt, ref,
environment, destination, authorization ID, artifact set or hash. The workflow
also runs only from `refs/heads/main`.

---

## 3. Owner-side NuGet Trusted Publishing configuration

NuGet Trusted Publishing policies are currently scoped to the GitHub
repository, workflow and environment; per-package policy scoping is not
available. The narrowest available policy is therefore the exact
repository/workflow/environment, and the workflow's explicit package allowlist
is the package-level control.

The owner must configure the following at <https://www.nuget.org/>:

1. Sign in to the NuGet account or organization that owns the package IDs.
2. Open **Trusted Publishing** and add a policy:
   - **Policy name**: `dogwood-sharp-publish` (or an equivalent owner label).
   - **Repository owner**: `jenolaszlo-sketch`.
   - **Repository**: `dogwood-sharp`.
   - **Workflow file**: `publish.yml`.
   - **Environment**: `nuget-publication`.
3. Confirm both package IDs are available for first publication under that
   account:
   - `DogwoodSharp`
   - `DogwoodSharp.CedarSharp`
4. In the GitHub repository, create the repository variable `NUGET_USER` with
   the NuGet username. It is a username, not an API key or secret.
5. Do not create a long-lived NuGet API key as a fallback. The workflow uses
   only the short-lived key returned by `NuGet/login@v1` through GitHub OIDC.

Required policy scope: package **create/push** for the two package IDs (and
their symbol packages), through the exact repository/workflow/environment. No
delete, unlist or wildcard scope is required. Because NuGet does not expose
per-package policy scoping at the time of this record, the workflow allowlist
and verifier are the package-level restriction.

These are owner-side actions. They cannot be completed from this repository
and are not claimed as completed.

---

## 4. GitHub environment and protection reality

The `nuget-publication` environment was created with:

- required reviewer: the repository owner (`jenolaszlo-sketch`);
- `prevent_self_review: false`;
- `can_admins_bypass: true`;
- custom deployment branch policy: `main` only;
- wait timer: 0.

This is a manual approval gate, but **not independent review or separation of
duties**. The owner is the only reviewer and can approve their own deployment;
an administrator can bypass the gate. The repository `main` branch is **not
protected** and no rulesets are configured. The workflow's main-ref check,
workflow-revision lock and exact-inventory verifier are the code-level
controls; they do not replace owner-side branch protection or an independent
reviewer. Configuring those remains an owner-side recommendation.

---

## 5. Workflow controls

The publication workflow:

- is manual-dispatch only and defaults to `dry_run: true`;
- requires `refs/heads/main`;
- verifies the workflow/verifier/inventory blob hashes against
  `publication-workflow.lock.json`;
- downloads only the named `dogwoodsharp-d3-candidates` artifact from the
  exact source run and attempt;
- verifies the source run repository, workflow path/ID, head SHA, attempt,
  event and successful conclusion;
- verifies the source artifact ID, digest, size and non-expiry;
- requires exactly the four approved files, with exact names, sizes and
  SHA-256 values;
- parses each nuspec and verifies package ID, version, entries, symbol
  correspondence, native asset hashes, lock hash, dependency metadata and the
  absence of PDBs/native assets where prohibited;
- reconciles the source artifact with the committed accepted verification
  report and the pinned CedarSharp dependency hash;
- requires the owner authorization bound to the exact inventory hash;
- validates every dispatch input as inert data before any shell step and binds
  the dispatch inputs to environment variables rather than interpolating them
  into run-script source;
- uses a workflow-level concurrency group and a local lock for overlapping
  attempts;
- initializes a durable, hash-chained publication journal before requesting
  credentials;
- executes four separately reconciled operations with a completion check;
- pins every action to an immutable commit SHA;
- performs no restore, build, pack, native compilation, re-signing or artifact
  transformation;
- acquires OIDC credentials only in the `publish` job, after the environment
  approval, the journal binding write and a second live verification.

---

## 6. Live publication sequence

1. The owner configures the NuGet Trusted Publishing policy, `NUGET_USER`, and
   any branch protection/independent reviewer decision.
2. The owner creates
   `docs/reviews/dogwood-sharp-pub-prep-01/OWNER-AUTHORIZATION-PUBLICATION.json`
   with `scope: "publication"`, `publicationAuthorized: true`, the exact
   inventory SHA-256, release candidate, source run, package hashes,
   destination and environment. The template is in
   `docs/reviews/dogwood-sharp-pub-prep-01/OWNER-AUTHORIZATION-PUBLICATION-TEMPLATE.md`.
3. The owner dispatches `publish.yml` on `main` with `dry_run: false` and the
   exact inputs.
4. The `verify` job builds and verifies the plan without credentials.
5. The `publish` job waits for the `nuget-publication` environment approval.
6. After approval, the job re-downloads the original artifact, re-verifies all
   hashes, cross-checks the verified and live plans, initializes the durable
   journal and only then requests the short-lived OIDC key.
7. Each operation runs separately and in order:
   1. `DogwoodSharp.0.1.0-preview.1.nupkg` with `--no-symbols`;
   2. `DogwoodSharp.0.1.0-preview.1.snupkg` explicitly;
   3. `DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg` with `--no-symbols`;
   4. `DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg` explicitly.
8. Before each upload the operation reconciles the current public state,
   appends a durable `attempt-initiated` record, re-hashes the complete
   authorized artifact set, and only then invokes the transport.
9. No `--skip-duplicate` is used. A duplicate, conflict, ambiguous response or
   unavailable registry is a hard failure or explicit hold.
10. A final completion check requires all four operations to be durably
    verified matching; a partial sequence cannot be reported complete.

The short-lived NuGet key is requested immediately before the pushes; NuGet's
temporary keys are valid for one hour.

---

## 7. Repository signing and public verification

NuGet.org applies a repository signature to uploaded packages. The original
upload artifact and the public repository-signed package are different
identities. Record both:

- the accepted original SHA-256 from the publication inventory; and
- the downloaded public package SHA-256.

Post-publication reconciliation:

1. downloads the public `.nupkg` from the flat container;
2. parses the public archive and rejects duplicate, case-ambiguous or unsafe
   entry names;
3. compares the **complete** entry inventory symmetrically against the accepted
   original: unexpected entries, missing entries and changed entries are all
   conflicts;
4. permits exactly one additional entry, `.signature.p7s`, only when it is
   stored uncompressed, non-empty and DER-shaped;
5. verifies the package identity and version from the nuspec;
6. runs `dotnet nuget verify --all` with revocation in offline mode and requires
   a valid, trusted **repository** signature (`Signature type: Repository`);
7. refuses on any payload, identity or signature difference.

`[Content_Types].xml`, `_rels/.rels` and
`package/services/metadata/core-properties/*` are **not** excluded: they are
part of the authorized payload and must match the accepted original. The only
permitted public-package difference is the repository signature entry.

`scripts/publication/Reconcile-Publication.ps1` implements this comparison. It
can run against the live NuGet.org flat container or an offline feed for
rehearsal. Repository signature verification requires the `dotnet` CLI and a
trusted repository certificate chain; the non-public fixture uses a genuine
repository-signed package with its signature stripped to create the accepted
original.

---

## 8. Symbols

The workflow pushes each `.snupkg` explicitly and uses `--no-symbols` on the
matching `.nupkg` push. This prevents implicit duplicate symbol submission.

NuGet.org does not expose the `.snupkg` through the flat container. Symbol
availability is verified through the NuGet symbol server:

```
https://symbols.nuget.org/download/symbols/<pdb-file-name>/<pdb-guid>ffffffff/<pdb-file-name>
```

The expected PDB identity is derived from the accepted symbol package and
cross-bound to the matching assembly:

- the PDB GUID comes from the Portable PDB `#Pdb` stream;
- the assembly CodeView entry must carry the same GUID;
- the assembly PDB-checksum debug entry must equal SHA-256 of the PDB with the
  20-byte PDB ID zeroed;
- the symbol-server request supplies
  `SymbolChecksum: SHA256:<pdb-checksum>`, which NuGet requires for Portable
  PDB downloads;
- the retrieved PDB must match the accepted PDB's byte length and SHA-256.

A successful retrieval verifies the individual PDB bytes. It does **not**
prove byte identity of the original `.snupkg` container; the accepted snupkg
hash remains the container identity.

Symbol states are explicit:

| State | Meaning | Action |
|---|---|---|
| `symbol-upload-not-attempted` | No durable symbol attempt exists and the PDBs are absent. | Eligible for first upload after the package is verified. |
| `symbol-upload-accepted` | The endpoint accepted the upload; indexing may still be pending. | Read-only bounded polling only. |
| `symbol-upload-outcome-unknown` | A durable attempt exists without a conclusive response. | Hold; owner reconciliation required. |
| `symbol-indexing-pending` | Upload accepted, expected PDBs still absent after bounded polling. | Hold; do not resubmit automatically. |
| `symbols-available-and-verified` | Every expected PDB is retrievable and byte-identical. | Complete. |
| `symbols-inconsistent-or-conflicting` | A retrieved PDB differs from the accepted PDB. | Refuse. |
| `symbol-state-requires-manual-reconciliation` | Registry unavailable or state cannot be established. | Hold; owner action required. |

A package being published does not imply its symbols are published. A symbol
upload being accepted does not imply immediate availability. NuGet documents
asynchronous validation and indexing (usually under 15 minutes).

---

## 9. Recovery contract

Publication is four independent operations:

1. `DogwoodSharp` package;
2. `DogwoodSharp` symbol package;
3. `DogwoodSharp.CedarSharp` package;
4. `DogwoodSharp.CedarSharp` symbol package.

Each operation has its own durable state. The adapter package requires the
core package to be publicly verified matching first; adapter symbols require
the adapter package.

### Durable write-ahead journal

Before any external upload, the operation appends a hash-chained
`attempt-initiated` record to an append-only journal committed to the
`publication-state` branch through the GitHub contents API. The record binds:

- inventory ID and SHA-256;
- owner authorization ID;
- release-candidate commit;
- operation order, kind, ID, version, file, SHA-256 and size;
- attempt ID and workflow run identity.

The write must be read back with the expected hash before the upload starts. If
the journal cannot be written or read back, publication is refused. Runner-local
files and artifacts uploaded only at normal job completion are not used as the
durable record.

After the transport returns, the operation appends `upload-accepted` or
`upload-outcome-unknown`, then performs bounded read-only reconciliation and
appends the observed state. The journal is never rewritten; a malformed,
reordered, truncated or hash-inconsistent journal causes refusal.

### State model

| Package state | Meaning | Action |
|---|---|---|
| `not-previously-attempted-and-absent` | No durable attempt exists and the flat container returned 404. | May upload once. |
| `publication-attempt-initiated` | Write-ahead record exists; upload may or may not have happened. | Reconcile; never upload again automatically. |
| `publication-outcome-unknown` | No conclusive response and the package is not visible. | Hold; owner reconciliation required. |
| `accepted-by-publication-endpoint-but-not-yet-visible` | Endpoint accepted the upload; flat container still 404. | Bounded polling; never upload again. |
| `published-and-verified-matching` | Public payload, identity and repository signature match. | Complete. |
| `published-and-conflicting` | Public package differs. | Refuse. |
| `registry-unavailable` | 429, 5xx, timeout or transport failure. | Hold. |
| `manual-reconciliation-required` | Journal inconsistent or state inconclusive. | Hold; owner action required. |

A 404 after any durable attempt is **not** proof of absence and does not
authorize a retry. Timeouts, cancellation, delayed indexing, HTTP 429, HTTP 5xx,
transport errors and ambiguous duplicate responses are likewise not proof of
absence. Only a clean first-publication state (no durable attempt) can authorize
the first upload. There is no exactly-once claim: the protocol cannot prove
that a lost response did not reach the registry. The control is conservative
refusal and explicit owner reconciliation.

No `--skip-duplicate` is used. No replacement package version is created. An
ambiguous operation is never automatically resubmitted.

---

## 10. Current limitations

- The NuGet Trusted Publishing policy is not configured in this repository and
  is an owner-side manual action.
- The `NUGET_USER` repository variable is not set.
- The `nuget-publication` environment reviewer is the owner with self-approval
  and administrator bypass; there is no independent reviewer or separation of
  duties.
- The `main` branch is not protected and no rulesets are configured.
- Actual live publication was not authorized or executed by this commission.
- The live path has been producer-verified by a dry-run and negative controls,
  not by a production package push.
- The `publication-state` branch is created by the live workflow on first use;
  the local controls exercise the same journal semantics through a local
  provider and a mocked GitHub contents API. A real GitHub state-branch write
  is not executed without a live publication authorization.
- Repository-signature verification requires a trusted certificate chain. The
  valid-signature control uses a local, non-public fixture derived from a
  genuine repository-signed package; it does not publish anything.
