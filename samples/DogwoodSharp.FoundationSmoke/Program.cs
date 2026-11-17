using DogwoodSharp;

var version = Dogwood.GetNativeVersion();
var parsed = Dogwood.Parse("permit(principal, action, resource);");
var failed = Dogwood.Parse("permit(");
if (version.AbiVersion != 1 || !parsed.Success || parsed.PolicyCount != 1 ||
    failed.Success || failed.PolicyCount.HasValue || failed.Diagnostics.IsEmpty)
{
    throw new InvalidOperationException("DogwoodSharp parse smoke check failed.");
}

Console.WriteLine($"DogwoodSharp bridge {version.BridgeVersion}, Dogwood {version.DogwoodVersion}, Cedar {version.CedarVersion}, {version.Target}: passed.");
