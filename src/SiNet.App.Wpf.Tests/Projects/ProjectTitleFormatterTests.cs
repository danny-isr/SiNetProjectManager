using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>
/// Tests for the shell title behavior (docs/PROJECTS.md §11): the pure
/// <see cref="ProjectTitleFormatter"/> and the subscribe-and-format pattern the shell
/// (<c>SiNetProjectManagerV2/MainWindow</c>) uses over <see cref="ICurrentProjectContext"/>.
/// <para>
/// The WPF <c>MainWindow</c> itself is not exercised directly (it needs a live host/Dispatcher and DI).
/// Instead these tests use a tiny <see cref="TitleSubscriber"/> that mirrors the shell's logic exactly:
/// it subscribes to <see cref="ICurrentProjectContext.CurrentProjectChanged"/> and formats the title via
/// <see cref="ProjectTitleFormatter"/>. This proves the observable contract the shell depends on without
/// UI brittleness.
/// </para>
/// </summary>
public sealed class ProjectTitleFormatterTests
{
    private const string DefaultTitle = "תוכנת ניהול   v1.2.3";

    private static ProjectSummaryDto Project(int id, string name)
        => new(
            ProjectId: id,
            ProjectNumber: id.ToString(),
            ProjectName: name,
            PlaceName: null,
            CompanyName: null,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true);

    /// <summary>
    /// Stand-in for the shell title owner: subscribes like <c>MainWindow</c> and records the title
    /// it would set, plus how many times it updated. No WPF/Dispatcher needed.
    /// </summary>
    private sealed class TitleSubscriber : IDisposable
    {
        private readonly ICurrentProjectContext _context;

        public TitleSubscriber(ICurrentProjectContext context, string defaultTitle)
        {
            _context = context;
            DefaultTitle = defaultTitle;
            // Seed exactly like the shell does from any already-selected project.
            Title = ProjectTitleFormatter.Format(defaultTitle, context.CurrentProject);
            _context.CurrentProjectChanged += OnCurrentProjectChanged;
        }

        public string DefaultTitle { get; }
        public string Title { get; private set; }
        public int UpdateCount { get; private set; }

        private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
        {
            Title = ProjectTitleFormatter.Format(DefaultTitle, e.Project);
            UpdateCount++;
        }

        public void Dispose() => _context.CurrentProjectChanged -= OnCurrentProjectChanged;
    }

    [Fact]
    public void Format_with_null_project_returns_default_title()
    {
        Assert.Equal(DefaultTitle, ProjectTitleFormatter.Format(DefaultTitle, (ProjectSummaryDto?)null));
    }

    [Fact]
    public void Format_with_project_returns_default_dash_project_name()
    {
        var result = ProjectTitleFormatter.Format(DefaultTitle, Project(42, "North Towers"));

        Assert.Equal($"{DefaultTitle} - North Towers", result);
    }

    [Fact]
    public void Format_with_blank_project_name_returns_default_title()
    {
        // A project with a whitespace name must not produce a dangling " - " suffix.
        var result = ProjectTitleFormatter.Format(DefaultTitle, Project(7, "   "));

        Assert.Equal(DefaultTitle, result);
    }

    [Fact]
    public void Format_string_overload_matches_dto_overload()
    {
        Assert.Equal(DefaultTitle, ProjectTitleFormatter.Format(DefaultTitle, (string?)null));
        Assert.Equal($"{DefaultTitle} - Delta", ProjectTitleFormatter.Format(DefaultTitle, "Delta"));
    }

    [Fact]
    public async Task Subscriber_updates_title_only_when_current_project_changes()
    {
        var context = new InMemoryCurrentProjectContext();
        using var subscriber = new TitleSubscriber(context, DefaultTitle);

        Assert.Equal(DefaultTitle, subscriber.Title);
        Assert.Equal(0, subscriber.UpdateCount);

        await context.SetCurrentProjectAsync(Project(1, "Alpha"));

        Assert.Equal($"{DefaultTitle} - Alpha", subscriber.Title);
        Assert.Equal(1, subscriber.UpdateCount);

        await context.SetCurrentProjectAsync(null); // clear -> back to default

        Assert.Equal(DefaultTitle, subscriber.Title);
        Assert.Equal(2, subscriber.UpdateCount);
    }

    [Fact]
    public async Task Duplicate_current_project_selection_does_not_update_title_twice()
    {
        // The context de-dupes by ProjectId, so re-selecting the same project fires no event and the
        // shell title subscriber is not invoked a second time.
        var context = new InMemoryCurrentProjectContext();
        using var subscriber = new TitleSubscriber(context, DefaultTitle);

        await context.SetCurrentProjectAsync(Project(5, "Foxtrot"));   // update once
        await context.SetCurrentProjectAsync(Project(5, "Foxtrot 2")); // same id -> no event

        Assert.Equal($"{DefaultTitle} - Foxtrot", subscriber.Title);
        Assert.Equal(1, subscriber.UpdateCount);
    }

    [Fact]
    public async Task Subscriber_stops_updating_after_dispose()
    {
        // Mirrors the shell unsubscribing in OnClosing: no further title updates after detach.
        var context = new InMemoryCurrentProjectContext();
        var subscriber = new TitleSubscriber(context, DefaultTitle);
        subscriber.Dispose();

        await context.SetCurrentProjectAsync(Project(9, "Hotel"));

        Assert.Equal(DefaultTitle, subscriber.Title);
        Assert.Equal(0, subscriber.UpdateCount);
    }
}
