# Canonical evidence hashing contract — DOGWOOD-SHARP-D3-Q3-REMED-01

**Status**: binding for remediation and future D3 evidence manifests.
**Date**: 27 November 2026

## 1. Canonical identity of committed evidence

The canonical identity of a committed evidence file is the SHA-256 of the
**committed Git blob bytes**:

```text
git cat-file blob <commit>:<repo-relative-path> | sha256
```

The working tree is not canonical. Checkout line endings, `core.autocrlf`,
editor normalization and filesystem metadata must never change a committed
manifest's result.

Manifests use lowercase hexadecimal, two spaces, and a repo-relative path:

```text
<64 hex>  <path>
```

A manifest never contains its own digest. Verification tool:
`scripts/verify-evidence-manifest.js --git <ref> [--base <dir>]`.

## 2. External execution artifacts

Externally retained execution artifacts (CI artifacts, downloaded logs,
native images, package candidates) are hashed over the **exact retained
bytes**. The record must identify:

- the SHA-256 of each artifact;
- the producing run/job or reviewer command;
- the artifact name and download/retention method;
- the platform, RID, TFM and toolchain;
- whether the bytes are known to be reproducible across runs.

A CI artifact hash is not a claim that an independent build produces the same
bytes. Byte reproducibility is recorded separately (see the binary
reproducibility investigation) and is not inferred from a passing manifest.

## 3. Text, CRLF and LF

- Committed text evidence is hashed as the Git blob (LF for this repository).
- A working-tree CRLF image is a different byte sequence and therefore a
  different SHA-256. It is not an alternate spelling of the same identity.
- Verification may **report** that a digest matches only after a CRLF/LF
  transform, but must still fail. Tool status: `newline-only-mismatch`.
- A manifest generated from CRLF working-tree bytes is not canonical and may
  not be presented as a committed-blob manifest.

## 4. Binary artifacts

Native images, NuGet packages, executables, PDBs and archives are opaque.
`binary` attributes are declared in `.gitattributes`; they are never text
normalized. Their identity is the exact byte hash, and their provenance
includes the build graph and toolchain.

## 5. Generated execution output

Observations, lifecycle JSON, TRX files and console logs are generated. Their
exact bytes are recorded. Where generation is known to be nondeterministic
(set ordering, random scratch paths, timestamps), the evidence record states:

- which fields are nondeterministic;
- the semantic projection used for comparison;
- that the raw bytes are not claimed to be cross-run identical.

## 6. Historical signatures

A historical manifest is preserved with its original bytes and original
verification semantics. It is never regenerated to make it "valid". A
successor manifest is a new, separately identified record. The original and
successor identities are both retained.

## 7. Verification controls

`scripts/self-test-evidence-manifest.js` exercises:

- exact match passes;
- changed content fails;
- missing artifact fails;
- stale checksum fails;
- CRLF file against LF digest fails as `newline-only-mismatch`;
- the independent Q3 reviewer manifest verifies at its commit;
- a stale copy of that manifest fails.

The tool is byte-exact and platform-independent; it is run on Windows and
Linux as part of remediation verification.
