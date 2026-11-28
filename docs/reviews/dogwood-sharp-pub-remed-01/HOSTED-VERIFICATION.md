# Hosted verification

Commission: `DOGWOOD-SHARP-PUB-REMED-01`

This file is completed from the actual GitHub Actions run after the successor
revision is committed and dispatched. The hosted dry-run must be executed with:

- workflow: `.github/workflows/publish.yml`
- ref: `refs/heads/main`
- `dry_run: true`
- `release_candidate`: `58b0a3332391d85cdf19fd7b794d79516d2ecdfe`
- `source_run_id`: `46053558029`
- `source_run_attempt`: `1`
- `owner_authorization_id`: `DOGWOOD-SHARP-PUB-PREP-01-DRY-RUN-1`
- `publication_environment`: `nuget-publication`
- `destination`: `https://api.nuget.org/v3/index.json`

Required observations:

- the `verify` job succeeds on the exact successor revision;
- the `publish` job is skipped;
- no OIDC credential is requested;
- no package or symbol is uploaded;
- the uploaded plan records `dryRun=true`, `credentialsRequested=false`,
  `uploadPerformed=false`, `rebuilt=false`, `repacked=false`;
- the plan binds the accepted inventory, source run/attempt, artifact digest
  and four package hashes.

Recorded run identity and evidence are appended below after execution.

## Recorded execution

Primary hosted dry-run (exact final publication revision `e895c7d`):

| Field | Value |
|---|---|
| Run | `46142050395`, attempt 1 |
| Head revision | `e895c7d4895ad1978dc61cdf8bd2369196294148` |
| Event / ref | `workflow_dispatch` / `refs/heads/main` |
| Conclusion | `success` |
| Verify job | `140969917778` — `success` |
| Publish job | `140970078766` — `skipped` |
| Input validation step | `success` |
| Workflow input safety step | `success` |
| Workflow lock step | `success` |
| `NUGET_USER` step | `skipped` (dry run) |
| OIDC credential requested | No |
| Upload performed | No |
| Plan artifact | `publication-plan`, id `15261154839`, 4389 bytes |
| Plan artifact digest | `sha256:f1584eb42f30fbe275b83de0e3791c0371dfabeed4ad237efd894db2805299ee` |
| Downloaded plan SHA-256 | `4212a5ed814da84a8750b81498e1d03737f4706048bc85e981f27b5f815ead01` |
| Source metadata SHA-256 | `16308f820f3ffb840f45f6e669064db076dc3076b7ed2f46ead15c323c3317cc` |

An earlier dry-run on implementation revision `b30dce12a76b9a946c3aa81d277af564e2bd572d`
(run `46141440524`) also succeeded with the publish job skipped and produced the
same plan SHA-256. The publication infrastructure is byte-identical between
`b30dce1` and `e895c7d`; only publication tests and this evidence changed.

The downloaded plan recorded `status: verified`, `dryRun: true`,
`credentialsRequested: false`, `uploadPerformed: false`, `rebuilt: false`,
`repacked: false`, inventory SHA-256
`524819fcea620889e45e62c5645a11fec0c8a01a7062973dd8c80bd3901bf252`,
release candidate `58b0a3332391d85cdf19fd7b794d79516d2ecdfe`, source run
`46053558029` attempt `1`, source artifact digest
`sha256:86df4ea69bd39c5ad798b8cd34bc5d32b6dfed1436f85bdad37269f0772ec6ad`
and the four exact accepted package hashes.

Evidence: `evidence/hosted-dry-run.json`,
`evidence/hosted-dry-run-plan.json`,
`evidence/hosted-dry-run-source-metadata.json`.

A hosted dry-run alone is not acceptance. This run demonstrates that the
successor workflow executes its validation path on the exact successor
revision and does not enter the credential-bearing publication path in dry-run
mode.