using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Offline proof that only an unrequested tier skips. Once <see cref="SystemCertificationEnvironment.EnabledEnv"/>
/// is set, every configuration gap is a violation and the test must run and fail.
/// </summary>
public sealed class SystemCertificationEnvironmentTests : IDisposable
{
    private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

    [Fact]
    public void WhenTierIsNotRequestedThenTargetIsNotEnabled()
    {
        Clear(SystemCertificationEnvironment.EnabledEnv);

        var target = SystemCertificationEnvironment.TryResolveTarget();

        Assert.False(target.IsEnabled);
        Assert.NotNull(target.SkipReason);
        Assert.Null(target.Violation);
    }

    [Fact]
    public void WhenTierIsRequestedButSqlConnectionIsMissingThenItIsAViolation()
    {
        Set(SystemCertificationEnvironment.EnabledEnv, "1");
        Clear(SystemCertificationEnvironment.SqlConnectionEnv);

        var target = SystemCertificationEnvironment.TryResolveTarget();

        Assert.True(target.IsEnabled);
        Assert.NotNull(target.Violation);
        Assert.Contains(SystemCertificationEnvironment.SqlConnectionEnv, target.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenTierIsRequestedButConnectionStringIsInvalidThenItIsAViolation()
    {
        Set(SystemCertificationEnvironment.EnabledEnv, "1");
        Set(SystemCertificationEnvironment.SqlConnectionEnv, "not-a-connection-string");

        var target = SystemCertificationEnvironment.TryResolveTarget();

        Assert.True(target.IsEnabled);
        Assert.NotNull(target.Violation);
    }

    [Fact]
    public void WhenGmailLayerIsRequestedButAccountIsMissingThenItIsAViolation()
    {
        Set(SystemCertificationEnvironment.GmailEnabledEnv, "1");
        Clear(SystemCertificationEnvironment.GmailAccountEnv);

        var gmail = SystemCertificationEnvironment.TryResolveGmailLayer();

        Assert.True(gmail.IsEnabled);
        Assert.NotNull(gmail.Violation);
        Assert.Null(gmail.SkipReason);
    }

    [Fact]
    public void WhenAccLayerIsRequestedWithoutGmailThenItIsAViolation()
    {
        Set(SystemCertificationEnvironment.AccEnabledEnv, "1");
        Clear(SystemCertificationEnvironment.GmailEnabledEnv);

        var gmail = SystemCertificationEnvironment.TryResolveGmailLayer();
        var acc = SystemCertificationEnvironment.TryResolveAccLayer(gmail);

        Assert.True(acc.IsEnabled);
        Assert.NotNull(acc.Violation);
    }

    private void Set(string name, string value)
    {
        Remember(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void Clear(string name)
    {
        Remember(name);
        Environment.SetEnvironmentVariable(name, null);
    }

    private void Remember(string name)
    {
        if (!_original.ContainsKey(name))
        {
            _original[name] = Environment.GetEnvironmentVariable(name);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _original)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
