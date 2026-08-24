using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Serializes the certification tier. Its scenarios share one database, one Gmail mailbox, one ACC project
/// and one evidence file, and they mutate <c>SystemSettings</c> and restore it — so concurrent runs would
/// corrupt each other's state and each other's evidence. The L4W smoke needed the same treatment after
/// parallel runs collided on the evidence file.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SystemCertificationTestCollection
{
    public const string Name = "SystemCertification";
}
