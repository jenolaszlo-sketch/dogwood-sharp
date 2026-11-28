# Publication state-transition specification

Commission: `DOGWOOD-SHARP-PUB-REMED-01`  
Scope: the four authorized publication operations. This is a conservative
state model, not an exactly-once protocol.

## 1. Operations

| Order | Operation | Kind | Dependency |
|---:|---|---|---|
| 1 | `DogwoodSharp.0.1.0-preview.1.nupkg` | package | none |
| 2 | `DogwoodSharp.0.1.0-preview.1.snupkg` | symbols | operation 1 publicly verified matching |
| 3 | `DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg` | package | operation 1 publicly verified matching |
| 4 | `DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg` | symbols | operation 3 publicly verified matching |

Each operation has its own journal state. A package being present does not
imply its symbols are present.

## 2. Package states

| State | Entry condition | Permitted next action |
|---|---|---|
| `not-previously-attempted-and-absent` | Flat container 404 after bounded polling; no durable attempt record for the operation; journal binding valid. | One upload, after a durable `attempt-initiated` record and immediate artifact re-verification. |
| `publication-attempt-initiated` | Durable attempt record exists, no later conclusive outcome. | Read-only reconciliation only. Never upload automatically. |
| `publication-outcome-unknown` | Attempt exists; response lost, transport error or 404 after the attempt; registry cannot prove absence. | Hold. Owner reconciliation required. |
| `accepted-by-publication-endpoint-but-not-yet-visible` | Transport returned accepted; flat container still 404 after bounded polling. | Bounded read-only polling on later runs. Never upload again. |
| `published-and-verified-matching` | Public payload, identity and repository signature match the accepted original. | Complete; skip. |
| `published-and-conflicting` | Any unexpected, missing, changed or duplicate entry; identity/version mismatch; signature missing/invalid/untrusted. | Refuse. |
| `registry-unavailable` | HTTP 429, HTTP 5xx, timeout, reset or transport failure after bounded retries. | Hold. Not absence. |
| `manual-reconciliation-required` | Journal missing, malformed, truncated, reordered, hash-inconsistent or not bound to the authorized inventory. | Refuse/hold. Owner reconciliation required. |

Transitions are recorded as append-only journal events. A prior attempt never
transitions directly back to `not-previously-attempted-and-absent`. Only an
explicit `attempt-aborted-before-upload` record (written when immediate artifact
re-verification fails before the transport is invoked) closes an attempt
without an upload.

## 3. Symbol states

| State | Entry condition | Permitted next action |
|---|---|---|
| `symbol-upload-not-attempted` | No durable symbol attempt; expected PDBs absent; matching package verified. | One symbol upload, after a durable attempt record. |
| `symbol-upload-accepted` | Symbol endpoint accepted the upload; indexing may be pending. | Read-only bounded PDB polling. |
| `symbol-upload-outcome-unknown` | Durable symbol attempt without a conclusive outcome. | Hold. Owner reconciliation required. |
| `symbol-indexing-pending` | Upload accepted; one or more expected PDBs absent after bounded polling. | Hold; do not resubmit automatically. |
| `symbols-available-and-verified` | Every expected PDB is retrievable and byte-identical to the accepted snupkg entry. | Complete; skip. |
| `symbols-inconsistent-or-conflicting` | A retrieved PDB differs in length or SHA-256. | Refuse. |
| `symbol-state-requires-manual-reconciliation` | Symbol server unavailable (including 401/403), expected identity cannot be derived, or state is otherwise inconclusive. | Hold. Owner action required. |

## 4. Guards

- **Authorization guard.** The journal binding must match inventory ID,
  inventory SHA-256, authorization ID and release-candidate commit.
- **Artifact guard.** Immediately before the transport call, all four authorized
  files are re-hashed and size-checked against the inventory.
- **Attempt guard.** A new `attempt-initiated` record is refused if a prior
  attempt is not conclusively closed.
- **Durability guard.** The attempt record must be read back with the expected
  hash before the upload starts. If it cannot be written or read back, the
  operation refuses.
- **Dependency guard.** Operations 2 and 3 require operation 1 to be publicly
  verified matching. Operation 4 requires operation 3.
- **Completion guard.** A terminal `sequence-completed` record is written only
  when all four operations are verified. Otherwise the workflow fails.

## 5. Exit semantics

`Invoke-PublicationOperation.ps1` exits:

- `0` — the operation is verified matching or already complete;
- `1` — refused: authorization, identity, payload or signature conflict;
- `2` — incomplete: registry unavailable, delayed indexing, ambiguous outcome
  or manual reconciliation required.

Exit code `2` is never treated as success. The workflow stops and the journal
retains the explicit state.

## 6. Non-claims

- No exactly-once guarantee is claimed. A lost response may have reached the
  registry.
- A 404 is not proof of absence after a durable attempt.
- A package being visible does not prove symbols are indexed.
- Retrieving an individual PDB does not prove the original snupkg container
  bytes.
- The protocol prefers refusal and owner reconciliation over speculative
  automation.
