# Release operations — exact-artifact NuGet publication

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
- uses a workflow-level concurrency group and a local lock for overlapping
  attempts;
- pins every action to an immutable commit SHA;
- performs no restore, build, pack, native compilation, re-signing or artifact
  transformation;
- acquires OIDC credentials only in the `publish` job, after the environment
  approval and after a second live verification.

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
   hashes, cross-checks the verified and live plans, then requests the
   short-lived OIDC key.
7. Push order:
   1. `DogwoodSharp.0.1.0-preview.1.nupkg` with `--no-symbols`;
   2. `DogwoodSharp.0.1.0-preview.1.snupkg` explicitly;
   3. `DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg` with `--no-symbols`;
   4. `DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg` explicitly.
8. No `--skip-duplicate` is used. A duplicate or conflict is a hard failure.
9. After publication, verify the public packages and symbols as described
   below.

The short-lived NuGet key is requested immediately before the pushes; NuGet's
temporary keys are valid for one hour.

---

## 7. Repository signing and public verification

NuGet.org may apply a repository signature. The original upload hashes above
must match the accepted artifacts exactly, but the publicly downloaded
`.nupkg` hashes may differ because of that signature. Record the original
upload hash and the public signed hash separately.

Post-publication verification must:

1. download the public `.nupkg` from the flat container;
2. compute and record its public signed SHA-256;
3. compare the package **payload** entries (libraries, native assets, README,
   LICENSE, NOTICE, nuspec) against the accepted original, excluding only
   signature/relationship/content-type metadata added by repository signing;
4. verify the repository signature with `dotnet nuget verify --all` and record
   the result;
5. refuse if the payload differs from the accepted artifact.

`scripts/publication/Reconcile-Publication.ps1` implements the payload
comparison and can run against the live NuGet.org flat container or an
offline feed for rehearsal. The original upload and public signed identities
must be recorded separately.

---

## 8. Symbols

The workflow pushes each `.snupkg` explicitly and uses `--no-symbols` on the
matching `.nupkg` push. This prevents implicit duplicate symbol submission.

NuGet.org does not expose the `.snupkg` through the flat container. Symbol
availability is verified through the NuGet symbol server:

```
https://symbols.nuget.org/download/symbols/<pdb-file-name>/<pdb-guid><age>/<pdb-file-name>
```

The PDB name and GUID/age are read from the accepted symbol package. A
successful symbol-server response with the expected PDB is the supported
availability check. The package page's symbol/source availability is
supporting evidence, not a substitute for the symbol-server response.

If a symbol push is interrupted or ambiguous, reconcile the package state
first, then check the symbol server before attempting any repeat submission.
Do not use a blanket duplicate-suppression flag.

---

## 9. Recovery contract

Package and symbol publication are potentially partially successful. The
workflow is a single ordered attempt and fails on the first error. For an
interrupted or ambiguous attempt:

1. Do not re-run blindly and do not use `--skip-duplicate`.
2. Run `scripts/publication/Reconcile-Publication.ps1` against the live
   NuGet.org flat container. It reports each package as `missing`,
   `published-matching` or `conflict`.
3. A `conflict` means the registry already has that ID/version with a
   different payload. Stop. Do not overwrite, replace or create a new version
   without a separate owner authorization.
4. A `published-matching` package is already published; do not push it again.
5. Resume only the `missing` authorized artifacts, in the approved order.
6. For symbols, check the symbol server for the expected PDBs before any
   retry. Treat a missing symbol as an incomplete authorized symbol
   publication; treat a different symbol as a conflict.
7. Record the partial state, the reconciled actions, the original and public
   package hashes, and the symbol-server results.

No replacement versions, no unapproved packages and no silent duplicate
suppression are permitted.

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
