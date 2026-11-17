using System.Text;
using DogwoodSharp;

namespace D1QualificationAot;

internal static class Program
{
    private const string Revision = "929f49e7305534ac61ef06cf8e47269c596dc851";

    private static int Main()
    {
        var target = Environment.GetEnvironmentVariable("D1Q_TARGET")
            ?? throw new InvalidOperationException("D1Q_TARGET is missing.");
        var version = Dogwood.GetNativeVersion();
        Ensure(version == new DogwoodNativeVersion(
            1, "0.1.0", Revision, "1.1.2", "4.11.0", "1.99.1", target),
            $"identity mismatch: {version}");

        var permit = Dogwood.Parse("permit(principal, action, resource);");
        Ensure(permit.Success && permit.PolicyCount == 1 && permit.Diagnostics.IsEmpty,
            "simple permit did not parse.");

        var empty = Dogwood.Parse(string.Empty);
        Ensure(empty.Success && empty.PolicyCount == 0, "empty source did not parse.");

        var failed = Dogwood.Parse("permit(");
        Ensure(!failed.Success && failed.PolicyCount is null && failed.Diagnostics.Length == 1 &&
            failed.Diagnostics[0].Category == PolicyDiagnosticCategory.Syntax &&
            failed.Diagnostics[0].Location == new PolicySourceLocation(7, 1),
            "malformed source diagnostic changed.");

        var maximum = "//" + new string('a', Dogwood.MaximumSourceBytes - 2);
        Ensure(Encoding.UTF8.GetByteCount(maximum) == Dogwood.MaximumSourceBytes,
            "maximum source is not exactly the limit.");
        var large = Dogwood.Parse(maximum);
        Ensure(large.Success && large.PolicyCount == 0, "maximum-size source did not parse.");

        try
        {
            _ = Dogwood.Parse(null!);
            throw new InvalidOperationException("null source was accepted.");
        }
        catch (ArgumentNullException)
        {
        }

        try
        {
            _ = Dogwood.Parse("\ud800");
            throw new InvalidOperationException("unpaired surrogate was accepted.");
        }
        catch (ArgumentException)
        {
        }

        Console.WriteLine("AOT SMOKE PASS");
        return 0;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
