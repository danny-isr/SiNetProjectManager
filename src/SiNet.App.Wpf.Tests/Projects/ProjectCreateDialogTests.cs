using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

public sealed class ProjectCreateServiceTests
{
    [Fact]
    public async Task CreateAsync_persists_multiple_job_types_and_quote_status()
    {
        var factory = await SeedCatalogAsync();
        var sut = new SqlProjectCreateService(factory);

        var result = await sut.CreateAsync(new CreateProjectCommand(
            "פרויקט בדיקה",
            PlaceId: 1,
            CompanyId: 1,
            ContactId: 1,
            JobTypeIds: [9, 2]));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.ProjectId);

        await using var db = await factory.CreateDbContextAsync();
        var project = await db.Projects.SingleAsync(p => p.Id == result.ProjectId);
        Assert.Equal("פרויקט בדיקה", project.Title);
        Assert.Equal(1, project.PlaceId);
        Assert.Equal(1, project.CompanyId);
        Assert.Equal(1, project.ContactsId);
        Assert.Equal(
            SqlProjectCreateService.DefaultQuoteStatusTitle,
            await db.ProjectStatuses.Where(s => s.Id == project.ProjectStatusId).Select(s => s.Title).SingleAsync());

        var typeIds = await db.TypeOfProjectInProjects
            .Where(t => t.ProjectId == project.Id)
            .Select(t => t.ProjectTypeId)
            .OrderBy(id => id)
            .ToListAsync();
        Assert.Equal(new int?[] { 2, 9 }, typeIds);
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_title()
    {
        var factory = await SeedCatalogAsync();
        var sut = new SqlProjectCreateService(factory);
        var first = await sut.CreateAsync(new CreateProjectCommand("כפול", 1, 1, 1, [9]));
        Assert.True(first.Succeeded);

        var second = await sut.CreateAsync(new CreateProjectCommand("כפול", 1, 1, 1, [9]));
        Assert.False(second.Succeeded);
        Assert.Contains("כבר קיים", second.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_requires_at_least_one_job_type()
    {
        var factory = await SeedCatalogAsync();
        var sut = new SqlProjectCreateService(factory);

        var result = await sut.CreateAsync(new CreateProjectCommand("בלי סוג", 1, 1, 1, []));
        Assert.False(result.Succeeded);
        Assert.Contains("סוג פרויקט", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveDefaultJobTypeId_prefers_legacy_id_9()
    {
        var factory = await SeedCatalogAsync();
        var sut = new SqlJobTypeQueryService(factory);

        var defaultId = await sut.ResolveDefaultJobTypeIdAsync();
        Assert.Equal(9, defaultId);
    }

    [Fact]
    public void AddSiNetProjectCreateSql_registers_create_ports()
    {
        var services = new ServiceCollection();
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbContextFactory(options));
        services.AddSiNetProjectCreateSql();

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetRequiredService<IProjectCreateService>());
        Assert.NotNull(sp.GetRequiredService<IPlaceCatalogService>());
        Assert.NotNull(sp.GetRequiredService<ICompanyCatalogService>());
        Assert.NotNull(sp.GetRequiredService<IJobTypeQueryService>());
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
        db.JobTypes.AddRange(
            new JobType { Id = 9, Title = SqlProjectCreateService.DefaultJobTypeTitle },
            new JobType { Id = 2, Title = "אדריכלות" });
        db.ProjectStatuses.Add(new ProjectStatus
        {
            Id = 1,
            Title = SqlProjectCreateService.DefaultQuoteStatusTitle,
        });
        await db.SaveChangesAsync();
        return new StubDbContextFactory(options);
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

public sealed class ProjectCreateDialogViewModelTests
{
    [Fact]
    public async Task CreateCommand_disabled_until_required_fields_filled()
    {
        var create = new StubCreateService();
        var places = new StubPlaceCatalog([new PlaceDto(1, "מקום")]);
        var companies = new StubCompanyCatalog(
            [new CompanyDto(1, "חברה")],
            [new ContactDto(1, 1, "איש קשר")]);
        var jobTypes = new StubJobTypes([new JobTypeDto(9, "חומר כללי"), new JobTypeDto(2, "אדריכלות")], defaultId: 9);

        var vm = new ProjectCreateDialogViewModel(
            create,
            places,
            companies,
            jobTypes,
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService());
        await vm.InitializeAsync();

        Assert.False(vm.CreateCommand.CanExecute(null));
        Assert.True(vm.JobTypes.Single(j => j.Id == 9).IsSelected);

        vm.ProjectName = "פרויקט";
        vm.SelectedPlace = vm.Places[0];
        vm.SelectedCompany = vm.Companies[0];
        await WaitUntilAsync(() => vm.Contacts.Count > 0);
        vm.SelectedContact = vm.Contacts[0];

        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task CreateCommand_invokes_service_with_selected_job_types()
    {
        var create = new StubCreateService { Result = CreateProjectResult.Ok(42, "פרויקט", "מקום") };
        var places = new StubPlaceCatalog([new PlaceDto(1, "מקום")]);
        var companies = new StubCompanyCatalog(
            [new CompanyDto(1, "חברה")],
            [new ContactDto(1, 1, "איש קשר")]);
        var jobTypes = new StubJobTypes([new JobTypeDto(9, "חומר כללי"), new JobTypeDto(2, "אדריכלות")], defaultId: 9);

        var vm = new ProjectCreateDialogViewModel(
            create,
            places,
            companies,
            jobTypes,
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService());
        await vm.InitializeAsync();
        vm.ProjectName = "פרויקט";
        vm.SelectedPlace = vm.Places[0];
        vm.SelectedCompany = vm.Companies[0];
        await WaitUntilAsync(() => vm.Contacts.Count > 0);
        vm.SelectedContact = vm.Contacts[0];
        vm.JobTypes.Single(j => j.Id == 2).IsSelected = true;

        var closed = false;
        vm.RequestClose += ok => closed = ok;
        vm.CreateCommand.Execute(null);
        await WaitUntilAsync(() => closed || !string.IsNullOrEmpty(vm.ValidationMessage));

        Assert.True(closed);
        Assert.Equal(42, vm.CreatedProjectId);
        Assert.NotNull(create.LastCommand);
        Assert.Contains(9, create.LastCommand!.JobTypeIds);
        Assert.Contains(2, create.LastCommand.JobTypeIds);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 40)
    {
        for (var i = 0; i < attempts && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    private sealed class StubCreateService : IProjectCreateService
    {
        public CreateProjectResult Result { get; set; } = CreateProjectResult.Ok(1, "x", "y");
        public CreateProjectCommand? LastCommand { get; private set; }

        public Task<decimal> GetNextProjectNumberAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(100m);

        public Task<bool> ProjectNameExistsAsync(string projectName, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<CreateProjectResult> CreateAsync(
            CreateProjectCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubPlaceCatalog(IReadOnlyList<PlaceDto> places) : IPlaceCatalogService
    {
        public Task<IReadOnlyList<PlaceDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(places);

        public Task<PlaceDto> SaveAsync(PlaceDto place, CancellationToken cancellationToken = default) =>
            Task.FromResult(place);
    }

    private sealed class StubCompanyCatalog(
        IReadOnlyList<CompanyDto> companies,
        IReadOnlyList<ContactDto> contacts) : ICompanyCatalogService
    {
        public Task<IReadOnlyList<CompanyDto>> ListCompaniesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(companies);

        public Task<IReadOnlyList<ContactDto>> ListContactsAsync(
            int companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(contacts);

        public Task<CompanyDto> AddCompanyAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompanyDto(99, title));

        public Task<ContactDto> AddContactAsync(
            int companyId,
            string displayName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContactDto(99, companyId, displayName));
    }

    private sealed class StubJobTypes(IReadOnlyList<JobTypeDto> types, int? defaultId) : IJobTypeQueryService
    {
        public Task<IReadOnlyList<JobTypeDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(types);

        public Task<int?> ResolveDefaultJobTypeIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(defaultId);
    }
}

public sealed class ProjectCreateDialogBoundaryTests
{
    [Fact]
    public void Create_dialog_xaml_uses_si_theme_keys()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Shared/Projects/ProjectCreateDialogView.xaml");
        Assert.Contains("SiBackgroundBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("SiTextNormalStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("SiComboBoxStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("SiPrimaryButtonStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("JobTypes", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_menu_opens_project_create_factory()
    {
        var source = ReadRepoFile("SiNetProjectManagerV2/MainWindow.xaml.cs");
        Assert.Contains("IProjectCreateDialogFactory", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "NavigateToView(new CreateProjectUserControl())",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_and_window_exist()
    {
        Assert.NotNull(typeof(IProjectCreateDialogFactory));
        Assert.NotNull(typeof(ProjectCreateDialogFactory));
        Assert.NotNull(typeof(ProjectCreateDialogWindow));
        Assert.NotNull(typeof(PlacePickerDialogViewModel));
        Assert.NotNull(typeof(CompanyContactPickerDialogViewModel));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !File.Exists(Path.Combine(dir.FullName, "SiNetProjectManager_GitHub.sln"))
               && !File.Exists(Path.Combine(dir.FullName, "src", "SiNet.App.Wpf", "SiNet.App.Wpf.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var root = dir!.FullName.EndsWith("SiNetProjectManager_GitHub", StringComparison.OrdinalIgnoreCase)
            ? dir.FullName
            : Path.Combine(dir.FullName, "SiNetProjectManager_GitHub");
        if (!Directory.Exists(root))
        {
            root = dir.FullName;
        }

        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
