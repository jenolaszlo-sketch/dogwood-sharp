using DogwoodSharp;

namespace CedarSharp.E1Prototype;

/// <summary>One differential corpus case with its independently derived expectations.</summary>
/// <remarks>
/// Expectations are derived from the authored policy semantics by hand, not
/// from either engine. Cases with <c>ExpectedDecision == null</c> are evidence
/// records: the two engines must agree with each other, and the observed
/// result is reported, but no hand expectation is claimed.
/// </remarks>
public sealed record E1Case(
    string Name,
    DogwoodSchema Schema,
    string Source,
    string ContextJson,
    string EntitiesJson,
    (string Type, string Id) Principal,
    (string Type, string Id) Action,
    (string Type, string Id) Resource,
    bool ValidateRequest,
    string? ExpectedDecision,
    int[]? ExpectedReasonRuleIndices,
    int[]? ExpectedErrorRuleIndices,
    string Purpose)
{
    /// <summary>
    /// Whether the request carries the augmented schema. The schema is not
    /// optional decoration: Cedar reads the action hierarchy from the schema,
    /// so a request without it cannot resolve <c>action in [Group]</c>.
    /// </summary>
    public bool PassSchema { get; init; } = true;

    /// <summary>
    /// Whether the schema's action-hierarchy entities are merged into the
    /// entity payload. Evidence shows this is redundant when the schema is
    /// present; the case exists to prove the redundancy.
    /// </summary>
    public bool MergeActionEntities { get; init; }

    public static E1Case Evidence(
        string name, DogwoodSchema schema, string source, string contextJson, string entitiesJson,
        (string Type, string Id) principal, (string Type, string Id) action, (string Type, string Id) resource,
        bool passSchema = true, bool mergeActionEntities = false, string purpose = "") =>
        new(name, schema, source, contextJson, entitiesJson, principal, action, resource,
            ValidateRequest: true, ExpectedDecision: null,
            ExpectedReasonRuleIndices: null, ExpectedErrorRuleIndices: null, Purpose: purpose)
        {
            PassSchema = passSchema,
            MergeActionEntities = mergeActionEntities,
        };
}

/// <summary>The E1 differential corpus: schemas, shared fixtures, and the case list.</summary>
public static class E1Corpus
{
    public const string Distincter = "e1";

    /// <summary>The shared schema. Focused schemas cover hierarchy, records, extensions, and required attributes.</summary>
    public const string BaseSchema =
        """
        namespace App {
          entity User in Group;
          entity Group;
          entity Doc {
            owner: User,
            secret?: Bool,
          };
          action read appliesTo {
            principal: [User],
            resource: [Doc],
          };
          action write appliesTo {
            principal: [User],
            resource: [Doc],
          };
        }
        """;

    public const string HierarchySchema =
        """
        namespace App {
          entity User;
          entity Doc;
          action ReadGroup;
          action read2 in ReadGroup appliesTo {
            principal: [User],
            resource: [Doc],
          };
        }
        """;

    public const string ContextRecordSchema =
        """
        namespace App {
          entity User;
          entity Doc;
          action read appliesTo {
            principal: [User],
            resource: [Doc],
            context: {
              count: Long,
              meta: {
                level: Long,
              },
            },
          };
        }
        """;

    public const string ExtensionSchema =
        """
        namespace App {
          entity User;
          entity Doc;
          action write appliesTo {
            principal: [User],
            resource: [Doc],
            context: {
              ip: ipaddr,
              amount: decimal,
              at: datetime,
            },
          };
        }
        """;

    public const string CountSchema =
        """
        namespace App {
          entity User;
          entity Doc;
          action read appliesTo {
            principal: [User],
            resource: [Doc],
            context: {
              count: Long,
            },
          };
        }
        """;

    /// <summary>A required attribute the bare scope entity cannot satisfy.</summary>
    public const string RequiredAttrSchema =
        """
        namespace App {
          entity Session {
            token: String,
          };
          entity Doc;
          action read appliesTo {
            principal: [Session],
            resource: [Doc],
          };
        }
        """;

    public static DogwoodSchema Base { get; } = new(BaseSchema, Distincter: Distincter);
    public static DogwoodSchema Hierarchy { get; } = new(HierarchySchema, Distincter: Distincter);
    public static DogwoodSchema ContextRecord { get; } = new(ContextRecordSchema, Distincter: Distincter);
    public static DogwoodSchema Extensions { get; } = new(ExtensionSchema, Distincter: Distincter);
    public static DogwoodSchema Count { get; } = new(CountSchema, Distincter: Distincter);
    public static DogwoodSchema RequiredAttr { get; } = new(RequiredAttrSchema, Distincter: Distincter);
    public static DogwoodSchema DefaultDistincter { get; } = new(BaseSchema);

