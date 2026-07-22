using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

public sealed class ProjectAccMappingOnCreateTests
{
    [Fact]
    public async Task CreateAsync_calls_acc_mapping_provisioner_after_commit()
    {
        var factory = await SeedCatalogAsync();
        var provisioner = new RecordingProvisioner();
        var sut = new SqlProjectCreateService(
            factory,
            folderBootstrapper: null,
            accMappingProvisioner: provisioner);

        var result = await sut.CreateAsync(new CreateProjectCommand(
            "פרויקט ACC",
            PlaceId: 1,
            CompanyId: 1,
            ContactId: 1,
            JobTypeIds: [9]));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.ProjectId);
        Assert.Equal(result.ProjectId, provisioner.LastProjectId);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public async Task CreateAsync_survives_acc_provision_failure_with_warning()
    {
        var factory = await SeedCatalogAsync();
        var sut = new SqlProjectCreateService(
            factory,
            folderBootstrapper: null,
            accMappingProvisioner: new ThrowingProvisioner());

        var result = await sut.CreateAsync(new CreateProjectCommand(
            "פרויקט ללא ACC",
            PlaceId: 1,
            CompanyId: 1,
            ContactId: 1,
            JobTypeIds: [9]));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.ProjectId);
        Assert.Contains("מיפוי ACC", result.WarningMessage, StringComparison.Ordinal);
    }

    private static async Task<IDbContextFactory<SiNetSQLDbContext>> SeedCatalogAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new SiNetSQLDbContext(options);
        db.Places.Add(new Place { Id = 1, Title = "תל אביב", InUse = true });
        db.Companies.Add(new Company { Id = 1, Title = "חברה", IsActive = true });
        db.Contacts.Add(new Contact { Id = 1, CompanyId = 1, FullName = "איש קשר", Title = "איש קשר", IsActive = true });
        db.JobTypes.Add(new JobType { Id = 9, Title = SqlProjectCreateService.DefaultJobTypeTitle });
        db.ProjectStatuses.Add(new ProjectStatus
        {
            Id = 1,
            Title = SqlProjectCreateService.DefaultQuoteStatusTitle,
        });
        await db.SaveChangesAsync();
        return new StubFactory(options);
    }

    private sealed class StubFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class RecordingProvisioner : IProjectAccMappingProvisioner
    {
        public int? LastProjectId { get; private set; }

        public Task EnsureMappingAsync(int projectId, CancellationToken cancellationToken = default)
        {
            LastProjectId = projectId;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingProvisioner : IProjectAccMappingProvisioner
    {
        public Task EnsureMappingAsync(int projectId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ACC down");
    }
}
