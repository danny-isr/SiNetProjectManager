using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Fact for the L4W <b>write</b> smoke tier (<c>docs/TEST_STRATEGY.md</c> §4W). Runs only when the
/// full fail-closed gate set from <see cref="PilotSmokeEnvironment"/> is present; otherwise it is
/// skipped with the reason, so CI and the read-only <see cref="LiveFactAttribute"/> tier are
/// unaffected.
/// <para>
/// Deliberately a separate <see cref="Category"/> from <see cref="LiveFactAttribute.Category"/>:
/// <c>Category=LiveSmoke</c> must never be able to trigger writes.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PilotSmokeFactAttribute : FactAttribute
{
    public const string Category = "PilotSmoke";

    public PilotSmokeFactAttribute()
    {
        var gate = PilotSmokeEnvironment.TryResolveSqlTier();
        if (!gate.IsEnabled)
        {
            Skip = gate.SkipReason;
        }
    }
}