    public const string DocOne = "App::Doc::\"d1\"";
    public const string ActionRead = "App::Action::\"read\"";

    public const string EntitiesOwnerAlice =
        """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}}},"parents":[]}]""";

    public const string EntitiesOwnerBob =
        """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"bob"}}},"parents":[]}]""";

    public const string EntitiesSecretTrue =
        """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}},"secret":true},"parents":[]}]""";

    /// <summary>Doc d1 without the optional secret attribute.</summary>
    public const string EntitiesNoSecret =
        """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}}},"parents":[]}]""";

    public const string EntitiesEscaped =
        """[{"uid":{"type":"App::User","id":"a\"b\\c"},"attrs":{},"parents":[]},{"uid":{"type":"App::User","id":"snow☃man"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}}},"parents":[]}]""";

    public const string EntitiesGroups =
        """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[{"type":"App::Group","id":"admins"}]},{"uid":{"type":"App::Group","id":"admins"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}}},"parents":[]}]""";

    public const string EntitiesNoGroups =
        """[{"uid":{"type":"App::User","id":"alice"},"attrs":{},"parents":[]},{"uid":{"type":"App::Group","id":"admins"},"attrs":{},"parents":[]},{"uid":{"type":"App::Doc","id":"d1"},"attrs":{"owner":{"__entity":{"type":"App::User","id":"alice"}}},"parents":[]}]""";

    public const string EntitiesIncompleteSession =
        """[{"uid":{"type":"App::Session","id":"s1"},"attrs":{},"parents":[]}]""";

    public const string EmptyEntities = "[]";

