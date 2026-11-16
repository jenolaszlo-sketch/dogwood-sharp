# Contributing

Read the commission, proposed ADR, and verification record first. D0 does not
authorize production implementation. Preserve upstream semantics, keep research
separate from `src/`, and document verified facts separately from proposals.
No Penghou-specific infrastructure belongs in this library.

Run `pwsh ./eng/Verify.ps1` before a contribution. New language capabilities need
upstream differential tests and explicit security boundaries. Never commit native
binaries, credentials or temporary logs. Dependency upgrades require lockfile,
license, feature and differential review; use no unpinned upstream branches.
Owner acceptance is distinct from technical qualification and publication.
