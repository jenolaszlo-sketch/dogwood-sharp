using DogwoodSharp;

var version = Dogwood.GetNativeVersion();
var parsed = Dogwood.Parse("permit(principal, action, resource);");
var failed = Dogwood.Parse("permit(");
if (version.AbiVersion != 1 || !parsed.Success || parsed.PolicyCount != 1 ||
    failed.Success || failed.PolicyCount.HasValue || failed.Diagnostics.IsEmpty)
{
    throw new InvalidOperationException("DogwoodSharp parse smoke check failed.");
}

// D2 lowering and validation. A validated policy and a lowered bundle must both
// be distinguishable from a validation failure, and a failed lowering must not
// report artifacts.
var schema = new DogwoodSchema("namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { count: Long } }; }");
var validated = Dogwood.Validate(
    "permit (principal, action == App::Action::\"read\", resource) when { context.count > 3 };", schema);
var lowered = Dogwood.Lower(
    "permit (principal, action == App::Action::\"read\", resource) when { context.count > 3 };", schema);
var rejected = Dogwood.Lower(
    "permit (principal, action == App::Action::\"read\", resource) when { context.nope > 3 };", schema with { Distincter = "smoke" });
var unusable = Dogwood.Lower(
    "permit (principal, action == App::Action::\"read\", resource);",
    new DogwoodSchema("namespace App { entity"));
var temporalSchema = new DogwoodSchema(
    "namespace App { entity User; entity Doc; action \"read\" appliesTo { principal: [User], resource: [Doc], context: { input: { count: Long } } }; }",
    Distincter: "smoke");
const string temporal = """
    permit (principal, action == App::Action::"read", resource)
    when temporal { formerly within 1h App::Action::"read"::request { input.count: 3 } };
    """;
var validTemporal = Dogwood.Lower(temporal, temporalSchema);
var collision = Dogwood.Lower(temporal, temporalSchema with
{
    PolicySchema = temporalSchema.PolicySchema.Replace("count: Long } }",
        "count: Long }, smoke_0__temporal_0: Bool }", StringComparison.Ordinal)
});
if (!validated.IsValid || !lowered.IsLowered || !rejected.Parsed || !rejected.Lowered ||
    rejected.Validation.IsValid || rejected.Validation.ValidationErrors.IsEmpty ||
    rejected.ArtifactsExported || rejected.CedarPolicyText.Length != 0 || rejected.CedarSchemaJson.Length != 0 ||
    !validTemporal.ArtifactsExported || validTemporal.SelfContainedCedar ||
    collision.ArtifactsExported || collision.Validation.Collision?.Field != "smoke_0__temporal_0" ||
    collision.CedarSchemaJson.Length != 0 ||
    unusable.IsLowered || unusable.StageError is null || !string.IsNullOrEmpty(unusable.CedarPolicyText) ||
    lowered.Rules.IsEmpty || lowered.Rules[0].RuleIndex != 0 || string.IsNullOrEmpty(lowered.CedarPolicyText) ||
    string.IsNullOrEmpty(lowered.CedarSchemaJson) || validated.RuleCount != 1)
{
    throw new InvalidOperationException("DogwoodSharp D2 lowering or validation smoke check failed.");
}

Console.WriteLine($"DogwoodSharp bridge {version.BridgeVersion}, Dogwood {version.DogwoodVersion}, Cedar {version.CedarVersion}, {version.Target}: passed.");
