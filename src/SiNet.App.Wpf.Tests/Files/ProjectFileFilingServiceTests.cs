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

    [Fact]
    public async Task Acc_missing_mapping_triggers_on_demand_provision_then_uploads()
    {
        Directory.CreateDirectory(_rootDir);
        var sourcePath = Path.Combine(_rootDir, "src.pdf");
        await File.WriteAllTextAsync(sourcePath, "content");

        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase("filing_acc_" + Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new PooledFactory(options);

        await using (var db = factory.CreateDbContext())
        {
            db.Projects.Add(new Project { Id = 7, Number = 700, NameAndNumber = "(700)P", Title = "P" });
            db.ProjectFiles.Add(new ProjectFile
            {
                Id = 7,
                Number = 1,
                TypeProjId = 1,
                Title = "Doc",
                StorageDestination = FileStorageDestination.Acc,
            });
            await db.SaveChangesAsync();
        }

        var provisioner = new RecordingAccProvisioner(factory);
        var upload = new CapturingAccUploadService();
        var metadataStore = new FileServerMetadataStore();
        var service = new ProjectFileFilingService(
            factory,
            new FolderPathResolver(),
            metadataStore,
            new FileServerVersionArchiver(metadataStore),
            new FixedRootResolver(_rootDir),
            upload,
            provisioner);

        var result = await service.FileAsync(new FileProjectFileRequest(
            ProjectId: 7,
            ProjectFileId: 7,
            ProjectAlternativeId: null,
            SourceLocalPath: sourcePath,
            OriginalFileName: "a.pdf",
            SourceType: FileInstanceSourceType.EmailAttachment));

        Assert.Equal(7, provisioner.LastProjectId);
        Assert.Equal(FileStorageDestination.Acc, result.StorageDestination);
        Assert.Equal("acc-item", result.TargetAccItemId);
        Assert.Equal("b.proj", upload.LastAccProjectId);
    }

    private sealed class RecordingAccProvisioner(IDbContextFactory<SiNetSQLDbContext> factory)
        : SiNet.Application.Projects.IProjectAccMappingProvisioner
    {
        public int? LastProjectId { get; private set; }

        public async Task EnsureMappingAsync(int projectId, CancellationToken cancellationToken = default)
        {
            LastProjectId = projectId;
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            db.ProjectAccMappings.Add(new ProjectAccMapping
            {
                ProjectId = projectId,
                AccProjectId = "b.proj",
                AccTargetFolderId = "folder-1",
                DocsStatus = DocsStatus.Ready,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CapturingAccUploadService : IAccFileUploadService
    {
        public string? LastAccProjectId { get; private set; }

        public Task<AccFileUploadResult> UploadAsync(AccFileUploadRequest request, CancellationToken cancellationToken = default)
        {
            LastAccProjectId = request.ProjectId;
            return Task.FromResult(new AccFileUploadResult(
                FolderId: "folder-1",
                ItemId: "acc-item",
                VersionId: "acc-ver",
                FileName: request.DisplayName,
                AlreadySameSource: false));
        }
    }

    private sealed class PooledFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
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
