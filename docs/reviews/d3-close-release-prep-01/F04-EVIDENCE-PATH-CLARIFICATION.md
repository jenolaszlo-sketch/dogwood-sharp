# F04 evidence-path clarification (append-only)

**Commission**: `DOGWOOD-SHARP-D3-CLOSE-RELEASE-PREP-01`
**Date**: 27 November 2026
**Record type**: append-only clarification; no signed historical record is
modified

---

## 1. The defect

`docs/reviews/d3-q3-remed-01/findings-closure.json` records the Q3-F04
supporting evidence as:

```
WS-C-DEPENDENCY-PROVENANCE.md; evidence/cedarsharp-provenance.json;
evidence/ci/package-verification-report.json
```

The first two paths resolve inside the remediation record. The third path does
not: `evidence/ci/package-verification-report.json` is not present under
`docs/reviews/d3-q3-remed-01/`. It is present under the Q3 reviewer record at:

```
docs/reviews/d3-qualification-01-q3/evidence/ci/package-verification-report.json
```

The Q3-R1 independent review identified this as a documentation-reference
defect, not an evidence-integrity failure.

---

## 2. Verified content

| Property | Value |
| --- | --- |
| Actual path | `docs/reviews/d3-qualification-01-q3/evidence/ci/package-verification-report.json` |
| Canonical SHA-256 | `fa7dade4807c2e85a3af0e4df8cf8e23d8c1b3295b4c0dca55d5225e2d8d041a` |
| Q3 manifest coverage | `docs/reviews/d3-qualification-01-q3/manifest.sha256` entry 71/71 |
| Content | Producer package verification for the Q3 qualification candidates `DogwoodSharp 0.0.0-discovery.1` and `DogwoodSharp.CedarSharp 0.0.0-experimental.1`, including the committed Cargo lock hash and the three native asset/manifest hashes |

The content is verified and preserved. The Q3 manifest verifies 71/71 at the
release-candidate commit and at the reviewed Q3-R1 identities.

---

## 3. Correction

The correct citation for Q3-F04's package-verification evidence is:

```
docs/reviews/d3-qualification-01-q3/evidence/ci/package-verification-report.json
```

Future records must cite that path. The remediation
`findings-closure.json` is preserved unchanged; this clarification is the
append-only correction. The release-candidate package verification for the
preview versions is a separate, later report recorded in `VERIFICATION.md`.

---

## 4. Effect

This is a non-substantive documentation-reference defect. It does not affect:

- the Q3-F04 disposition;
- the CedarSharp package or native identities;
- the Q3 or remediation manifests;
- any authorization decision; or
- the release-candidate package content.

No signed historical record, manifest, register or fixture is modified.
