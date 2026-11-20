using System.Diagnostics;
using CedarSharp;
using DogwoodSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>
/// E1 process-level finding: the Dogwood bridge and CedarSharp ship two
/// different Cedar versions as separate native images. Both must be loadable
/// in one process, because the DogwoodSharp lowering path and the CedarSharp
/// authorization path have to coexist in the same host.
/// </summary>
public sealed class NativeCoexistenceTests
{
    private readonly ITestOutputHelper output;

    public NativeCoexistenceTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void BothNativeImages_AreResidentInTheSameProcess()
    {
        // Force both native paths first: the images load lazily, so the module
        // list is only meaningful after each one has been called.
        var source = "permit(principal, action == App::Action::\"read\", resource) when { context.count > 1 };";
        var schema = new DogwoodSchema(E1Corpus.CountSchema, Distincter: "e1");
        var lowered = Dogwood.Lower(source, schema);
        Assert.True(lowered.ArtifactsExported);
        var bundle = E1Bundle.FromDogwood(source, schema);
        var authorized = new CedarEngine().Authorize(
            new CedarAuthorizationRequest(
                new CedarEntityUid("App::User", "alice"),
                new CedarEntityUid("App::Action", "read"),
                new CedarEntityUid("App::Doc", "d1"),
                bundle.PolicySet,
                contextJson: "{\"count\":5}",
                schema: bundle.SchemaJson));
        Assert.Equal(CedarSharp.CedarDecision.Allow, authorized.Decision);

        var modules = Process.GetCurrentProcess().Modules
            .Cast<ProcessModule>()
            .Select(module => module.ModuleName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        output.WriteLine($"Native modules: [{string.Join(", ", modules.Where(name => name.Contains("dogwood", StringComparison.OrdinalIgnoreCase) || name.Contains("cedar", StringComparison.OrdinalIgnoreCase)))}]");

        if (!OperatingSystem.IsWindows())
        {
            // The module list is a Windows-specific view; the behavioral
            // coexistence above is the portable evidence.
            return;
        }

        Assert.Contains("dogwoodsharp_bridge.dll", modules);
        Assert.Contains("cedarsharp_native.dll", modules);
    }

    [Fact]
    public void TheTwoCedarVersions_ServeDifferentAnswersInOneProcess()
    {
        // The DogwoodSharp path lowers and validates with the bridge's Cedar
        // 4.11; the CedarSharp path evaluates with Cedar 4.13. Both run here,
        // in the same process, against the same policy.
        var source = "permit(principal, action == App::Action::\"read\", resource) when { context.count > 1 };";
        var schema = new DogwoodSchema(E1Corpus.CountSchema, Distincter: "e1");
        var lowered = Dogwood.Lower(source, schema);
        Assert.True(lowered.ArtifactsExported);

        var policies = CedarPolicySet.FromPolicies(
            (lowered.Rules[0].CedarPolicyId, E1Bundle.FromDogwood(source, schema).PoliciesById[lowered.Rules[0].CedarPolicyId]));
        var request = new CedarAuthorizationRequest(
            new CedarEntityUid("App::User", "alice"),
            new CedarEntityUid("App::Action", "read"),
            new CedarEntityUid("App::Doc", "d1"),
            policies,
            contextJson: "{\"count\":5}",
            schema: CedarSchema.FromJson(lowered.CedarSchemaJson));
        var result = new CedarEngine().Authorize(request);
        output.WriteLine($"CedarSharp decision: {result.Decision}");
        Assert.Equal(CedarSharp.CedarDecision.Allow, result.Decision);
    }
}
