# Durable recovery-record specification

Commission: `DOGWOOD-SHARP-PUB-REMED-01`

## 1. Storage

The production journal is stored in the repository on the
`publication-state` branch at:

```
publication-state/journal.jsonl
```

Writes use the GitHub contents API with a write-ahead commit before every
external upload. The branch is created from the invoked workflow commit when it
does not exist. The write is read back and verified before the upload starts.
Runner-local files and artifacts uploaded only at normal workflow completion are
not used as the durable record.

Local controls use the same journal format in a local file through
`-JournalPath`. The GitHub provider is exercised locally through a mocked
contents API; a real state-branch write requires a live publication
authorization and is not performed by this commission.

## 2. Line format

Each line is:

```
<record-sha256>\t<previous-record-sha256>\t<base64(canonical-record-json)>
```

- `record-sha256` is lowercase SHA-256 over the exact UTF-8 record JSON bytes.
- `previous-record-sha256` is empty for the first record, otherwise it must equal
  the previous line's `record-sha256`.
- The record JSON is compact, UTF-8, and contains the fields below.
- Lines are append-only. A partial final line, a reordered line, an edited
  record or a broken chain is detected and causes refusal.

## 3. Record fields

| Field | Meaning |
|---|---|
| `schemaVersion` | `1` |
| `sequence` | 1-based; must equal the previous sequence + 1 |
| `recordedAtUtc` | UTC timestamp |
| `event` | `journal-initialized`, `attempt-initiated`, `upload-accepted`, `upload-outcome-unknown`, `attempt-aborted-before-upload`, `reconciliation-observed`, `published-verified`, `symbols-verified`, `conflict-detected`, `manual-reconciliation-required`, `sequence-completed` |
| `state` | State-machine value from `STATE-MACHINE.md` |
| `inventoryId` | Inventory ID |
| `inventorySha256` | Exact SHA-256 of `publication-inventory.json` |
| `authorizationId` | Owner authorization ID |
| `releaseCandidate` | Release-candidate commit |
| `operationOrder` | 1–4, empty for the binding/sequence records |
| `operationKind` | `package` or `symbols`, empty for sequence records |
| `operationId` | Package ID |
| `operationVersion` | Package version |
| `operationFile` | Exact authorized file name |
| `operationSha256` | Authorized file SHA-256 |
| `operationSize` | Authorized file size |
| `attemptId` | Workflow run/attempt/order/timestamp identity |
| `detail` | Event-specific evidence (transport exit, observed status, public hash, dependency state, etc.) |

Every record after initialization must carry the same inventory, authorization
and release-candidate binding as the initialization record.

## 4. Required records per operation

1. `attempt-initiated` — written before the transport call.
2. `upload-accepted` or `upload-outcome-unknown` — written after the transport
   returns or fails.
3. `reconciliation-observed` — read-only observation.
4. One terminal state: `published-verified` / `symbols-verified`,
   `conflict-detected`, or an explicit hold state.

An operation with only `attempt-initiated` is ambiguous and must not be
retried.

## 5. Failure handling

| Journal condition | Result |
|---|---|
| Missing ref and invalid base revision | Refuse |
| Ref creation failure | Refuse |
| Contents read failure other than 404 | Refuse |
| Commit failure | Refuse |
| Read-back hash mismatch | Refuse |
| Concurrent change detected with the supplied `sha` | Refuse |
| Broken hash chain or malformed line | Refuse |
| Binding mismatch | Refuse |
| Journal above 512 KiB | Refuse |

A refused journal write occurs before the upload, so it cannot create an
untracked publication.

## 6. Recovery

To reconcile an interrupted publication, rerun the workflow with the same
authorization and inventory. The operation driver:

1. reads and validates the journal;
2. reconciles the current public state with bounded read-only polling;
3. marks matching operations verified and skips them;
4. refuses conflicts;
5. holds ambiguous or unavailable states without uploading;
6. uploads only a genuine first-publication state with no prior durable attempt;
7. writes a final `sequence-completed` record only when all four operations are
   verified.

There is no journal rewrite, no compaction, no replacement version and no
automatic duplicate suppression.
