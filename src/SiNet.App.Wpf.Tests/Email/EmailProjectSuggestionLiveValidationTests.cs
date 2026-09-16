using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using SiNet.App.Wpf.Tests.Live;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

/// <summary>
/// Read-only local ranking against live Project facts. Skips when SQL is unavailable.
/// Does not call AI, Gmail, or write mailbox/labels.
/// </summary>
public sealed class EmailProjectSuggestionLiveValidationTests
{
    [Fact]
    public async Task WhenSqlAvailableThenRankManualSubjectsWithoutAi()
    {
        var connection = LiveEnvironment.TryResolveSqlConnectionString();
        if (string.IsNullOrWhiteSpace(connection))
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddSiNetSecrets();
        services.AddSiNetSql(connection);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetProjectQuerySql();
        services.AddSiNetAi();
        await using var sp = services.BuildServiceProvider();

        var swCatalog = Stopwatch.StartNew();
        var projects = await sp.GetRequiredService<IProjectQueryService>()
            .SearchProjectsAsync(new ProjectSearchQuery(IncludeClosed: false))
            .ConfigureAwait(true);
        swCatalog.Stop();
        Assert.True(projects.Count > 0, "Expected active project facts from SQL.");

        var service = sp.GetRequiredService<IEmailProjectSuggestionService>();
        var warmup = service.Suggest("3070", projects);

        var subjects = new[]
        {
            "הושלם הטיפול בפנייתך תיאום תכנון ביבנה מזרח",
            "קלאוזנר 7 רעננה | הגשה מלאה כולל נספח תנועה עדכני",
            "בקשת הצעת מחיר",
            "תכנון",
            "שלום",
            "3070",
            "יבנה מזרח מגרש 166",
            "נספח תנועה עדכני",
            "הגשה מלאה כולל נספח תנועה",
            "תיאום תכנון",
            "בדיקה",
            "פרויקט",
        };

        var latencies = new List<double>(subjects.Length);
        var sb = new StringBuilder();
        sb.AppendLine($"Active projects: {projects.Count}");
        sb.AppendLine($"Catalog query ms: {swCatalog.Elapsed.TotalMilliseconds:F1}");
        sb.AppendLine($"Warmup suggestions: {warmup.Suggestions.Count}; catalog build ms: {warmup.CatalogBuildElapsed.TotalMilliseconds:F1}; retrieve ms: {warmup.Elapsed.TotalMilliseconds:F1}");
        sb.AppendLine();

        foreach (var subject in subjects)
        {
            var result = service.Suggest(subject, projects);
            latencies.Add(result.Elapsed.TotalMilliseconds);
            sb.Append("Subject: ").AppendLine(subject);
            if (result.Suggestions.Count == 0)
            {
                sb.AppendLine("Suggested projects: (none)");
            }
            else
            {
                foreach (var row in result.Suggestions)
                {
                    sb.Append("  ")
                        .Append(row.Project.ProjectNumber)
                        .Append(" — ")
                        .Append(row.Project.ProjectName);
                    if (!string.IsNullOrWhiteSpace(row.ExplanationHe))
                    {
                        sb.Append(" (").Append(row.ExplanationHe).Append(')');
                    }

                    sb.AppendLine();
                }
            }

            sb.AppendLine($"Elapsed ms: {result.Elapsed.TotalMilliseconds:F1}");
            sb.AppendLine();
            Assert.All(result.Suggestions, s => Assert.Contains(projects, p => p.ProjectId == s.Project.ProjectId));
            Assert.Equal(result.Suggestions.Select(s => s.Project.ProjectId).Distinct().Count(), result.Suggestions.Count);
        }

        latencies.Sort();
        var avg = latencies.Average();
        var p95 = latencies[Math.Clamp((int)Math.Ceiling(latencies.Count * 0.95) - 1, 0, latencies.Count - 1)];
        sb.AppendLine($"Average retrieval ms: {avg:F1}");
        sb.AppendLine($"P95 retrieval ms: {p95:F1}");

        foreach (var generic in new[] { "בקשת הצעת מחיר", "תכנון", "שלום", "בדיקה", "פרויקט" })
        {
            Assert.Empty(service.Suggest(generic, projects).Suggestions);
        }

        var outDir = Path.Combine(FindRepoRoot(), "artifacts", "project-intelligence");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "picker-facts-manual-subjects.txt"), sb.ToString());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !File.Exists(Path.Combine(dir.FullName, "src", "SiNet.App.Wpf", "SiNet.App.Wpf.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
