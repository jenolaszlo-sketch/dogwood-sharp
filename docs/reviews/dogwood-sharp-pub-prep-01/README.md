# DOGWOOD-SHARP-PUB-PREP-01 — Exact-artifact publication infrastructure

**Commission**: `DOGWOOD-SHARP-PUB-PREP-01`
**Date**: 28 November 2026
**Disposition**: **A — PUBLICATION INFRASTRUCTURE IMPLEMENTED AND PRODUCER-VERIFIED; READY FOR OWNER-SIDE NUGET CONFIGURATION AND PUBLICATION AUTHORIZATION**
**Baseline**: canonical `6d7032e` / tree `b5a030ad`
**Publication workflow commit**: `dd7d3f9` / tree `81f6f33a`
**Hosted dry-run**: `46128901899` at `dd7d3f9`
**Verification**: `VERIFICATION.md`
**Inventory**: `publication-inventory.json`
**Machine record**: `publication-record.json`

---

## 1. Disposition

**A — the exact-artifact publication infrastructure is implemented and
producer-verified.**

The live publication path has **not** been executed and is **not** authorized by
this commission. The owner-side NuGet Trusted Publishing policy and the
`NUGET_USER` repository variable are not configured; those are owner-side
publication prerequisites, not infrastructure-verification blockers. The
workflow refuses live publication until they are configured and the owner
creates the publication authorization.

No package was pushed to NuGet.org. No release tag, GitHub release or new
development stage was created.

---

## 2. Binding baseline and artifacts

| Object | Identity |
|---|---|
| Canonical main at start | `6d7032e8048134b3245087043b1b4fa06b567b06` / tree `b5a030ad33988e16095b3cf4b317310fe04e4630` |
| Owner closure record | `ad4de111d50ff0c6aba332c0d4d67726fbc44eaf` |
| Production implementation | `7a22871ae89205a51e382c3cdc238190d927a471` |
| Release candidate | `58b0a3332391d85cdf19fd7b794d79516d2ecdfe` / tree `4c535ac8640ec5a1086d0956017e80b63850ef98` |
| Source CI run | `46053558029`, attempt 1 |
| Source artifact | `dogwoodsharp-d3-candidates`, id `15226757191`, digest `sha256:86df4ea6...` |
| Release differential | `46053620392` |
| Regression run | `46053558043` |
| Evidence verification | `46053625568` |
| Publication workflow run (dry-run) | `46128901899` at `dd7d3f9` |
| Workflow lock | `publication-workflow.lock.json` |

The four approved publication artifacts and their exact hashes are in
`publication-inventory.json`. The external CedarSharp `1.0.0` package is a
verified dependency, not a publication artifact.

### Durable retention

The four accepted artifacts are committed as exact byte copies under
`artifacts/`. They were extracted from the binding source artifact; the source
artifact zip digest and extracted file hashes are in `source-provenance.json`.
No artifact was rebuilt, repacked, modified or re-signed. The original CI
artifact expires 2027-02-25; the repository copies are the durable retention.

---

## 3. Implemented workflow

`.github/workflows/publish.yml` replaces the refusal gate with a manual,
exact-artifact promotion workflow:

- manual `workflow_dispatch` only; no push, pull request, tag or schedule
  trigger;
- `dry_run` defaults to `true`; the `publish` job is skipped for dry-run;
- only `refs/heads/main` is accepted;
- the workflow, verifier, inventory and accepted-verification blobs are checked
  against `publication-workflow.lock.json`;
- the exact source repository, run, attempt, head SHA, event, successful
  conclusion, artifact ID, digest, size and non-expiry are verified;
- only the four approved files are accepted, with exact names, sizes and
  SHA-256 values;
- package IDs, versions, entries, symbol correspondence, native assets, lock
  hash and the CedarSharp dependency identity are verified;
- the owner authorization is bound to the exact inventory SHA-256;
- a protected `nuget-publication` environment, GitHub OIDC through
  `NuGet/login@v1`, a short-lived key and job-scoped `id-token: write` are used;
- every action is pinned to an immutable commit SHA;
- a publication-wide concurrency group and a local lock prevent overlapping
  attempts;
- no restore, build, pack, native compilation, re-signing or transformation
  occurs;
- pushes use `--no-symbols` for nupkgs and explicit `.snupkg` pushes; no
  `--skip-duplicate` or silent conflict suppression is used.

The live sequence, repository-signing handling, symbol-server verification and
partial-publication recovery contract are in
[release operations](../../release-operations.md).

---

## 4. Owner-side configuration and environment reality

### GitHub environment

`nuget-publication` exists with:

- required reviewer: the repository owner (`jenolaszlo-sketch`);
- `prevent_self_review: false`;
- `can_admins_bypass: true`;
- custom deployment branch policy: `main` only;
- wait timer: 0.

This is a manual approval gate, **not independent review or separation of
duties**. The owner can approve their own deployment and can bypass it as an
administrator. The `main` branch is not protected and no rulesets are
configured. The workflow's ref check, revision lock and exact-inventory verifier
are the code-level controls; they do not replace owner-side branch protection or
an independent reviewer. See `evidence/environment.json` and
`evidence/environment-branch-policies.json`.

### NuGet Trusted Publishing (owner-side, not configured)

The owner must create a NuGet Trusted Publishing policy for:

| Field | Value |
|---|---|
| NuGet account/organization | the owner's NuGet account; set `NUGET_USER` |
| Repository owner | `jenolaszlo-sketch` |
| Repository | `dogwood-sharp` |
| Workflow file | `publish.yml` |
| Environment | `nuget-publication` |
| Authorized package IDs | `DogwoodSharp`, `DogwoodSharp.CedarSharp` |
| Policy scope | package create/push through the exact repo/workflow/environment; no delete/unlist/wildcard |

