# Publication-infrastructure verification — `dd7d3f9`

**Commission**: `DOGWOOD-SHARP-PUB-PREP-01`
**Baseline**: `6d7032e` / tree `b5a030ad`
**Workflow revision**: `dd7d3f9ea38674f09712ac5c7574654f5a738e62` / tree `81f6f33a477802aa07689ee447a6ffc8902ad85c`
**Hosted dry-run**: `46128901899` at `dd7d3f9`
**Disposition**: **A — infrastructure implemented and producer-verified; live publication not authorized**

---

## 1. Baseline verification

| Identity | Value | Result |
|---|---|---|
| Canonical main | `6d7032e8048134b3245087043b1b4fa06b567b06` | matches |
| Canonical tree | `b5a030ad33988e16095b3cf4b317310fe04e4630` | matches |
| Owner closure | `ad4de111d50ff0c6aba332c0d4d67726fbc44eaf` | present |
| Production implementation | `7a22871ae89205a51e382c3cdc238190d927a471` | present |
| Release candidate | `58b0a3332391d85cdf19fd7b794d79516d2ecdfe` | matches |
| Release-candidate tree | `4c535ac8640ec5a1086d0956017e80b63850ef98` | matches |
| Q3 manifest | 71/71 | passes |
| Remediation manifest | 74/74 | passes |
| Q3-R1 manifest | 64/64 | passes |
| Release-record manifest | 63/63 | passes |

The binding package-producing run is `46053558029`, attempt 1, head
`58b0a33`, conclusion success, workflow `.github/workflows/ci.yml`, workflow ID
`420173740`, event push.

---

## 2. Source artifact and durable retention

| Item | Value |
|---|---|
| Source artifact | `dogwoodsharp-d3-candidates` |
| Artifact ID | `15226757191` |
| Artifact digest | `sha256:86df4ea69bd39c5ad798b8cd34bc5d32b6dfed1436f85bdad37269f0772ec6ad` |
| Artifact size | 18,794,042 bytes |
| Original expiry | 2027-02-25T19:00:24Z |
| Durable copies | `artifacts/` in this record |

The downloaded source artifact zip hash matched the API digest exactly. The
four extracted files matched the accepted hashes exactly:

| File | Size | SHA-256 |
|---|---:|---|
| `DogwoodSharp.0.1.0-preview.1.nupkg` | 18,772,211 | `384e013a...` |
| `DogwoodSharp.0.1.0-preview.1.snupkg` | 26,896 | `e35ae30e...` |
| `DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg` | 53,207 | `3d882e6b...` |
| `DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg` | 23,593 | `49cefe0d...` |

No artifact was rebuilt, repacked, modified or re-signed. See
`source-provenance.json`.

---

## 3. Hosted dry-run

| Item | Value |
|---|---|
| Run | `46128901899` |
| Attempt | 1 |
| Head | `dd7d3f9ea38674f09712ac5c7574654f5a738e62` |
| Event | `workflow_dispatch` |
| `Verify exact publication plan` | **success** (job `140929529423`) |
| `Publish exact authorized artifacts` | **skipped** (job `140929625815`) |
| Plan artifact | `publication-plan`, id `15256469400`, digest `sha256:8f6f7b2d...` |
| Credentials requested | **false** |
| Upload performed | **false** |

The hosted plan matched the local dry-run plan on inventory SHA-256, release
candidate, source run/attempt, source artifact ID/digest, destination,
environment, package hashes and publish order.

Evidence:

- `evidence/hosted-dry-run.json`
- `evidence/hosted-dry-run-plan.json`
- `evidence/hosted-dry-run-source-metadata.json`

The plan records `status: verified`, `dryRun: true`,
`credentialsRequested: false`, `uploadPerformed: false`, and exactly four
packages in the approved order.

---

## 4. Negative controls

`scripts/publication/Test-PublicationControls.ps1` executed the positive dry-run
and all 18 required negative controls. Result: **19/19 passed**.

Each negative control produced a non-zero exit, no plan file, and the expected
refusal message. The full log is `evidence/publication-controls.log`; the
machine record is `evidence/publication-controls.json`.

