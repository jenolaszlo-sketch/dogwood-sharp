# Dependency-ordered implementation plan

Every milestone requires technical qualification before a separate owner acceptance
decision. The owner has commissioned D1. D2 and later remain gated. Work packages
are bounded to the objective shown; expand executable contracts rather than
implementing speculative infrastructure.

## D0: Discovery and project foundation

- Dependencies: None.
- Work packages / scope: Inspect pinned sources; bounded spike; empty builds; documents.
- Public API: None; assembly metadata only.
- Explicit exclusions: Production engine and publication.
- Security considerations: Accurate reference/security labeling.
- Required tests: Build/format/smoke; spike on available host.
- Qualification matrix: Windows x64 both TFMs locally; CI matrix foundation.
- Acceptance criteria: All documents traceable; foundation builds; owner reviews ADR.
- Expected evidence: Source pins, spike assertions, verification record.
- Publication eligibility: Never.

## D1: Versioned native bridge and parsing

- Dependencies: D0 owner acceptance.
- Work packages / scope: ABI/version/buffer lifecycle; parse limits; diagnostic wire format.
- Public API: Parse result and library version only.
- Explicit exclusions: Temporal authorization, providers.
- Security considerations: Panic/UTF-8/allocation bounds.
- Required tests: Malformed input, ownership, parser goldens.
- Qualification matrix: Three RIDs, both TFMs for interop.
- Acceptance criteria: Independent ABI/parse qualification; owner acceptance.
- Expected evidence: ABI spec, test logs, manifests.
- Publication eligibility: Internal artifacts only.

## D2: Lowering, schemas and validation

- Dependencies: D1 (accepted with qualifications and closed).
- Work packages / scope: Schema builders; lowered bundle; ID/source map; example import.
- Public API: Immutable validated bundle and findings.
- Explicit exclusions: Authorizing unvalidated policies.
- Security considerations: Reject unsupported features; schema/binding collisions.
- Required tests: Upstream examples, schema goldens, macros and invalid policies.
- Qualification matrix: Three RIDs x two TFMs.
- Acceptance criteria: Export/validation parity for declared feature set.
- Expected evidence: Pinned fixture manifest, differential report.
- Publication eligibility: Internal artifacts only.
- Status: Implemented. See [the D2 producer report](d2-implementation.md); independent
  qualification and owner acceptance are separate decisions.

## D3: CedarSharp interoperability

- Dependencies: D2.
- Work packages / scope: Complete request/entity export; callback prototype; version pair; error mapping.
- Public API: Explicit experimental CedarSharp backend only after gate.
- Explicit exclusions: General temporal claims, CedarSharp changes in this repo.
- Security considerations: Mixed entity conformance; raw vs clean Allow.
- Required tests: Cedar-only corpus, action hierarchy, escaping, policy errors, AOT callback.
- Qualification matrix: Three RIDs x two TFMs.
- Acceptance criteria: No unexplained parity differences; fallback A decision if infeasible.
- Expected evidence: Compatibility matrix, independent review.
- Publication eligibility: No public release.

## D4: Event model and reference temporal execution

- Dependencies: D2.
- Work packages / scope: Event signatures; logged/request bags; session ordering; history caps; replay.
- Public API: Reference session and trusted event contract.
- Explicit exclusions: Durable storage, distributed coordination.
- Security considerations: Identity/order validation; cancellation state.
- Required tests: Temporal regression corpus, history-only events, restart/replay fixtures.
- Qualification matrix: Three RIDs x two TFMs.
- Acceptance criteria: Declared operators match upstream; limits fail explicitly.
- Expected evidence: Corpus manifest and resource-bound report.
- Publication eligibility: No public release.

## D5: Temporal authorization and differential conformance

- Dependencies: D3 and D4.
- Work packages / scope: Native reference authorizer; backend comparison; rule diagnostics.
- Public API: Explicit backend selection; clean-Allow enforcement view.
- Explicit exclusions: Production-ready temporal claim.
- Security considerations: Generated context ownership; provider/temporal failures.
- Required tests: Mixed corpus, window endpoints, negative/forbid cases, lifecycle races.
- Qualification matrix: Three RIDs x two TFMs; native memory checks.
- Acceptance criteria: Zero unexplained differences in supported corpus; separate owner acceptance.
- Expected evidence: Independent conformance report.
- Publication eligibility: Candidate for later preview review only.

## D6: Pluggable backends and providers

- Dependencies: D5.
- Work packages / scope: Provider resolver contract; optional MCP utility; backend failure protocol.
- Public API: Opt-in provider/backend interfaces.
- Explicit exclusions: Network providers by default, durable implementation.
- Security considerations: Purity, resource limits, observe failure semantics.
- Required tests: Provider errors, output types, cancellation, deterministic replay.
- Qualification matrix: Three RIDs x two TFMs for enabled features.
- Acceptance criteria: Contract probes pass; unsupported backends rejected.
- Expected evidence: API review and provider isolation evidence.
- Publication eligibility: Candidate only.

## D7: Security hardening

- Dependencies: D5; D6 if included.
- Work packages / scope: Threat-driven adversarial tests; diagnostics redaction; capacity policy.
- Public API: Only security-reviewed controls.
- Explicit exclusions: Distributed spending coordinator.
- Security considerations: Tenant isolation, replay, native exhaustion, event authenticity boundaries.
- Required tests: Fuzz/adversarial, concurrency, isolation and failure injection.
- Qualification matrix: All supported backend/RID/TFM combinations.
- Acceptance criteria: Independent security review; critical findings resolved; owner acceptance.
- Expected evidence: Threat-control traceability and residual-risk record.
- Publication eligibility: Not alone sufficient.

## D8: Cross-platform package qualification

- Dependencies: D7.
- Work packages / scope: Stage manifests/licenses; exact package verification; package-only consumers; OIDC workflow.
- Public API: No policy API expansion.
- Explicit exclusions: New RIDs without qualification.
- Security considerations: Supply-chain provenance; exact artifact integrity.
- Required tests: NuGet archive, native hash, AOT/trim, dependency audits, publication refusal.
- Qualification matrix: win-x64/linux-x64/osx-arm64 x net8.0/net10.0.
- Acceptance criteria: Every claimed environment passes exact package tests.
- Expected evidence: Package hashes, CI runs, independent consumer report.
- Publication eligibility: Qualified candidate only.

## D9: Preview readiness

- Dependencies: D8.
- Work packages / scope: Document supported subset and limitations; migration/version rules; owner release decision.
- Public API: Preview API only.
- Explicit exclusions: Stable or production guarantee inferred from preview.
- Security considerations: Known issues disclosed and bounded.
- Required tests: Re-run exact release gates and install smoke.
- Qualification matrix: All claimed combinations.
- Acceptance criteria: Independent qualification then explicit owner publication authorization.
- Expected evidence: Acceptance record + exact candidate hashes.
- Publication eligibility: Preview only if authorized.

## D10: Stable readiness

- Dependencies: D9 feedback and operational evidence.
- Work packages / scope: Close API/compatibility risks; security review; support policy.
- Public API: Reviewed stable API contract.
- Explicit exclusions: Implicit promotion of reference backend to production.
- Security considerations: Production claims need separate evidence.
- Required tests: Full regression, upgrade, deployment and security gates.
- Qualification matrix: All stable support claims.
- Acceptance criteria: Independent qualification + separate owner acceptance.
- Expected evidence: Stable release dossier, signed-off residual risks.
- Publication eligibility: Stable only if authorized.
