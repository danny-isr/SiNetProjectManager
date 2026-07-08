using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email;
using SiNet.Infrastructure.Sql.Services.Email;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class SqlEmailThreadLinkQueryServiceTests
{
    [Fact]
    public async Task GetLinkStatesByGmailThreadIdsAsync_returns_mapping_without_inbox_row()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.Projects.Add(new Project
            {
                Id = 1042,
                Number = 1042,
                Title = "North",
                NameAndNumber = "1042 — North",
            });
            seed.ThreadStatusMappings.Add(new ThreadStatusMapping
            {
                ThreadUniqueId = "thread-unique-1",
                ThreadId = "gmail-thread-1",
                ProjectId = 1042,
                Status = ThreadMappingStatus.Assigned,
                LastUpdated = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var factory = new StubDbContextFactory(options);
        var sut = new SqlEmailThreadLinkQueryService(factory);

        var result = await sut.GetLinkStatesByGmailThreadIdsAsync(["gmail-thread-1"]);

        Assert.True(result.TryGetValue("gmail-thread-1", out var info));
        Assert.True(info.HasThreadHistory);
        Assert.Equal(1042, info.ThreadProjectId);
        Assert.Equal("1042 — North", info.ThreadProjectName);
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
