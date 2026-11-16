# DOGWOOD-SHARP-DISCOVERY-01
## Architecture discovery, project initialization and implementation planning

### 1. Objective

Establish DogwoodSharp as an independent, open-source .NET library for the Dogwood policy language.

Dogwood extends Cedar with temporal authorization, enabling policies that depend on previous events, action sequences, approvals, spending limits and other history-dependent conditions.

The preferred architectural direction is to reuse CedarSharp for Cedar policy evaluation while wrapping relevant functionality from the upstream Dogwood Rust implementation.

This is a discovery and project foundation commission. Do not begin full production implementation until the architecture and implementation plan have been accepted by the owner.

### 2. Repositories and references

Target repository:

https://github.com/jenolaszlo-sketch/dogwood-sharp

Upstream Dogwood:

https://github.com/dogwood-policy/dogwood

Dogwood documentation:

https://dogwood-policy.github.io/dogwood/

Existing CedarSharp implementation:

https://github.com/jenolaszlo-sketch/cedar-sharp

Baize engineering and publishing reference:

https://github.com/jenolaszlo-sketch/penghou-baize

Additional architectural context:

https://nvidia.github.io/OpenShell-Research/dev-notes/posts/2026-11-09-cedar-policy-language/

Investigate the actual repositories, source code, documentation, tests, dependency manifests and GitHub workflows. Do not rely solely on summaries or assumptions.

Record the exact upstream revisions used in the investigation.

### 3. Project principles

DogwoodSharp shall:

- Be an independent, reusable .NET library.
- Not depend on Penghou, Hufu, Gagamba or other Penghou-specific infrastructure.
- Preserve upstream Dogwood language and evaluation semantics.
- Reuse existing libraries where appropriate.
- Avoid unnecessarily reimplementing Rust functionality in C#.
- Support .NET 8 and .NET 10.
- Be local-first and usable without AWS services.
- Target Apache 2.0 licensing with appropriate upstream attribution.
- Support cross-platform native packaging.
- Clearly distinguish reference-interpreter functionality from production security guarantees.

A future Penghou.Hufu.Dogwood adapter may consume DogwoodSharp, but that integration is outside this commission.

### 4. Upstream discovery

Investigate the following Dogwood capabilities:

- Policy parsing and formatting.
- Schema handling and validation.
- Policy lowering to Cedar.
- Temporal expression parsing and evaluation.
- Event schemas and event representation.
- Temporal operators, aggregation and macros.
- Information providers and guardrails.
- PolicyEngine and TemporalEngine extension points.
- Authorizer lifecycle and state management.
- Diagnostics and source locations.
- Trace replay.
- MCP tool schema generation.
- Existing conformance and regression test corpora.
- Experimental and optional features.
- Security limitations of the reference interpreter.

Determine which capabilities are stable, experimental, incomplete or explicitly unsuitable for production use.

Inspect the published Rust API and dependency graph, including Cargo.lock, rather than assuming that dependency declarations identify the resolved Cedar version.

Produce a capability inventory containing:

- Capability.
- Upstream implementation.
- Relevant public API.
- Dependencies.
- Stability.
- Security considerations.
- Proposed DogwoodSharp exposure.
- Proposed implementation milestone.

### 5. Architecture investigation

Evaluate at least three architectural approaches.

#### Option A: Full native Dogwood wrapper

Wrap the upstream Dogwood authorizer, including its existing Cedar policy engine and temporal evaluation.

Consider:

- Fidelity to upstream behavior.
- Implementation effort.
- Native dependency duplication.
- Maintainability.
- Diagnostics.
- Stateful native object ownership.
- Compatibility with CedarSharp.

#### Option B: DogwoodSharp built on CedarSharp

Use the upstream Dogwood implementation for parsing, lowering and temporal functionality, with CedarSharp performing the final Cedar authorization decision.

Investigate how to transfer:

- Lowered policies.
- Augmented Cedar schemas.
- Principal, action and resource identifiers.
- Entity stores.
- Temporal bindings.
- Computed information-provider values.
- Policy identifiers.
- Diagnostics and determining rule identities.

