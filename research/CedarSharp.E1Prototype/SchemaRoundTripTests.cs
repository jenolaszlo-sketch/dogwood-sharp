using CedarSharp;
using Xunit;
using Xunit.Abstractions;

namespace CedarSharp.E1Prototype;

/// <summary>
/// Schema and entity round trips: both exported schema forms must behave
/// identically, and the entity payload must survive the CedarSharp inputs.
/// </summary>
public sealed class SchemaRoundTripTests
{
    private readonly ITestOutputHelper output;

    public SchemaRoundTripTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void BothExportedSchemaForms_ParseAndValidateIdentically()
    {
        foreach (var testCase in E1Corpus.Cases)
        {
            var bundle = E1Bundle.FromDogwood(testCase.Source, testCase.Schema);
            var fromJson = E1Requests.Engine.CheckSchema(bundle.SchemaJson);
            var fromText = E1Requests.Engine.CheckSchema(bundle.SchemaText);
            Assert.True(fromJson.IsSuccess, $"{testCase.Name}: JSON schema: {fromJson}");
            Assert.True(fromText.IsSuccess, $"{testCase.Name}: text schema: {fromText}");

            var validatedJson = E1Requests.Engine.ValidatePolicies(bundle.PolicySet, bundle.SchemaJson);
            var validatedText = E1Requests.Engine.ValidatePolicies(bundle.PolicySet, bundle.SchemaText);
            output.WriteLine($"{testCase.Name}: valid(json)={validatedJson.IsValid} valid(text)={validatedText.IsValid} " +
                $"errors(json)={validatedJson.ValidationErrors.Count} errors(text)={validatedText.ValidationErrors.Count} " +
                $"warnings(json)={validatedJson.ValidationWarnings.Count} warnings(text)={validatedText.ValidationWarnings.Count}");

            Assert.Equal(validatedJson.IsValid, validatedText.IsValid);
            Assert.Equal(validatedJson.ValidationErrors.Count, validatedText.ValidationErrors.Count);
            Assert.Equal(validatedJson.ValidationWarnings.Count, validatedText.ValidationWarnings.Count);
        }
    }

    [Fact]
    public void SchemaForms_ParseOnThePinnedReferenceToo()
    {
        var bundle = E1Bundle.FromDogwood(
            "permit(principal, action == App::Action::\"read\", resource);",
            E1Corpus.Base);
        using var jsonDocument = Cedar11Reference.Call(
            Cedar11Reference.OperationCheckSchema,
            bundle.Lowered.CedarSchemaJson).Document;
        Assert.Equal("success", jsonDocument.RootElement.GetProperty("type").GetString());

        using var textDocument = Cedar11Reference.Call(
            Cedar11Reference.OperationCheckSchema,
            System.Text.Json.JsonSerializer.Serialize(bundle.Lowered.CedarSchemaText)).Document;
        Assert.Equal("success", textDocument.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void EntityPayload_RoundTripsThroughTheCheckOperations()
    {
        var bundle = E1Bundle.FromDogwood(
            "permit(principal, action == App::Action::\"read\", resource) when { resource.owner == principal };",
            E1Corpus.Base);
        var entities = E1Requests.Engine.CheckEntities(E1Corpus.EntitiesOwnerAlice, bundle.SchemaJson);
        Assert.True(entities.IsSuccess, entities.ToString());

        var withoutSchema = E1Requests.Engine.CheckEntities(E1Corpus.EntitiesOwnerAlice);
        Assert.True(withoutSchema.IsSuccess, withoutSchema.ToString());
    }
}
