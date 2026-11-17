using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace DogwoodSharp.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void AssemblyLoadsForTheExecutingTargetFramework()
    {
        var assembly = typeof(Dogwood).Assembly;
        Assert.Equal(typeof(FoundationTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName,
            assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
    }
}
