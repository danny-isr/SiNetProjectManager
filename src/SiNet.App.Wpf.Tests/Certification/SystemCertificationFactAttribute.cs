using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Fact for the Full System Workflow Certification tier — a separate, stricter tier than
/// <c>Category=PilotSmoke</c>, which is retained unchanged as the fast smoke.
/// <para>
/// Skips only when the tier is not switched on, so CI and the offline suite are unaffected. When the tier
/// <i>is</i> switched on but the target cannot be proven to be the approved DEV environment, the test
/// deliberately still runs and fails on the guard: a skip would be indistinguishable from a clean run,
/// which is exactly the "green while nothing happened" failure mode this tier exists to eliminate.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SystemCertificationFactAttribute : FactAttribute
{
    public const string Category = "SystemCertification";

    public SystemCertificationFactAttribute()
    {
        var target = SystemCertificationEnvironment.TryResolveTarget();
        if (!target.IsEnabled)
        {
            Skip = target.SkipReason;
        }
    }
}