Determine whether the public upstream APIs expose sufficient information to reproduce the complete authorization semantics without copying internal implementation details.

#### Option C: Hybrid architecture

Provide an upstream-compatible native reference backend and a separate CedarSharp-backed execution path.

Determine whether this provides a useful reference implementation for differential qualification while allowing production-oriented backends to evolve independently.

#### Architecture decision

Compare the options against:

- Semantic fidelity.
- Security.
- Testability.
- Performance.
- Native packaging.
- Dependency duplication.
- API usability.
- Long-term maintainability.
- Extensibility.
- Implementation complexity.
- Upstream upgrade compatibility.

CedarSharp reuse is preferred, but it is not a predetermined requirement if evidence shows that it creates significant semantic or interoperability problems.

Do not change CedarSharp during this commission. Document any required CedarSharp extensions as separate proposed work items.

### 6. CedarSharp compatibility assessment

Inspect the published CedarSharp package, source code and native bridge.

Verify whether CedarSharp supports everything required to evaluate lowered Dogwood policies accurately.

Investigate:

- Resolved Cedar versions and enabled extensions.
- Cedar policy and schema serialization formats.
- Context representation.
- Entity representations and action hierarchies.
- Policy identifier preservation.
- Validation behavior.
- Authorization diagnostics.
- Default-deny and forbid precedence.
- Policy evaluation errors.
- Native ABI and memory ownership.
- Thread safety and concurrency.
- Native library loading and version discovery.
- NativeAOT and trimming.

Identify semantic mismatches, missing capabilities and possible version incompatibilities.

Perform a bounded feasibility spike if needed.

The spike may use temporary research code or dedicated non-production fixtures. It must not establish an unsupported production architecture.

At minimum, attempt to demonstrate:

1. A plain Cedar-compatible Dogwood policy.
2. A Dogwood policy containing a temporal condition.
3. Lowering to Cedar and obtaining the augmented schema.
4. Evaluation through CedarSharp using independently computed temporal context.
5. Comparison against the upstream Dogwood authorizer.
6. Matching decisions and, where available, determining policy identities and diagnostics.

Include negative controls and policies where the temporal condition changes the authorization decision.

### 7. Temporal architecture

Investigate how DogwoodSharp should expose stateful temporal functionality.

Consider:

- In-memory reference execution.
- Pluggable temporal evaluation backends.
- Durable event storage.
- Event identity and ordering.
- Trusted timestamps.
- Event authenticity.
- Replay and restart recovery.
- History retention and eviction.
- Tenant and session isolation.
- Multi-agent shared history.
- Concurrent authorization requests.
- Atomic spending and resource reservations.
- Cancellation and resource limits.

Clearly distinguish temporal policy evaluation from transactional enforcement.

For example, evaluating whether a shared spending limit has been exceeded does not, by itself, prevent two concurrent agents from exceeding that limit.

Do not attempt to implement a distributed coordination system as part of DogwoodSharp's initial release.

Identify which responsibilities belong in DogwoodSharp and which should belong to consuming applications or optional infrastructure packages.

### 8. Security requirements

Treat this as a security-sensitive authorization library.

Review upstream security considerations and establish explicit contracts for:

- Default-deny behavior.
- Validation and interpretation failures.
- Missing or malformed temporal context.
- Reserved context-field ownership.
- Untrusted caller-supplied events.
- Malformed or replayed events.
- Cross-session and cross-tenant isolation.
- Event timestamp integrity.
- Provider execution limits.
- Native memory safety.
- ABI compatibility.
- Diagnostic information leakage.
- Resource exhaustion.
- Unsupported language features.

Disable network-enabled information providers and experimental language features by default.

Do not allow callers to fabricate trusted temporal bindings through ordinary request context.

Preserve upstream semantics at the wrapper boundary while allowing consumers to apply a stricter fail-closed enforcement policy.

Do not describe the upstream reference interpreter as production-ready.

Produce an initial threat model and security limitations document.

### 9. Project initialization

Initialize the repository with an appropriate foundation, informed by the discovery findings.

