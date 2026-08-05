using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

public sealed class ProjectUpdateAndRenameTests
{
    [Fact]
    public async Task SaveAsync_updates_job_types_admin_worker_and_bid_value()
    {
        var factory = await SeedAsync();
        var create = new SqlProjectCreateService(factory);
        var created = await create.CreateAsync(new CreateProjectCommand(
            "לעריכה",
            1,
            1,
            1,
            [9],
            ApproveDescription: "הוגש ללקוח",
            JobTypeLines: [new CreateProjectJobTypeLine(9, AdminWorkerId: 1, BidValue: 100m)]));
        Assert.True(created.Succeeded);

        var sut = new SqlProjectUpdateService(factory);
        var save = await sut.SaveAsync(new UpdateProjectCommand(
            created.ProjectId!.Value,
            PlaceId: 1,
            CompanyId: 1,
            ContactId: 1,
            ParentProjectId: null,
            ProjectStatusId: null,
            ApproveDescription: "עודכן",
            JobTypes:
            [
                new ProjectJobTypeEditLine(9, "חומר כללי", true, 1, 250m),
                new ProjectJobTypeEditLine(2, "אחר", true, null, 10m),
            ]));

        Assert.True(save.Succeeded);

        await using var db = await factory.CreateDbContextAsync();
        var links = await db.TypeOfProjectInProjects
            .Where(t => t.ProjectId == created.ProjectId)
            .OrderBy(t => t.ProjectTypeId)
            .ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Equal(1, links.Single(t => t.ProjectTypeId == 9).AdminWorkerId);

        var bid = await db.Bids.SingleAsync(b => b.ProjectsId == created.ProjectId && b.JobTypeId == 9);
        Assert.Equal(250m, bid.BidValue);

        var project = await db.Projects.SingleAsync(p => p.Id == created.ProjectId);
        Assert.Equal("עודכן", project.ApproveDescription);
    }

    [Fact]
    public async Task RenameAnalyze_builds_predicted_name_and_checklist_steps()
    {
        var factory = await SeedAsync(placeTitle: "TelAviv");
        var create = new SqlProjectCreateService(factory);
        var created = await create.CreateAsync(new CreateProjectCommand("ישן", 1, 1, 1, [9]));
        Assert.True(created.Succeeded);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == created.ProjectId);
            project.Number = created.ProjectId;
            project.NameAndNumber = $"({created.ProjectId})ישן";
            await db.SaveChangesAsync();
        }

        var predicted = ProjectFolderNameHelper.BuildNameAndNumber(created.ProjectId!.Value, "חדש");
        Assert.Equal($"({created.ProjectId})חדש", predicted);

        var sut = new ProjectRenameOrchestrator(factory);
        var analysis = await sut.AnalyzeAsync(created.ProjectId.Value, "חדש");
        Assert.True(analysis.CanExecute);
        Assert.Contains(analysis.Steps, s => s.Kind == ProjectRenameStepKind.FileServer);
        Assert.Contains(analysis.Steps, s => s.Kind == ProjectRenameStepKind.Database);
        Assert.Equal(predicted, analysis.PredictedNameAndNumber);
    }

    [Fact]
    public void ProjectFolderNameHelper_FixDirectoryName_replaces_spaces_and_strips_invalid()
    {
        var fixedName = ProjectFolderNameHelper.FixDirectoryName("(1)A B/C");
        Assert.Equal("(1)A_BC", fixedName);
    }

    [Fact]
    public void EmailProjectLabel_number_regex_matches_leaf_and_detects_duplicate_numbers()
    {
        var leaves = new[] { "(12)Old", "(12)Dup", "(13)Other" };
        var dups = leaves
            .Select(l =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(l, @"^\((\d+)\)");
                return m.Success ? int.Parse(m.Groups[1].Value) : (int?)null;
            })
            .Where(n => n is not null)
            .GroupBy(n => n!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Equal([12], dups);
    }

    [Fact]
    public async Task CreateAsync_persists_admin_worker_bid_and_approve_description()
    {
        var factory = await SeedAsync();
        var sut = new SqlProjectCreateService(factory);
        var result = await sut.CreateAsync(new CreateProjectCommand(
            "עם ערך",
            1,
            1,
            1,
            [9],
            ApproveDescription: "למי הוגש",
            JobTypeLines: [new CreateProjectJobTypeLine(9, AdminWorkerId: 1, BidValue: 999m)]));

        Assert.True(result.Succeeded);
        await using var db = await factory.CreateDbContextAsync();
        var project = await db.Projects.SingleAsync(p => p.Id == result.ProjectId);
        Assert.Equal("למי הוגש", project.ApproveDescription);
        var link = await db.TypeOfProjectInProjects.SingleAsync(t => t.ProjectId == project.Id);
        Assert.Equal(1, link.AdminWorkerId);
        var bid = await db.Bids.SingleAsync(b => b.ProjectsId == project.Id);
        Assert.Equal(999m, bid.BidValue);
    }

    [Fact]
    public void FileServerRootResolver_BuildProjectFullPath_uses_place_and_name()
    {
        var project = new Project
        {
            NameAndNumber = "(5)Demo Name",
            Place = new Place { Title = "City A" },
        };
        var path = FileServerRootResolver.BuildProjectFullPath(project);
        Assert.NotNull(path);
        Assert.EndsWith(@"City_A\(5)Demo_Name", path, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IDbContextFactory<SiNetSQLDbContext>> SeedAsync(string placeTitle = "Place")
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubFactory(options);
        await using var db = await factory.CreateDbContextAsync();
        db.Places.Add(new Place { Id = 1, Title = placeTitle, InUse = true });
        db.Companies.Add(new Company { Id = 1, Title = "Co", IsActive = true });
        db.Contacts.Add(new Contact { Id = 1, CompanyId = 1, FullName = "איש קשר", Title = "איש קשר", IsActive = true });
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.JobTypes.Add(new JobType { Id = 2, Title = "אחר" });
        db.ProjectStatuses.Add(new ProjectStatus { Id = 1, Title = SqlProjectCreateService.DefaultQuoteStatusTitle });
        db.Siusers.Add(new Siuser { Id = 1, Name = "worker", IsActive = true });
        await db.SaveChangesAsync();
        return factory;
    }

    private sealed class StubFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