The controls cover missing authorization, wrong release candidate, wrong source
run, unsuccessful source run, missing artifact, modified package, modified
symbol package, extra package, wrong package ID, wrong package version,
CedarSharp dependency inclusion, unauthorized ref, environment mismatch,
duplicate identity, expired artifact, unapproved destination, rebuild/repack and
concurrent publication.

The controls executed in the verifier before any plan was written. The workflow
also enforces the main-ref, environment and concurrency guards; the environment
reviewer and branch policy were inspected/configured, not proven by a live
deployment. The local concurrency-lock control is a simulation; the workflow
`concurrency` group is the primary guard.

---

## 5. Workflow lock

`publication-workflow.lock.json` binds the workflow, verifier, harness, lock
checker, comparison/reconciliation scripts, inventory, dry-run authorization and
accepted verification inputs by Git blob SHA. The lock check passed locally and
in the hosted dry-run. The lock is anchored at `91598d9`, an ancestor of the
workflow revision.

---

## 6. Environment verification

`nuget-publication` was created with:

- required reviewer: repository owner (`jenolaszlo-sketch`, id `312601944`);
- `prevent_self_review: false`;
- `can_admins_bypass: true`;
- deployment branch policy: `main` only;
- wait timer: 0.

This is a manual approval capability, not independent review or separation of
duties. The owner can self-approve and bypass. The `main` branch is not
protected and no rulesets exist. The workflow's ref/revision/inventory checks
are the code-level controls; owner-side branch protection and independent
review remain recommendations.

Evidence: `evidence/environment.json`,
`evidence/environment-branch-policies.json`.

---

## 7. NuGet configuration status

The NuGet Trusted Publishing policy is **not configured**. The `NUGET_USER`
repository variable is **not set**. No long-lived NuGet API key exists. The
workflow's live path therefore cannot acquire a key and would refuse at the
login step.

The exact required policy is documented in `README.md` and
`docs/release-operations.md`:

- repository owner `jenolaszlo-sketch`;
- repository `dogwood-sharp`;
- workflow `publish.yml`;
- environment `nuget-publication`;
- package IDs `DogwoodSharp` and `DogwoodSharp.CedarSharp`;
- package create/push scope through the exact repo/workflow/environment;
- no delete/unlist/wildcard scope;
- no long-lived API key.

Package-name-scoped NuGet policies are not available at the time of this
record; the workflow allowlist is the package-level restriction.

---

## 8. Reconciliation and live-contract rehearsal

`Reconcile-Publication.ps1` was exercised against an offline feed:

| Feed state | Result |
|---|---|
| Empty | both packages `missing` / `push` |
| Accepted stable package present | `published-matching` / `skip`; experimental `missing` / `push` |
| Modified stable package present | `conflict` / `refuse` |

Evidence: `evidence/reconciliation-empty.json`,
`evidence/reconciliation-matching.json`,
`evidence/reconciliation-conflict.json`.

The live sequence is documented in `docs/release-operations.md`:

- stable nupkg, stable snupkg, experimental nupkg, experimental snupkg;
- `--no-symbols` on nupkg pushes and explicit `.snupkg` pushes;
- no `--skip-duplicate`;
- repository signing handled by recording original upload and public signed
  hashes separately and comparing payload entries;
- symbol availability via the NuGet symbol server;
- interrupted publication reconciled before any retry; conflicts refuse;
- no replacement versions without separate owner authorization.

Live publication, public package download, repository-signature verification and
symbol-server lookup were **not executed**.

---

## 9. Remaining prerequisites

No infrastructure-verification blocker remains. Live publication requires:

1. NuGet Trusted Publishing policy (owner-side);
2. `NUGET_USER` repository variable;
3. owner publication authorization bound to the exact inventory SHA-256;
4. owner decision on independent review and branch protection;
5. explicit owner publication authorization.

The live publication path is not authorized or executed by this commission.

---

## 10. Conclusion

The exact-artifact publication infrastructure is implemented, locked to the
approved workflow revision, and producer-verified by a local 19/19 control run
and a hosted dry-run. The four approved artifacts are durably retained and
byte-identical to the binding CI artifacts. The live path remains fail-closed
until the owner completes the NuGet Trusted Publishing configuration,
`NUGET_USER`, authorization and any desired independent-review/branch-protection
controls.