    public static IReadOnlyList<E1Case> Cases { get; } =
    [
        new E1Case(
            "permit-direct", Base, "permit(principal, action == App::Action::\"read\", resource);", "{}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [0], [], "Permit with no condition."),

        new E1Case(
            "default-deny", Base, "permit(principal, action == App::Action::\"write\", resource);", "{}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Deny", [], [], "No policy matches: Cedar default deny."),

        new E1Case(
            "matching-forbid", Base, "forbid(principal, action == App::Action::\"read\", resource);", "{}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Deny", [0], [], "A matching forbid denies."),

        new E1Case(
            "permit-forbid", Base,
            "permit(principal, action == App::Action::\"read\", resource); forbid(principal, action == App::Action::\"read\", resource);",
            "{}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Deny", [1], [], "Forbid precedence over a satisfied permit."),

        new E1Case(
            "multiple-permits", Count,
            "permit(principal, action == App::Action::\"read\", resource) when { context.count > 1 }; permit(principal, action == App::Action::\"read\", resource) when { context.count > 2 };",
            "{\"count\":5}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [0, 1], [], "Two distinct permits both determine one Allow."),

        new E1Case(
            "distinct-ids-12", Count,
            string.Join(" ", Enumerable.Range(0, 12).Select(count =>
                $"permit(principal, action == App::Action::\"read\", resource) when {{ context.count > {count} }};")),
            "{\"count\":5}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [0, 1, 2, 3, 4], [], "Twelve rules with distinct IDs; the satisfied set is count>0..count>4."),

        new E1Case(
            "namespaced-entity-match", Base,
            "permit(principal, action == App::Action::\"read\", resource) when { resource.owner == principal };",
            "{}", EntitiesOwnerAlice,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [0], [], "Entity attribute reference across the namespace."),

        new E1Case(
            "namespaced-entity-mismatch", Base,
            "permit(principal, action == App::Action::\"read\", resource) when { resource.owner == principal };",
            "{}", EntitiesOwnerBob,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Deny", [], [], "Owner comparison fails cleanly."),

        new E1Case(
            "escaped-ids", Base,
            """permit(principal == App::User::"a\"b\\c", action == App::Action::"read", resource); permit(principal == App::User::"snow\u{2603}man", action == App::Action::"read", resource);""",
            "{}", EntitiesEscaped,
            ("App::User", "snow☃man"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [1], [], "Escaped quote, backslash, and Unicode escape in scope UIDs."),

        new E1Case(
            "action-hierarchy-with-schema", Hierarchy,
            "permit(principal, action in App::Action::\"ReadGroup\", resource);",
            "{}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read2"), ("App::Doc", "d1"), true,
            "Allow", [0], [],
            "Action group membership with the augmented schema attached and no action entities in the store: Cedar adds the schema's action entities and their ancestor edges, so the membership resolves."),

        new E1Case(
            "action-hierarchy-without-schema", Hierarchy,
            "permit(principal, action in App::Action::\"ReadGroup\", resource);",
            "{}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read2"), ("App::Doc", "d1"), false,
            "Deny", [], [],
            "The same policy without the schema: Cedar has no action entities, so the membership is false and the rule never fires. This is the adapter requirement — the schema must travel with the request.")
        {
            PassSchema = false,
        },

        new E1Case(
            "action-hierarchy-with-merged-entities", Hierarchy,
            "permit(principal, action in App::Action::\"ReadGroup\", resource);",
            "{}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read2"), ("App::Doc", "d1"), true,
            "Allow", [0], [],
            "The schema's action-hierarchy entities merged into the entity payload as well: the decision is unchanged, because Cedar adds the same action entities from the schema anyway.")
        {
            MergeActionEntities = true,
        },

        new E1Case(
            "entity-parents-match", Base,
            "permit(principal, action == App::Action::\"read\", resource) when { principal in App::Group::\"admins\" };",
            "{}", EntitiesGroups,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [0], [], "Hierarchy membership through a parent edge."),

        new E1Case(
            "entity-parents-miss", Base,
            "permit(principal, action == App::Action::\"read\", resource) when { principal in App::Group::\"admins\" };",
            "{}", EntitiesNoGroups,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Deny", [], [], "No parent edge: the membership test is false, not an error."),

        new E1Case(
            "optional-secret-forbid", Base,
            "forbid(principal, action == App::Action::\"read\", resource) when { resource has secret && resource.secret == true }; permit(principal, action == App::Action::\"read\", resource);",
            "{}", EntitiesSecretTrue,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Deny", [0], [], "Optional attribute present and true: forbid determines."),

        new E1Case(
            "optional-secret-absent", Base,
            "forbid(principal, action == App::Action::\"read\", resource) when { resource has secret && resource.secret == true }; permit(principal, action == App::Action::\"read\", resource);",
            "{}", EntitiesNoSecret,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [1], [], "Optional attribute absent: a has-guarded forbid is false, the permit is clean."),

        new E1Case(
            "forbid-error-skipped", RequiredAttr,
            "forbid(principal, action == App::Action::\"read\", resource) when { principal.token == \"t\" }; permit(principal, action == App::Action::\"read\", resource);",
            "{}", EmptyEntities,
            ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [1], [0],
            "Skip-on-error: a forbid that errors stops applying, so the permit decides. The raw Allow carries the forbid's policy error, so it is not a clean Allow."),

        new E1Case(
            "missing-attribute-error", RequiredAttr,
            "permit(principal, action == App::Action::\"read\", resource) when { principal.token == \"t\" };",
            "{}", EmptyEntities,
            ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Deny", [], [0],
            "A required attribute of a scope entity that is absent from the store: the policy errors and the decision is a fail-closed Deny that still reports the erroring rule."),

        new E1Case(
            "allow-with-policy-errors", RequiredAttr,
            "permit(principal, action == App::Action::\"read\", resource); permit(principal, action == App::Action::\"read\", resource) when { principal.token == \"t\" };",
            "{}", EmptyEntities,
            ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [0], [1],
            "A raw Allow that carries a policy evaluation error: the decision is Allow, but it is not a clean Allow."),

        new E1Case(
            "context-record", ContextRecord,
            "permit(principal, action == App::Action::\"read\", resource) when { context.meta.level > 2 };",
            "{\"count\":1,\"meta\":{\"level\":5}}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Allow", [0], [], "Nested record attribute in context."),

        new E1Case(
            "context-record-false", ContextRecord,
            "permit(principal, action == App::Action::\"read\", resource) when { context.meta.level > 2 };",
            "{\"count\":1,\"meta\":{\"level\":1}}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            "Deny", [], [], "Record comparison false: clean deny."),

        new E1Case(
            "extension-values", Extensions,
            "permit(principal, action == App::Action::\"write\", resource) when { context.ip.isLoopback() && context.amount.greaterThan(decimal(\"10.00\")) && context.at < datetime(\"2027-01-01T00:00:00Z\") };",
            "{\"ip\":{\"__extn\":{\"fn\":\"ip\",\"arg\":\"127.0.0.1\"}},\"amount\":{\"__extn\":{\"fn\":\"decimal\",\"arg\":\"25.50\"}},\"at\":{\"__extn\":{\"fn\":\"datetime\",\"arg\":\"2026-06-01T00:00:00Z\"}}}",
            EmptyEntities,
            ("App::User", "alice"), ("App::Action", "write"), ("App::Doc", "d1"), true,
            "Allow", [0], [], "ipaddr, decimal, and datetime extension values and operators."),

        new E1Case(
            "extension-values-false", Extensions,
            "permit(principal, action == App::Action::\"write\", resource) when { context.ip.isLoopback() && context.amount.greaterThan(decimal(\"10.00\")) };",
            "{\"ip\":{\"__extn\":{\"fn\":\"ip\",\"arg\":\"10.0.0.1\"}},\"amount\":{\"__extn\":{\"fn\":\"decimal\",\"arg\":\"25.50\"}},\"at\":{\"__extn\":{\"fn\":\"datetime\",\"arg\":\"2026-06-01T00:00:00Z\"}}}",
            EmptyEntities,
            ("App::User", "alice"), ("App::Action", "write"), ("App::Doc", "d1"), true,
            "Deny", [], [], "Non-loopback address: extension method false."),

        new E1Case(
            "validate-request-true-rejects", Count,
            "permit(principal, action == App::Action::\"read\", resource);",
            "{\"count\":5}", EmptyEntities,
            ("App::Doc", "d1"), ("App::Action", "read"), ("App::Doc", "d1"), true,
            null, null, null,
            "Request validation on, with a principal whose type the action does not apply to: the observed failure mode is recorded."),

        new E1Case(
            "validate-request-false-evaluates", Count,
            "permit(principal, action == App::Action::\"read\", resource);",
            "{\"count\":5}", EmptyEntities,
            ("App::Doc", "d1"), ("App::Action", "read"), ("App::Doc", "d1"), false,
            "Allow", [0], [],
            "The same request with request validation off evaluates; the flag difference must survive both engines."),

        new E1Case(
            "context-missing-required-attribute-fails", Count,
            "permit(principal, action == App::Action::\"read\", resource);",
            "{}", EmptyEntities,
            ("App::User", "alice"), ("App::Action", "read"), ("App::Doc", "d1"), false,
            null, null, null,
            "A missing required context attribute fails even with request validation off: the schema-based context parse is not a request-validation step, so the flag does not bypass it."),

        E1Case.Evidence(
            "bare-scope-entity-match", RequiredAttr,
            "permit(principal, action == App::Action::\"read\", resource) when { principal == App::Session::\"s1\" };",
            "{}", EmptyEntities,
            ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"),
            purpose: "Bare scope entity (absent from the entity store) compared by identity: Cedar compares entity references without requiring presence, so the comparison succeeds. The observed mode defines the adapter requirement."),

        E1Case.Evidence(
            "bare-scope-required-attr-read", RequiredAttr,
            "permit(principal, action == App::Action::\"read\", resource) when { principal.token == \"t\" };",
            "{}", EmptyEntities,
            ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"),
            purpose: "Bare scope entity with a required-attribute read: the uid has no attributes, so the policy errors and the decision fails closed."),

        E1Case.Evidence(
            "supplied-incomplete-entity", RequiredAttr,
            "permit(principal, action == App::Action::\"read\", resource) when { principal == App::Session::\"s1\" };",
            "{}", EntitiesIncompleteSession,
            ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"),
            purpose: "A caller-supplied entity that lacks a schema-required attribute: upstream rejects it during conformance (fail-closed deny); the schema-carrying CedarSharp call conformance-fails the entities instead."),

        new E1Case(
            "scope-entity-present-missing-attribute", RequiredAttr,
            "permit(principal, action == App::Action::\"read\", resource) when { principal.token == \"t\" };",
            "{}", EntitiesIncompleteSession,
            ("App::Session", "s1"), ("App::Action", "read"), ("App::Doc", "d1"), false,
            null, null, null,
            "The scope entity is present in the store but has no attributes, and no schema is attached so no conformance check runs: the attribute read fails with a different diagnostic than the absent-entity case. This isolates the upstream bare-scope-entity difference.")
        {
            PassSchema = false,
        },
    ];
}
