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
