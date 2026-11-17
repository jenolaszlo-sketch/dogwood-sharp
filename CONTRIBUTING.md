# Contributing

Read the accepted ADR, D1 implementation report, and verification record first.
Preserve upstream semantics, keep research separate from `src/`, and document
verified facts separately from proposals. D1 is the only commissioned
implementation milestone.

Run `pwsh ./eng/Verify.ps1` before a contribution. New language capabilities need
upstream differential tests and explicit security boundaries. Keep native binaries,
credentials and temporary logs out of Git. Dependency upgrades require lockfile,
license, feature and differential review. Use exact upstream revisions.
Owner acceptance is distinct from technical qualification and publication.
