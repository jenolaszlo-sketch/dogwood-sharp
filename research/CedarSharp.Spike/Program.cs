using System.Text.Json;
using CedarSharp;

using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
foreach (var item in document.RootElement.EnumerateArray())
{
    var policies = CedarPolicySet.FromPolicies(item.GetProperty("policies").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
    var request = new CedarAuthorizationRequest(new("Test::User", "u"), new("Test::Action", "Read"), new("Test::Item", "i"), policies,
        contextJson: item.GetProperty("context").GetRawText(), schema: CedarSchema.FromJsonElement(item.GetProperty("schema")));
    var result = new CedarEngine().Authorize(request);
    var reasons = item.GetProperty("reasons").EnumerateArray().Select(x => x.GetString()!).Order().ToArray();
    if (!result.IsErrorFree || (result.Decision == CedarDecision.Allow) != item.GetProperty("allow").GetBoolean() || !reasons.SequenceEqual(result.DeterminingPolicies.Order()))
        throw new InvalidOperationException($"Mismatch: {item.GetProperty("name")}: {result.Raw}");
    Console.WriteLine($"PASS {item.GetProperty("name")}: {result.Decision}; determining policies [{string.Join(",", reasons)}]; no errors");
}
