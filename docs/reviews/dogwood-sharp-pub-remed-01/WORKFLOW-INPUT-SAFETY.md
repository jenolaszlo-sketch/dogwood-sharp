# Workflow input safety assessment

Commission: `DOGWOOD-SHARP-PUB-REMED-01`  
Evidence: `evidence/workflow-input-safety.json` (21/21 controls),
`evidence/baseline-defect-reproduction.json` (F-04 baseline).

## 1. Inputs

| Input | Validation |
|---|---|
| `release_candidate` | Exactly 40 lowercase hexadecimal characters. Bound to the inventory release candidate. |
| `source_run_id` | Positive decimal, at most 20 digits. Bound to the inventory source run. |
| `source_run_attempt` | Positive decimal, at most 6 digits. Bound to the inventory attempt. |
| `owner_authorization_id` | `[A-Za-z0-9][A-Za-z0-9._-]{0,127}`. Bound to the authorization file. |
| `dry_run` | Exactly `true` or `false`. |
| `publication_environment` | Exactly `nuget-publication`. |
| `destination` | Exactly `https://api.nuget.org/v3/index.json`. |

All inputs are first bound to environment variables in the workflow `env:`
block. They are never interpolated into `run:` script source. The validator
rejects empty values, overlong values, line breaks, tabs and control
characters before applying the format allowlist.

## 2. Controls

- No `${{ ... }}` expression appears inside any workflow `run:` block.
- No `Invoke-Expression`, `iex`, `eval` or equivalent dynamic execution.
- No `--skip-duplicate`.
- Every dispatch input has an environment binding.
- The original independent PowerShell expression reproducer still executes
  against the baseline pattern.
- The successor rejects the original expression, command substitution,
  backtick substitution, quote breakout, multiline input, uppercase/short/
  non-hex commit values, negative/expression run IDs, attempt zero,
  authorization expression, environment mismatch, destination substitution and
  dry-run expression.
- Valid inputs remain accepted.

## 3. Credential and publication boundary

Input validation runs in both the verify and publish jobs before the
credential-bearing stage. The publish job revalidates immediately before
`NuGet/login`, and each operation revalidates the owner authorization, inventory
hash, release candidate, source run/attempt, environment and destination before
using the transport. The complete authorized artifact set is re-hashed
immediately before each upload.

The controls never acquire OIDC credentials, never contact NuGet and never
upload anything. The malicious inputs are handled only as inert environment
data.

## 4. Residual risks

- GitHub Actions expression interpolation outside `run:` blocks (for example
  action `with:` inputs) remains data, not shell. The only action input carrying
  a dispatch value is the artifact download `run-id`, which is validated first.
- Repository writers/admins remain inside the trusted control plane; the
  workflow lock is not an independent trust anchor (F-05).
- The owner-side environment approval remains self-approvable and
  administrator-bypassable (F-07).
