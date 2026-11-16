using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace DogwoodSharp.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void AssemblyLoadsForTheExecutingTargetFramework()
    {
        var assembly = Assembly.Load("DogwoodSharp");
        Assert.Equal(typeof(FoundationTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName,
            assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
    }

    [Fact]
    public void DiscoveryAssemblyDoesNotExposeAnUnimplementedEngine()
    {
        var assembly = Assembly.Load("DogwoodSharp");
        Assert.Empty(assembly.GetExportedTypes());
        Assert.Contains(assembly.GetCustomAttributes<AssemblyMetadataAttribute>(),
            a => a.Key == "DevelopmentStage" && a.Value == "DiscoveryOnly");
    }
}
