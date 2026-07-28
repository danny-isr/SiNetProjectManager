using System.IO;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SiNet.App.Wpf.Tests.Boundary;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Infrastructure.Sql.AutodeskLocal;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

/// <summary>
/// The SQL layer owns the hub lookup; the remote ACC call is delegated to
/// <see cref="IAccProjectRootFolderIdReader"/> so SiNet.Infrastructure.Sql no longer references
/// the Autodesk connector.
/// </summary>
public sealed class LocalAccProjectRootFolderResolverTests
{
    [Fact]
    public async Task WhenProjectHasAccMappingThenHubFromMappingIsUsed()
    {
        var options = await SeedAsync(seed =>
        {
            seed.AccHubs.Add(new AccHub { Id = 7, HubId = "b.hub-mapped" });
            seed.ProjectAccMappings.Add(new ProjectAccMapping
            {
                Id = 1,
                ProjectId = 100,
                AccHubId = 7,
                AccProjectId = "b.project-1",
            });
        });

        var reader = new RecordingRootFolderIdReader("urn:folder:mapped");
        var sut = new LocalAccProjectRootFolderResolver(new StubDbContextFactory(options), reader);

        await sut.ResolveProjectFilesRootFolderIdAsync("b.project-1");

        Assert.Equal("b.hub-mapped", reader.LastHubId);
    }

    [Fact]
    public async Task WhenReaderReturnsAFolderIdThenResolverReturnsIt()
    {
        var options = await SeedAsync(seed =>
        {
            seed.AccHubs.Add(new AccHub { Id = 7, HubId = "b.hub-mapped" });
            seed.ProjectAccMappings.Add(new ProjectAccMapping
            {
                Id = 1,
                ProjectId = 100,
                AccHubId = 7,
                AccProjectId = "b.project-1",
            });
        });

        var sut = new LocalAccProjectRootFolderResolver(
            new StubDbContextFactory(options),
            new RecordingRootFolderIdReader("urn:folder:mapped"));

        var result = await sut.ResolveProjectFilesRootFolderIdAsync("b.project-1");

        Assert.Equal("urn:folder:mapped", result);
    }

    [Fact]
    public async Task WhenProjectIsOnlyASystemResourceThenHubFromSystemResourceIsUsed()
    {
        var options = await SeedAsync(seed =>
        {
            seed.AccHubs.Add(new AccHub { Id = 9, HubId = "b.hub-system" });
            seed.AccSystemResources.Add(new AccSystemResource
            {
                Key = "OfficeInbox",
                AccHubId = 9,
                AccProjectId = "b.project-2",
            });
        });

        var reader = new RecordingRootFolderIdReader("urn:folder:system");
        var sut = new LocalAccProjectRootFolderResolver(new StubDbContextFactory(options), reader);

        await sut.ResolveProjectFilesRootFolderIdAsync("b.project-2");

        Assert.Equal("b.hub-system", reader.LastHubId);
    }

    [Fact]
    public async Task WhenProjectIdHasNoHubThenReaderIsNotCalled()
    {
        var options = await SeedAsync(static _ => { });

        var reader = new RecordingRootFolderIdReader("urn:folder:unused");
        var sut = new LocalAccProjectRootFolderResolver(new StubDbContextFactory(options), reader);

        await sut.ResolveProjectFilesRootFolderIdAsync("b.unknown");

        Assert.Null(reader.LastHubId);
    }

    [Fact]
    public async Task WhenProjectIdIsMissingTheBimPrefixThenItIsNormalized()
    {
        var options = await SeedAsync(seed =>
        {
            seed.AccHubs.Add(new AccHub { Id = 11, HubId = "b.hub-normalized" });
            seed.ProjectAccMappings.Add(new ProjectAccMapping
            {
                Id = 2,
                ProjectId = 200,
                AccHubId = 11,
                AccProjectId = "b.project-3",
            });
        });

        var reader = new RecordingRootFolderIdReader("urn:folder:normalized");
        var sut = new LocalAccProjectRootFolderResolver(new StubDbContextFactory(options), reader);

        await sut.ResolveProjectFilesRootFolderIdAsync("project-3");

        Assert.Equal("b.project-3", reader.LastProjectId);
    }

    [Fact]
    public async Task WhenNoRootFolderReaderIsRegisteredThenResolverReportsUnknown()
    {
        var options = await SeedAsync(seed =>
        {
            seed.AccHubs.Add(new AccHub { Id = 13, HubId = "b.hub-no-reader" });
            seed.ProjectAccMappings.Add(new ProjectAccMapping
            {
                Id = 3,
                ProjectId = 300,
                AccHubId = 13,
                AccProjectId = "b.project-4",
            });
        });

        var sut = new LocalAccProjectRootFolderResolver(new StubDbContextFactory(options), rootFolderIdReader: null);

        var result = await sut.ResolveProjectFilesRootFolderIdAsync("b.project-4");

        Assert.Null(result);
    }

    [Fact]
    public void Infrastructure_Sql_csproj_does_not_reference_the_autodesk_connector()
    {
        var csproj = Path.Combine(
            RepoPaths.RepoRoot, "src", "SiNet.Infrastructure.Sql", "SiNet.Infrastructure.Sql.csproj");

        var references = XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(
            references,
            r => r.Contains("AutodeskConnector", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Infrastructure_Sql_source_does_not_use_the_autodesk_sdk_namespace()
    {
        var root = Path.Combine(RepoPaths.RepoRoot, "src", "SiNet.Infrastructure.Sql");

        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("MyOffice.AutodeskConnector", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.Empty(offenders);
    }

    private static async Task<DbContextOptions<SiNetSQLDbContext>> SeedAsync(Action<SiNetSQLDbContext> seedAction)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var seed = new SiNetSQLDbContext(options);
        seedAction(seed);
        await seed.SaveChangesAsync();

        return options;
    }

    private sealed class RecordingRootFolderIdReader(string? folderId) : IAccProjectRootFolderIdReader
    {
        public string? LastHubId { get; private set; }

        public string? LastProjectId { get; private set; }

        public Task<string?> GetProjectRootFolderIdAsync(
            string hubId,
            string projectId,
            CancellationToken cancellationToken = default)
        {
            LastHubId = hubId;
            LastProjectId = projectId;
            return Task.FromResult(folderId);
        }
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