Proposed structure:

    DogwoodSharp.slnx
    Directory.Build.props
    Directory.Packages.props
    global.json
    README.md
    LICENSE
    NOTICE
    SECURITY.md
    CONTRIBUTING.md
    .editorconfig
    .gitignore

    src/
      DogwoodSharp/

    native/
      dogwoodsharp-bridge/

    tests/
      DogwoodSharp.Tests/
      DogwoodSharp.Conformance.Tests/

    samples/

    eng/

    docs/
      architecture/
      research/
      implementation-plan.md
      roadmap.md
      verification.md

    .github/
      workflows/
        ci.yml
        publish.yml

This is an initial proposal, not a mandatory decomposition.

Avoid unnecessary packages and premature abstractions. Explain any significant structural deviations.

Configure:

- Nullable reference types.
- Centralized dependency versions.
- Deterministic builds.
- SourceLink.
- XML documentation.
- Analyzers.
- Package metadata.
- Symbol packages.
- Apache 2.0 license metadata.
- Repository metadata.
- NativeAOT and trimming compatibility where achievable.

Create a buildable project foundation and initial smoke tests without presenting placeholder functionality as an implemented policy engine.

### 10. Native interoperability

Use CedarSharp's native implementation as the primary engineering reference.

Investigate and design:

- A minimal, versioned C ABI.
- Rust cdylib output.
- Managed interop using LibraryImport or another justified mechanism.
- UTF-8 serialization boundaries.
- Native allocation and deallocation ownership.
- Panic containment across the ABI.
- SafeHandle or equivalent stateful handle management.
- Native handle lifetime and disposal.
- Concurrent access guarantees.
- ABI and upstream version discovery.
- Input and output size limits.
- Native error translation.
- Artifact integrity verification.

Prefer stable serialization boundaries over sharing Rust pointers or internal Cedar objects between independently built native libraries.

Pin the upstream Dogwood dependency and Rust toolchain.

Do not depend on an unpinned upstream main branch.

### 11. Continuous integration

Use Baize and CedarSharp as engineering references.

Baize:

- Build and formatting verification.
- Test execution.
- Line and branch coverage.
- Qualification tests.
- Independent package consumers.
- NuGet package and symbol artifacts.

CedarSharp:

- Native build and test matrix.
- Native artifact provenance.
- Manifest and checksum verification.
- Cross-platform native staging.
- NativeAOT smoke tests.
- Package-only consumer qualification.
- Exact package verification before publishing.

Plan and initialize GitHub Actions CI covering:

| Platform | Architecture | RID |
|----------|--------------|-----|
| Windows | x64 | win-x64 |
| Linux | x64 | linux-x64 |
| macOS | ARM64 | osx-arm64 |

Qualify .NET 8 and .NET 10 on each supported platform.

Include, where applicable:

- Rust formatting, linting and tests.
- Managed Release builds and formatting checks.
- Unit tests.
- Integration tests.
- Upstream differential conformance tests.
- Coverage reports.
- Native memory ownership tests.
- Dependency and vulnerability checks.
- Native artifact manifests.
- NuGet package verification.
- NativeAOT smoke execution.
- Fresh package-only consumer tests.

Use explicit runner and toolchain versions where appropriate.

Do not claim support for untested platforms or runtime combinations.

Additional RIDs may be considered in later milestones.

### 12. NuGet packaging and publishing

Use the package ID:

    DogwoodSharp

Check its availability and report any naming conflicts.

Follow Baize's NuGet trusted publishing approach and CedarSharp's verified native packaging model.

The publishing workflow should:

- Use GitHub OIDC and NuGet/login.
- Obtain a short-lived NuGet API credential.
- Avoid long-lived NuGet API keys.
- Publish only explicitly authorized releases.
- Require successful qualification.
- Verify the exact artifact being published.
- Include symbol packages.
- Verify all required native runtime assets.
- Include applicable license and notice files.
- Prevent unintended publication from pull requests or ordinary pushes.
- Prevent concurrent release publication.

Investigate whether manual dispatch or a release-tag workflow is preferable for DogwoodSharp.

Given the native packaging requirements, prefer publishing the exact package produced and qualified by CI rather than rebuilding an unverified artifact during publication.

