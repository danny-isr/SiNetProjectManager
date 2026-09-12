using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfDispatcherCollection
{
    public const string Name = "WpfDispatcher";
}
