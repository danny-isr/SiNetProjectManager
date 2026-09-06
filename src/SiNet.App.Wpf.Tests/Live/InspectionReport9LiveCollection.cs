using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>Serializes Report #9 live mutation tests so they do not race on shared DEV SQL rows.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InspectionReport9LiveCollection : ICollectionFixture<object>
{
    public const string Name = "InspectionReport9Live";
}
