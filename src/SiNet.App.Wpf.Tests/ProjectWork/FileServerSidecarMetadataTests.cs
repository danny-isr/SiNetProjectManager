using System.IO;
using SiNet.Infrastructure.FileSystem.ProjectWork;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class FileServerSidecarMetadataTests : IDisposable
{
    private readonly string _dir;

    public FileServerSidecarMetadataTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sinet_sidecar_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void IsMetadataCompanion_true_for_si_json_suffix()
        => Assert.True(FileServerSidecarMetadata.IsMetadataCompanion(Path.Combine(_dir, "plan.dwg.si.json")));

    [Fact]
    public void IsMetadataCompanion_true_for_json_next_to_data_sibling()
    {
        var data = Path.Combine(_dir, "report.pdf");
        File.WriteAllText(data, "x");
        Assert.True(FileServerSidecarMetadata.IsMetadataCompanion(data + ".json"));
    }

    [Fact]
    public void IsMetadataCompanion_false_for_plain_data_file()
        => Assert.False(FileServerSidecarMetadata.IsMetadataCompanion(Path.Combine(_dir, "report.pdf")));

    [Fact]
    public void TryReadSourceFileName_reads_source_from_sidecar()
    {
        var data = Path.Combine(_dir, "x.pdf");
        File.WriteAllText(data, "x");
        File.WriteAllText(data + ".si.json", "{\"SourceFileName\":\"original name.pdf\"}");

        Assert.Equal("original name.pdf", FileServerSidecarMetadata.TryReadSourceFileName(data));
    }

    [Fact]
    public void TryReadSourceFileName_null_when_missing()
        => Assert.Null(FileServerSidecarMetadata.TryReadSourceFileName(Path.Combine(_dir, "nope.pdf")));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
