using System.IO;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Files;

/// <summary>
/// Verifies the native (Infrastructure.Sql) <see cref="ProjectFileFilingService"/> FileServer path:
/// convention naming, companion JSON, and version archival on replacement. ACC upload is not
/// exercised here (that path is exercised through the ACC adapter integration tests).
/// </summary>
public sealed class ProjectFileFilingServiceTests : IDisposable
{
    private readonly string _rootDir = Path.Combine(Path.GetTempPath(), "sinet_filing_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task FileServer_places_convention_named_file_with_companion_json()
    {
        var (service, sourcePath) = await BuildAsync();

        var result = await service.FileAsync(new FileProjectFileRequest(
            ProjectId: 1,
            ProjectFileId: 1,
            ProjectAlternativeId: null,
            SourceLocalPath: sourcePath,
            OriginalFileName: "incoming.pdf",
            SourceType: FileInstanceSourceType.EmailAttachment));

        Assert.Equal(FileStorageDestination.FileServer, result.StorageDestination);
        Assert.Equal(1, result.CurrentVersionNumber);
        Assert.Null(result.ArchivedPreviousVersion);

        // (ProjectNumber)-TypeProjId-FileNumber-Alt-Version-Title.ext
        Assert.Equal("(100)-5-10-1-1-Plan.pdf", result.PlacedFileName);
        Assert.True(File.Exists(result.PlacedFilePath));
        Assert.True(File.Exists(result.PlacedFilePath + ".json"));
    }

    [Fact]
    public async Task FileServer_archives_previous_active_file_and_bumps_version()
    {
        var (service, sourcePath) = await BuildAsync();

        var first = await service.FileAsync(NewRequest(sourcePath));
        var second = await service.FileAsync(NewRequest(sourcePath));

        Assert.Equal(1, first.CurrentVersionNumber);
        Assert.Equal(2, second.CurrentVersionNumber);
        Assert.NotNull(second.ArchivedPreviousVersion);
        Assert.Equal(1, second.ArchivedPreviousVersion!.Value.ArchivedVersionNumber);

        var versionsDir = Path.Combine(Path.GetDirectoryName(second.PlacedFilePath!)!, ".versions");
        Assert.True(Directory.Exists(versionsDir));
        Assert.Contains(
            Directory.GetFiles(versionsDir),
            f => Path.GetFileName(f).Contains(".v1", StringComparison.Ordinal) && f.EndsWith(".pdf", StringComparison.Ordinal));
        Assert.True(File.Exists(second.PlacedFilePath));
    }

    private static FileProjectFileRequest NewRequest(string sourcePath) => new(
        ProjectId: 1,
        ProjectFileId: 1,
        ProjectAlternativeId: null,
        SourceLocalPath: sourcePath,
        OriginalFileName: "incoming.pdf",
        SourceType: FileInstanceSourceType.EmailAttachment);

    private async Task<(ProjectFileFilingService Service, string SourcePath)> BuildAsync()
    {
        Directory.CreateDirectory(_rootDir);
        var sourcePath = Path.Combine(_rootDir, "src.pdf");
        await File.WriteAllTextAsync(sourcePath, "content");

        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase("filing_" + Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new PooledFactory(options);

        await using (var db = factory.CreateDbContext())
        {
            db.Projects.Add(new Project { Id = 1, Number = 100, NameAndNumber = "(100)Proj", Title = "Proj" });
            db.ProjectFiles.Add(new ProjectFile
            {
                Id = 1,
                Number = 10,
                TypeProjId = 5,
                Title = "Plan",
                StorageDestination = FileStorageDestination.FileServer,
            });
            await db.SaveChangesAsync();
        }

        var metadataStore = new FileServerMetadataStore();
        var service = new ProjectFileFilingService(
            factory,
            new FolderPathResolver(),
            metadataStore,
            new FileServerVersionArchiver(metadataStore),
            new FixedRootResolver(_rootDir),
            new ThrowingAccUploadService());

        return (service, sourcePath);
    }

    private sealed class PooledFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedRootResolver(string root) : IFileServerRootResolver
    {
        public Task<string?> ResolveAsync(SiNetSQLDbContext db, int projectId, CancellationToken ct = default)
            => Task.FromResult<string?>(root);
    }

    private sealed class ThrowingAccUploadService : IAccFileUploadService
    {
        public Task<AccFileUploadResult> UploadAsync(AccFileUploadRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("ACC upload should not be called for the FileServer path.");
    }
}