NuGet Trusted Publishing currently scopes policies to the repository, workflow
and environment, not to individual package names. The workflow's explicit
four-artifact allowlist is therefore the package-level restriction. The
`NUGET_USER` repository variable is not set. No long-lived NuGet API key is used
or permitted.

These are owner-side actions. They cannot be completed from this repository and
are not claimed as completed.

---

## 5. Producer qualification

### Local positive dry-run and negative controls

The local harness `scripts/publication/Test-PublicationControls.ps1` executed:

- **1 positive dry-run** using the exact four accepted artifacts; it produced
  the publication plan with `credentialsRequested: false` and
  `uploadPerformed: false`;
- **all 18 required negative controls**, each refusing before any plan or
  upload.

Result: **19/19 passed**. Evidence:
`evidence/publication-controls.json`,
`evidence/publication-controls.log`,
`evidence/publication-plan.dry-run.json`.

| # | Control | Observed refusal |
|---|---|---|
| 1 | Missing owner publication authorization | `Missing owner publication authorization.` |
| 2 | Incorrect release-candidate identity | `Incorrect release-candidate identity.` |
| 3 | Incorrect source workflow run | `Incorrect source workflow run.` |
| 4 | Unsuccessful source workflow | `Source workflow did not complete successfully.` |
| 5 | Missing artifact | `Missing artifact: DogwoodSharp.0.1.0-preview.1.snupkg` |
| 6 | Modified package | `Modified package: DogwoodSharp.0.1.0-preview.1.nupkg` |
| 7 | Modified symbol package | `Modified symbol package: DogwoodSharp.0.1.0-preview.1.snupkg` |
| 8 | Unexpected additional package | `Unexpected additional package: Unexpected.1.0.0.nupkg` |
| 9 | Incorrect package ID | `Incorrect package ID: DogwoodSharp.0.1.0-preview.1.nupkg` |
| 10 | Incorrect package version | `Incorrect package version: DogwoodSharp.0.1.0-preview.1.nupkg` |
| 11 | CedarSharp dependency included | `CedarSharp dependency was included in the publication set.` |
| 12 | Unauthorized ref | `Unauthorized ref: publication is restricted to refs/heads/main.` |
| 13 | Publication environment mismatch | `Publication environment mismatch.` |
| 14 | Duplicate/conflicting identity | `Duplicate or conflicting publication identity.` |
| 15 | Expired/unavailable source artifact | `Source artifact expired: dogwoodsharp-d3-candidates` |
| 16 | Unapproved destination | `Unapproved publication destination.` |
| 17 | Attempted rebuild or repack | `Package entries differ from the accepted inventory...` |
| 18 | Concurrent publication attempt | `Concurrent publication attempt detected.` |

### Hosted dry-run

Run `46128901899` at `dd7d3f9`:

- `Verify exact publication plan`: **success**;
- `Publish exact authorized artifacts`: **skipped** (dry-run);
- no OIDC credential was requested and no package was uploaded;
- the hosted plan matches the local plan on inventory hash, release candidate,
  source run/attempt, artifact digest, destination, environment, package hashes
  and publish order.

Evidence: `evidence/hosted-dry-run.json`,
`evidence/hosted-dry-run-plan.json`,
`evidence/hosted-dry-run-source-metadata.json`.

### Reconciliation rehearsal

`Reconcile-Publication.ps1` was exercised against an offline feed:

- empty feed -> both packages `missing` / `push`;
- accepted stable package present -> `published-matching` / `skip`, experimental
  `missing` / `push`;
- modified stable package present -> `conflict` / `refuse`.

Evidence: `evidence/reconciliation-empty.json`,
`evidence/reconciliation-matching.json`,
`evidence/reconciliation-conflict.json`.

### Executed versus inspected/simulated

- **Executed**: the verifier, the positive dry-run, all 18 negative controls,
  the local reconciliation rehearsal and the hosted dry-run.
- **Inspected/configured**: the `nuget-publication` environment, its reviewer
  and `main`-only branch policy, the workflow concurrency group, the pinned
  action revisions and the workflow lock.
- **Simulated**: the local concurrency-lock control; the live NuGet push,
  symbol-server lookup and repository-signature verification were not executed.
- **Not configured**: the NuGet Trusted Publishing policy, `NUGET_USER`,
  independent reviewer/separation of duties and `main` branch protection.

---

## 6. Remaining owner-side prerequisites

No infrastructure-verification blocker remains. Live publication requires:

1. the NuGet Trusted Publishing policy described in section 4;
2. the `NUGET_USER` repository variable;
3. an explicit owner publication authorization file bound to the exact
   inventory SHA-256;
4. the owner's decision on independent review and branch protection;
5. the owner's explicit publication authorization.

The live path has not been executed and is not authorized by this record.

---

## 7. Authority exclusions

This record does not authorize or perform:

- release tagging;
- GitHub release publication;
- NuGet or symbol publication;
- new development stages;
- dependency modernization;
- deterministic native build remediation;
- destructive repository or worktree cleanup;
- any change to the accepted package artifacts or their source.

Publication remains subject to separate explicit owner authorization after the
owner-side NuGet configuration is complete.

---

## 8. Evidence manifest

`manifest.sha256` covers the files in this review directory except the manifest
itself, using committed Git blob bytes. The manifest is generated after the
record is committed and verified against `HEAD`.
