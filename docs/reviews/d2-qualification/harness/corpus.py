"""Authored qualification inputs. Collision names come from upstream probes."""

TEMPORAL = 'when temporal { formerly within 30m Ledger::Action::"post"::request { input.amount: 11 } }'
POST = 'action == Ledger::Action::"post"'
DRAFT = 'action == Ledger::Action::"draft"'
GROUP = 'action in Ledger::Action::"work"'


def schema(post="{ input: { amount: Long }, word: String }",
           draft="{ input: { amount: Long }, word: String }", aliases="", prefix=""):
    return prefix + f"""
namespace Ledger {{
    {aliases}
    entity Clerk;
    entity Invoice;
    action "work";
    action "review" in ["work"];
    action "draft" in ["review"] appliesTo {{ principal: [Clerk], resource: [Invoice], context: {draft} }};
    action "post" in ["review"] appliesTo {{ principal: [Clerk], resource: [Invoice], context: {post} }};
}}
"""


def policy(scope, condition=TEMPORAL):
    return f"permit (principal, {scope}, resource) {condition};"


def record(declaration):
    return "{ input: { amount: Long }, word: String, " + declaration + " }"


def cases(derive):
    base = schema()
    alternate = "ledgerBatch"
    two = policy(POST, TEMPORAL + " " + TEMPORAL.replace(": 11", ": 12"))
    probes = [
        {"source": policy(POST), "policySchema": base},
        {"source": policy(POST), "policySchema": base, "distincter": alternate},
        {"source": two, "policySchema": base},
    ]
    regular, custom, multi = derive(probes)
    field = regular["fields"][0]["field"]
    field_custom = custom["fields"][0]["field"]
    first, second = [entry["field"] for entry in multi["fields"]]
    assert field == first and field != field_custom and first != second
    tests = []

    def add(name, expected, source, policy_schema=base, **extra):
        tests.append({"id": name, "expect": expected,
                      "request": dict(source=source, policySchema=policy_schema, **extra)})

    add("cedar-policy", "export", policy(POST, "when { context.input.amount >= 1 }"))
    add("cedar-empty-export", "export", "// independently authored empty set", "")
    add("namespace-and-repeated-id-annotations", "export",
        '@id("same") ' + policy(POST, "") +
        '\n@id("same") forbid (principal is Ledger::Clerk, action == Ledger::Action::"draft", resource);',
        distincter="tagged")
    add("ordered-twelve-rules", "export", "\n".join(
        f'@label("r{i}") ' + policy(POST, f"when {{ context.input.amount == {i} }}") for i in range(12)),
        distincter="order")
    add("macro-library-and-expansion", "export", policy(POST, "when { bigger(context.input.amount) }"),
        macros="def cedar bigger(?value) { ?value > 4 };")
    add("alternate-event-schema", "export",
        policy(POST, 'when temporal { formerly within 30m Ledger::Action::"post"::seen { input.amount: 11 } }'),
        eventSchema="decision event <A>::seen { ...inputs(A), actor: principalType(A), }")
    add("temporal-default", "export", policy(POST))
    add("temporal-custom", "export", policy(POST), distincter=alternate)
    add("cedar-warning-only", "warn", policy(POST, "when { false }"))
    add("temporal-unknown-event", "validation", policy(POST, TEMPORAL.replace("::request", "::unconfigured")))
    add("validation-utf8", "validation", '// 賬簿 🧾\n' + policy(POST, "when { context.unlisted == 1 }"),
        distincter=alternate)
    add("validation-utf8-bom", "validation", '\ufeff// 賬簿 🧾\n' + policy(POST, "when { context.unlisted == 1 }"),
        distincter=alternate)
    add("parse-utf8-bom", "parse", "\ufeff// 賬簿 🧾\npermit (")
    add("invalid-event-schema", "service-schema", policy(POST), eventSchema="event <")
    add("action-schema-nesting", "action-schema", policy(POST), "{" * 128)
    add("action-schema-syntax", "lower", policy(POST), "namespace Ledger { entity")
    add("invalid-distincter", "lower", policy(POST), distincter="two-part")
    add("unresolvable-context", "lower", policy(POST), schema("MissingType"))
    add("cyclic-context-alias", "lower", policy(POST), schema("First", aliases="type First = Last; type Last = First;"))
    add("original-overwrite", "collision", policy(POST), schema(record(f"{field}: String")))
    add("equal-required-type", "collision", policy(POST), schema(record(f"{field}: Bool")))
    add("equal-optional-type", "collision", policy(POST), schema(record(f"{field}?: Bool")))
    add("shared-alias-chain", "collision", policy(POST),
        schema("Third", "Third", f"type First = {record(f'{field}: Bool')}; type Second = First; type Third = Second;"))
    add("cross-namespace-alias", "collision", policy(POST),
        schema("Archive::Alias", prefix=f"namespace Archive {{ type Stored = {record(f'{field}?: String')}; type Alias = Stored; }}"))
    add("transitive-group", "collision", policy(GROUP), schema(record(f"{field}: Long")))
    add("unconstrained-target", "collision", policy("action"), schema(draft=record(f"{field}: Long")))
    add("multiple-affected-actions", "collision", policy(GROUP), schema(record(f"{field}: Long"), record(f"{field}: String")))
    add("second-generated-field", "collision", two, schema(record(f"{second}: Bool")))
    add("first-of-many-conflicts", "collision", two, schema(record(f"{second}: Bool, {first}: String")))
    add("custom-distincter-collision", "collision", policy(POST), schema(record(f"{field_custom}: Bool")), distincter=alternate)
    add("custom-distincter-unrelated-default", "export", policy(POST), schema(record(f"{field}: Bool")), distincter=alternate)
    add("unaffected-action", "export", policy(POST), schema(draft=record(f"{field}: Bool")))
    add("nested-lookalike", "export", policy(POST), schema("{ input: { amount: Long, " + field + ": Bool } }"))
    add("case-sensitive-lookalike", "export", policy(POST), schema(record(f"{field.upper()}: Bool")))
    add("list-scope", "collision", policy('action in [Ledger::Action::"draft", Ledger::Action::"post"]'),
        schema(draft=record(f"{field}: Bool")))
    return tests
