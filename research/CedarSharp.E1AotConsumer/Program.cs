using CedarSharp;
using DogwoodSharp;

// A D3-E1 research consumer. It lowers one Dogwood policy through the resident
// Dogwood bridge and evaluates the same policy through CedarSharp, in one
// process, and fails loudly if the two disagree. It is not a public API and
// must not graduate without its own review.

const string schemaText =
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

const string source =
    """permit(principal, action == App::Action::"read", resource) when { context.count > 1 };""";

var schema = new DogwoodSchema(schemaText, Distincter: "aot");
var lowered = Dogwood.Lower(source, schema);
if (!lowered.ArtifactsExported)
{
    Console.Error.WriteLine("The lowering did not export artifacts.");
    return 1;
}

var rule = lowered.Rules.Single();
var policies = CedarPolicySet.FromPolicies((rule.CedarPolicyId, lowered.CedarPolicyText));
var request = new CedarAuthorizationRequest(
    new CedarEntityUid("App::User", "alice"),
    new CedarEntityUid("App::Action", "read"),
    new CedarEntityUid("App::Doc", "d1"),
    policies,
    contextJson: """{"count":5}""",
    schema: CedarSchema.FromJson(lowered.CedarSchemaJson));

var result = new CedarEngine().Authorize(request);
if (!result.IsSuccess || result.Decision != CedarDecision.Allow)
{
    Console.Error.WriteLine($"Unexpected CedarSharp result: {result.Raw}");
    return 1;
}

Console.WriteLine($"lowered rule id = {rule.CedarPolicyId}");
Console.WriteLine($"cedarsharp decision = {result.Decision}");
Console.WriteLine($"determining policies = [{string.Join(", ", result.DeterminingPolicies)}]");
Console.WriteLine("AOT consumer OK");
return 0;
