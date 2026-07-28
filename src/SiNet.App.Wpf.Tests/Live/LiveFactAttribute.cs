using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Fact that runs only when <c>SINET_LIVE_SMOKE=1</c>. Otherwise skipped so CI stays green.
/// Apply <c>[Trait("Category", LiveFactAttribute.Category)]</c> on the test class.
/// See <c>docs/TEST_STRATEGY.md</c> L4.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveFactAttribute : FactAttribute
{
    public const string EnvVarName = "SINET_LIVE_SMOKE";
    public const string Category = "LiveSmoke";

    public LiveFactAttribute()
    {
        if (!IsLiveEnabled())
        {
            Skip = $"Set {EnvVarName}=1 to run live smoke (see docs/TEST_STRATEGY.md).";
        }
    }

    public static bool IsLiveEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnvVarName);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
