using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Serializes L4W Pilot smoke tests — they share evidence file timestamps and Pilot.* settings on one DB.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PilotSmokeTestCollection
{
    public const string Name = "PilotSmoke";
}
