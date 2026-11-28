# Four-operation interruption and recovery matrix

Commission: `DOGWOOD-SHARP-PUB-REMED-01`  
Executed evidence: `evidence/recovery-controls.json`,
`evidence/journal-controls.json`, `evidence/github-journal-controls.json`.

The campaign used the accepted four artifacts, a local mock HTTP registry and
symbol server, a controlled `dotnet` transport stub, and a local journal
provider. No real NuGet endpoint was contacted and no package or symbol was
uploaded.

## 1. Required controls

| Required control | Executed scenario | Result |
|---|---|---|
| Genuine first publication | Four operations uploaded in order through the transport stub; all four public payloads and symbols verified. | Pass |
| Successful publication followed by delayed indexing | Flat container returned 404 for the first four polls, then the staged package became visible. | Pass |
| Upload accepted but response lost | Transport staged the package and exited 42; read-only reconciliation found the matching package. | Pass |
| Upload interrupted before response | Transport exited 42 without staging; reconciliation returned 404; operation held as outcome unknown. | Pass |
| Runner terminated during publication | Test killed the operation process tree while the transport was staged and sleeping; the journal retained `attempt-initiated`; rerun verified without a second upload. | Pass |
| HTTP 404 after an ambiguous attempt | Prior attempt record present; 404 produced `publication-outcome-unknown`, no upload. | Pass |
| HTTP 429 and HTTP 5xx | Mock registry returned 429 and 503; both produced `registry-unavailable`, no upload. | Pass |
| Registry connectivity failure | Mock registry reset the connection; produced `registry-unavailable`, no upload. | Pass |
| Matching package becomes visible during reconciliation | Seeded ambiguous attempt, then staged the package; bounded polling verified it without uploading. | Pass |
| Conflicting package becomes visible during reconciliation | Seeded ambiguous attempt, then exposed a package with an extra entry; conflict and refusal. | Pass |
| Missing or inconsistent durable recovery record | Tampered journal record hash; operation refused before transport. | Pass |
| Resume after a previously completed publication | Re-ran operation 1 after completion; `ALREADY_VERIFIED`, transport count unchanged. | Pass |
| Partial publication cannot be reported complete | Completion check with operations 1 and 3 only; refused `not complete`, no sequence-completed record. | Pass |
| Adapter before core verification | Operation 3 with no verified operation 1; refused before transport. | Pass |
| Symbol upload not attempted | Fresh symbol operation; absent PDBs classified `symbol-upload-not-attempted`. | Pass |
| Symbol upload accepted, indexing pending | Seeded `upload-accepted`; absent PDBs classified `symbol-indexing-pending`, no resubmission. | Pass |
| Symbol upload outcome unknown | Seeded `attempt-initiated`; absent PDBs classified `symbol-upload-outcome-unknown`, no resubmission. | Pass |
| Symbols available and verified | Symbol server served expected PDB bytes; all PDB SHA-256 values matched. | Pass |
| Symbols inconsistent or conflicting | Symbol server served a mutated PDB; conflict and refusal. | Pass |
| Symbol first publication | Transport copied the expected PDB tree; post-upload reconciliation verified all PDBs. | Pass |

## 2. Transport command assertions

The controlled transport recorded the exact command line for each operation:

1. `DogwoodSharp.0.1.0-preview.1.nupkg` with `--no-symbols`;
2. `DogwoodSharp.0.1.0-preview.1.snupkg` without `--no-symbols`;
3. `DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg` with `--no-symbols`;
4. `DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg` without
   `--no-symbols`.

No command contained `--skip-duplicate`. The API key was supplied only through
the environment, never on the command line.

## 3. State persistence

The runner-termination control demonstrated that:

- the `attempt-initiated` record existed in the journal before the transport was
  invoked;
- killing the operation process did not remove or rewrite the record;
- the resumed operation reconciled the visible matching package and appended
  `published-verified` without invoking the transport again.

The GitHub provider controls demonstrated the same write-ahead/read-back and
concurrent-write refusal semantics through a mocked contents API.

## 4. Interpretation

These are controlled simulations of external state. They demonstrate the
successor's decision logic and durable-record behavior. They do not prove the
live NuGet.org response semantics, the live symbol index timing or the live
GitHub state-branch permissions; those remain owner-side and hosted
verification items.
