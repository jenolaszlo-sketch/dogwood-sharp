# DOGWOOD-SHARP-D3-IMPL-01-R5 — Remediation handoff

**C. REMEDIATION REQUIRED** · 25 November 2026 · Owner design decision required

The frozen R4 replay reproduced all four Q2 counterexamples on Windows x64 under .NET 8 and .NET 10. It also found the same clean Deny versus clean Allow discrepancy for two alternate decimal encodings. The two existing ED-06 cases still produce their exact approved observations. The difference starts at the reference's typed-context conversion. Within the approved D3 contract, equivalent Cedar JSON encodings must reach the same evaluation outcome. Preserving the explicit-form Allow and forcing Deny on the equivalent alternate forms would change that contract. R5 stopped at the owner decision boundary before changing production, tests, harnesses or CI.

## 1. Provenance and preserved review

| Record | Commit | Tree |
| --- | --- | --- |
| Frozen R4 reproduction subject | `a244d65e3c1e803626e796bdee3af08a9ca5e89a` | `90e4262b0fb4d9169b94ae6eae11980f54a964d4` |
| Parent associated with the previously reported tree | `554edc1103192e68933ca71d1e03f2af72cb0920` | `1aa73fd2a08e527b5d04c8a5fba487e22160e906` |
| Independent Q2 preservation commit | `dd1aa2fd3de44f914b412bf7df4a111dfbffe931` | `47f51239fdf117fd3604e730802c99924d940732` |
| Frozen Q1 subject | `ee870e9fc32b1f20cb20863002bd18f9d4122de1` | `6a5a0d3966e9f9100ebc34cad3dae04b6264cc88` |

