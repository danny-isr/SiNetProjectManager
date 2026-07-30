using Microsoft.EntityFrameworkCore;
using Moq;
using SiNet.Application.ProjectWork;
using SiNet.Infrastructure.Sql.Services.ProjectWork;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class SqlProjectFolderWriteServiceTests
{
    [Fact]
    public async Task CreateChildFolder_inserts_under_parent_and_rejects_duplicate_title()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
            seed.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "הצעת מחיר", Infolderid = 1 });
            await seed.SaveChangesAsync();
        }

        var factory = new Mock<IDbContextFactory<SiNetSQLDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new SiNetSQLDbContext(options)));

        var pathResolver = new Mock<IProjectFolderPathResolver>();
        pathResolver
            .Setup(r => r.ResolveFileServerFolderPathAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var sut = new SqlProjectFolderWriteService(factory.Object, pathResolver.Object);

        var created = await sut.CreateChildFolderAsync(2, "תת תיקייה", projectId: 3146);
        Assert.True(created.Success);
        Assert.NotNull(created.FolderId);

        await using (var verify = new SiNetSQLDbContext(options))
        {
            var row = await verify.ProjectFolders.SingleAsync(f => f.Id == created.FolderId);
            Assert.Equal("תת תיקייה", row.Title);
            Assert.Equal(2, row.Infolderid);
        }

        var dup = await sut.CreateChildFolderAsync(2, "תת תיקייה", projectId: 3146);
        Assert.False(dup.Success);
        Assert.Contains("כבר קיימת", dup.ErrorMessage, StringComparison.Ordinal);
    }
}