Document the owner-side configuration required for NuGet trusted publishing.

Do not publish packages, create release tags, configure credentials or modify external publishing settings during this commission.

### 13. Qualification strategy

Establish a milestone-based qualification strategy.

Every implemented language capability should be demonstrably consistent with the corresponding upstream Dogwood behavior.

Include:

- Upstream example corpus execution.
- Upstream temporal regression corpus reuse.
- Differential testing against the native reference authorizer.
- Cedar-only equivalence tests against CedarSharp.
- Mixed Cedar and temporal policies.
- Temporal boundaries and history ordering.
- Provider failure cases.
- Invalid and adversarial inputs.
- Native ABI and lifecycle tests.
- Concurrent and multi-agent scenarios when applicable.
- Package-only consumer tests.
- Cross-platform verification.

Separate reference conformance from production security qualification.

Define explicit evidence requirements and release gates.

For major milestones, plan independent qualification followed by a separate owner acceptance decision.

### 14. Implementation plan and roadmap

Produce a dependency-ordered implementation plan.

The following milestone sequence is indicative and may be revised based on discovery:

- D0: Discovery, architecture selection and project foundation.
- D1: Native bridge and basic policy parsing.
- D2: Lowering, schemas and validation.
- D3: CedarSharp interoperability and Cedar-only equivalence.
- D4: Event model and reference temporal evaluation.
- D5: Temporal authorization and differential conformance.
- D6: Pluggable backends and information-provider support.
- D7: Security hardening and operational boundaries.
- D8: Cross-platform package qualification.
- D9: Preview release readiness.
- D10: Stable release readiness.

Identify durable temporal storage, production-grade temporal evaluation and Hufu integration as separate roadmap items unless the discovery establishes a compelling reason to include them earlier.

Each milestone must identify:

- Objective.
- Dependencies.
- Work packages.
- Public API changes.
- Implementation scope.
- Explicit exclusions.
- Security considerations.
- Required tests.
- Qualification matrix.
- Acceptance criteria.
- Expected evidence.
- Publication eligibility.

Use bounded, actionable work packages.

Avoid large speculative specifications where executable tests or formal contracts would be more effective.

### 15. Required deliverables

Produce the following documents or equivalent clearly identified artifacts:

1. Upstream discovery report.
2. Capability and API inventory.
3. Architecture options and recommendation.
4. CedarSharp compatibility assessment.
5. Feasibility spike evidence, if performed.
6. Architecture decision record.
7. Initial threat model.
8. Native interoperability design.
9. CI and NuGet publishing strategy.
10. Implementation plan.
11. Milestone-based roadmap.
12. Qualification strategy and acceptance matrix.
13. Initialized, buildable project foundation.

All material architectural claims must be traceable to upstream source code, documentation or reproducible experimental evidence.

Distinguish verified findings from assumptions and recommendations.

### 16. Repository discipline

Work in the DogwoodSharp repository.

Before making changes:

- Verify repository identity.
- Record the starting HEAD and tree.
- Confirm the current branch and worktree state.
- Inspect existing files and repository instructions.
- Preserve existing user changes.

Use main for the accepted project foundation. Avoid unnecessary branching and worktrees.

Do not modify CedarSharp, Baize, Hufu or any other repository.

Do not commit secrets, credentials, generated native binaries or temporary research artifacts.

Run all available foundation verification before committing.

Commit and push the completed discovery and project foundation to main, provided verification succeeds.

Do not force-push.

### 17. Final handoff

Return a structured report containing:

- Formal disposition.
- Repository identity and final revision.
- Verified upstream revisions.
- Key technical discoveries.
- Recommended architecture.
- Whether CedarSharp reuse is supported, conditionally supported or not recommended.
- Required CedarSharp changes, if any.
- Feasibility spike results.
- Project foundation summary.
- CI and publishing readiness.
- Security limitations.
- Verification results.
- Remaining risks and open decisions.
- Recommended next implementation commission.

Explicitly identify any conclusions that could not be established.

Stop after discovery, project initialization and planning.

Do not proceed into full production implementation without owner authorization.