Local Git and GitHub confirm the R4 identity. `git diff 554edc1 a244d65` identifies only the R4 README. The complete independent Q2 report and both framework results, lifecycle results, test records, harness controls and CI evidence were reviewed before the replay. Q2 was committed separately on `main`. Its original files survived byte-for-byte, including all 13 SHA-256 manifest entries. The evidence-directory Git attribute prevents line-ending conversion. The Q1 subject and R2–R4 historical files remain intact. [Independent Q2 report](../d3-qualification-01-r4-q2/README.md) · [Frozen R4 commit](https://github.com/jenolaszlo-sketch/dogwood-sharp/commit/a244d65e3c1e803626e796bdee3af08a9ca5e89a)

## 2. Frozen baseline reproduction

The R4 export came from `git archive` at the frozen commit. The unchanged oracle built using `cargo tree --locked --offline` and `cargo build --locked --offline --release` on Rust 1.99.1. The unchanged managed probe used `dotnet restore --locked-mode` and `dotnet build -c Release --no-restore` with SDK 10.0.404. Local cache restore used NuGet audit disabled. The Git archive emitted SourceLink warnings about the absence of repository metadata. The baseline pair reused the R4 Windows bridge image after SHA-256, ABI and lockfile checks.

Each cell ran the full 18-case replay fixture using `PrepProbe.dll differential <fixtures> <oracle> <register> <observations> <lifecycle> <scratch>`. The two original ED-06 cases and their exact approved fields were retained as controls. Each alternate case used the same policy, schema, principal, action, resource and constructor argument as its explicit-escape counterpart. The new input file uses the R4 ordinary-agreement classification for all unapproved cases. The raw results preserve the stage, decision, determining rules, clean-Allow flag, errors and error categories for each side. The replay exited 1 on both TFMs because of the six deliberately exposed unexpected differences. [Replay inputs](evidence/fixtures.json) · [Scoped register](evidence/register.json) · [Baseline matrix](evidence/matrix.json)

| Type and policy check | Explicit extension escape | Implicit extension object | Short string |
| --- | --- | --- | --- |
| IP, policy matches | Deny / Allow, ED-06 | Deny / Allow, unexpected | Deny / Allow, unexpected |
| Datetime, policy matches | Deny / Allow, ED-06 | Deny / Allow, unexpected | Deny / Allow, unexpected |
| Decimal, policy matches | Allow / Allow | Deny / Allow, unexpected | Deny / Allow, unexpected |
| IP, datetime and decimal, policy does not match | Deny / Deny, all three | Deny / Deny, all three | Deny / Deny, all three |

Each pair lists upstream first and D3 second. All 18 inputs reached policy evaluation on each framework. Both sides reported zero operation failures and zero errors. Every Deny / Allow observation differed in exactly `decision`, `determining` and `cleanAllow`. Each cell had 12 passes, six failures and zero skips. The original R4 boundary and malformed-input coverage remains in the Q2 record. [Windows .NET 8 results](evidence/observations-net8.0.json) · [Windows .NET 10 results](evidence/observations-net10.0.json) · [Replay log](evidence/replay.log)

| Baseline execution identity | Value |
| --- | --- |
| Environment | Windows x64 build 26200, .NET SDK 10.0.404, .NET 8.0.31 and .NET 10.0.15, Rust 1.99.1 |
| Rebuilt oracle binary SHA-256 | `05994178be3ee951b1e63ca2e26735a711ec28070b2a3209431410eb5cb670b6` |
| Upstream pins | Dogwood 1.1.2 at `929f49e7305534ac61ef06cf8e47269c596dc851`, reference Cedar 4.11.0 |
| D3 pins | CedarSharp 1.0.0, native Cedar 4.13.0 |
| Windows Dogwood bridge SHA-256 | `94811767dc92488b2f47ef6d6434677c03798eb9d0e7f26fc56f47610131bda5`, ABI 1 |
| Windows CedarSharp native SHA-256 | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` |

[SDK and runtime](evidence/dotnet-environment.log) · [Oracle build](evidence/oracle-build.log) · [Managed build](evidence/probe-build.log) · [Native identities](evidence/native-identities.txt)

## 3. Root cause and owner decision

1. **Schema and policy lowering succeed.** The fixture lowers the same rule and typed action context for every encoding of a given value. Its context is request data. It does not change the lowered policy or canonical schema.
2. **The pinned upstream Dogwood reference loses the extension type at the event boundary.** R4 oracle `json_to_value` emits a Dogwood `Value::String` for the short form and a plain `Value::Object` for IP and datetime objects. `Event` then takes those values as request context. Pinned upstream `build_context` calls `value_to_expr` and `Context::from_pairs`, so Cedar receives the string or record as such. Dogwood 1.1.2 has a decimal value variant and its escape converter recognizes explicit decimal JSON. The same converter cannot produce IP or datetime values, and the alternate decimal forms remain a string or record. Schema validation of the lowered policy succeeds independently of this value conversion. The reference results are clean Deny for the affected permits. This is a Dogwood-to-Cedar value-boundary issue. The official Cedar JSON parser supports these schema-directed constructors even at Cedar 4.11.0. [R4 oracle context conversion](https://github.com/jenolaszlo-sketch/dogwood-sharp/blob/a244d65e3c1e803626e796bdee3af08a9ca5e89a/docs/reviews/d3-impl-01/evidence/differential/r4/upstream-oracle/src/main.rs#L710-L745) · [Pinned Dogwood request construction](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs#L1506-L1553) · [Pinned value conversion](https://github.com/dogwood-policy/dogwood/blob/929f49e7305534ac61ef06cf8e47269c596dc851/dogwood-language/src/api.rs#L1984-L2037)
3. **D3 preserves the typed value.** The self-contained adapter forwards Cedar JSON and the canonical action schema with `validateRequest=true`. The CedarSharp native bridge dispatches to Cedar's JSON authorization API. Under the schema, Cedar turns both object forms into the named extension call and resolves the short form to the same single-argument constructor. Equivalent encodings then produce the same typed value, the same determining ID and the same clean decision. The integration contract prohibits adapter-side coercion or rewriting of authorization-relevant context. [Managed path](https://github.com/jenolaszlo-sketch/dogwood-sharp/blob/a244d65e3c1e803626e796bdee3af08a9ca5e89a/src/DogwoodSharp.CedarSharp/DogwoodCedarAuthorizer.cs#L227-L251) · [CedarSharp native dispatch](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/68ffdafa3bd9a2e4e5744069220bededb5684c5b/native/src/lib.rs#L94-L105) · [Cedar extension JSON parser](https://github.com/cedar-policy/cedar/blob/v4.13.0/cedar-policy-core/src/entities/json/value.rs#L753-L828) · [D3 integration contract](https://github.com/jenolaszlo-sketch/dogwood-sharp/blob/a244d65e3c1e803626e796bdee3af08a9ca5e89a/docs/d3-integration-contract.md#43-scope-context-and-entity-data)
4. **Temporal processing was outside these reproductions.** The four original counterexamples use self-contained bundles. Their first loss occurs before Cedar evaluation in the reference path. The separate trusted-runtime branch also converts JSON through Dogwood `Value` before evaluating temporal/provider data and passes enriched context to CedarSharp. That path has the same representation constraint at the conversion point. Its authorization outcome for a mixed typed-context and history policy remains unverified. The pinned source also lists `duration` as unrepresentable. No `duration` result is claimed here. [Native trusted event construction](https://github.com/jenolaszlo-sketch/dogwood-sharp/blob/a244d65e3c1e803626e796bdee3af08a9ca5e89a/native/dogwoodsharp-bridge/src/d2/runtime.rs#L412-L435)

**Owner decision required.** Authorize a typed-context correction in the pinned reference path and authorize revisiting ED-06 under the resulting observed behavior, or retain the current comparison contract and leave the discrepancy open. I recommend the reference-path correction. An encoding-neutral change to D3 would apply to both the explicit ED-06 cases and their equivalent alternate forms. Preserving the frozen ED-06 Allow while assigning clean Deny to those alternate forms requires new representation-dependent authorization semantics. The current commission bars that change. Normalizing all inputs to the explicit form in D3 would still produce typed Allows. Normalizing upstream JSON to explicit form would help decimal, with IP and datetime still lacking a `Value` variant. This sets the scope of the decision rather than substituting an input filter or altering the evidence register.

## 4. Closure and pending evidence

| Obligation | Status at handoff |
| --- | --- |
| QD3Q-03 and ED-06 | Blocking design decision. Four original cases reproduced on both TFMs, plus two adjacent decimal differences. The original two-case, three-field ED-06 register remains intact. Production correction and permanent regression pass evidence are pending. |
| QD3Q-05, C-14 | Open. Q2's genuine temporal-engine evaluation failure remains pending. The C-23 cases in this replay used no history evaluation. |
| QD3Q-07 | Open. Family validator and requested negative controls remain pending. |
| C-11 and C-21 | Open. Committed fixture and assertion corrections remain pending. |
| QD3Q-12 | Retained. The underlying production paths were unchanged. Unknown diagnostic ID remains Low, duplicate or mismatched exported IDs Low, caller action-entity conflict Medium, multi-action `appliesTo` canonicalization Low, and explicit `CurrentLoggedJson` suppression Low. C-11's committed qualification control remains pending independently. |
| Other Q2 dispositions | Retained as recorded by Q2. The baseline replay provides no new closure claim for them. |

| Required full R5 differential cell | .NET 8 | .NET 10 |
| --- | --- | --- |
| Windows x64 | Pending. R4 baseline replay 18 executed, 12 passed, 6 expected baseline failures, 0 skipped | Pending. R4 baseline replay 18 executed, 12 passed, 6 expected baseline failures, 0 skipped |
| Linux x64 | Pending | Pending |
| macOS ARM64 | Pending | Pending |

There is no remediated implementation revision or R5 hosted execution. The existing R4 terminal run remains the observed historical package-consumer qualification, with 12/12 jobs and six successful JIT and NativeAOT cells at `a244d65`. It provides separate evidence from the differential. No Q3 readiness recommendation was issued. This handoff records the baseline once under the finite evidence-chain rule. [R4 terminal run](https://github.com/jenolaszlo-sketch/dogwood-sharp/actions/runs/45490253791) · [Baseline evidence SHA-256 manifest](evidence/manifest.sha256)

The isolated source export, temporary archive and compiled replay binaries were removed after verification. Exact inputs, raw results and supporting logs were retained. [Replay verification](evidence/replay-verification.log) · [Cleanup record](evidence/cleanup.log)
