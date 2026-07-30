using Microsoft.EntityFrameworkCore;
using Moq;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.FileCatalog;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin.FileCatalog;

public sealed class SqlFileCatalogWriteServiceTests
{
    [Fact]
    public async Task CreateFolder_and_CreateFile_and_assign_work()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
            seed.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(options);

        var folder = await sut.CreateFolderAsync(1, "הצעת מחיר");
        Assert.True(folder.Success);
        Assert.NotNull(folder.NewId);

        var file = await sut.CreateFileAsync(folder.NewId!.Value, 9);
        Assert.True(file.Success);
        Assert.NotNull(file.NewId);

        var child = await sut.CreateFolderAsync(folder.NewId.Value, "תת");
        Assert.True(child.Success);

        var assign = await sut.AssignFileToFolderAsync(file.NewId!.Value, child.NewId!.Value);
        Assert.True(assign.Success);

        await using var verify = new SiNetSQLDbContext(options);
        var row = await verify.ProjectFiles.SingleAsync(f => f.Id == file.NewId);
        Assert.Equal(child.NewId, row.Folderid);
        Assert.Equal(9, row.TypeProjId);
    }

    [Fact]
    public async Task DeleteFile_rejects_known_catalog_code()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
            seed.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
            seed.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "הצעת מחיר", Infolderid = 1 });
            seed.ProjectFiles.Add(new ProjectFile
            {
                Id = 50,
                Title = "אומדן הצעה",
                Code = ProjectFileCatalogCodes.QuoteEstimate,
                TypeProjId = 9,
                Folderid = 2,
                IsRequired = true,
            });
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(options);
        var result = await sut.DeleteFileAsync(50);
        Assert.False(result.Success);
        Assert.Contains("QuoteEstimate", result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(ProjectFileCatalogSeedData.IsKnownCatalogCode(ProjectFileCatalogCodes.QuoteEstimate));
    }

    private static SqlFileCatalogWriteService CreateSut(DbContextOptions<SiNetSQLDbContext> options)
    {
        var factory = new Mock<IDbContextFactory<SiNetSQLDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new SiNetSQLDbContext(options)));
        return new SqlFileCatalogWriteService(factory.Object);
    }
}
