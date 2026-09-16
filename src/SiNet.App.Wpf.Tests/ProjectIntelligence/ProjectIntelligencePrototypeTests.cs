using System.IO;
using SiNet.Application.ProjectIntelligence;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectIntelligence;

public sealed class ProjectIntelligencePrototypeTests
{
    [Fact]
    public void Sanitizer_rejects_generic_foreign_number_and_sentences()
    {
        var terms = ProjectIntelligenceTermSanitizer.Sanitize(
            ["פרויקט", "יב", "עיריית יבנה", "3070", "זהו משפט ארוך מדי שאינו מונח חיפוש שימושי בכלל", "מגרש 166"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "3070" },
            new HashSet<int> { 99 });

        Assert.Contains("עיריית יבנה", terms);
        Assert.Contains("מגרש 166", terms);
        Assert.DoesNotContain(terms, static t => t.Contains("פרויקט", StringComparison.Ordinal));
        Assert.DoesNotContain("3070", terms);
    }

    [Fact]
    public void Enrichment_parser_keeps_layers_separate_and_drops_invented_ids()
    {
        var facts = new ProjectIntelligenceFacts(3070, "3070", "יבנה מזרח", "3070 יבנה מזרח", "יבנה", "עיריית יבנה", null, null, true);
        var derived = ProjectIntelligenceEnrichmentService.Parse(
            """{"aliases":["יבנה מזרח","פרויקט"],"abbreviations":["YM"],"distinctivePhrases":["מגרש 166"],"placeVariants":["יבנה"],"keywords":["9999"]}""",
            facts,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "9999", "3070" },
            new HashSet<int> { 1, 3070 },
            "fixture",
            "none");

        Assert.Contains("יבנה מזרח", derived.Aliases);
        Assert.DoesNotContain(derived.Aliases, static a => a == "פרויקט");
        Assert.Contains("מגרש 166", derived.DistinctivePhrases);
        Assert.DoesNotContain(derived.Keywords, static k => k == "9999");
        Assert.Equal("יבנה מזרח", facts.CanonicalName);
    }

    [Fact]
    public void Retriever_place_digit_without_project_fact_is_not_a_meaningful_match()
    {
        var catalog = BuildCatalog();
        var hits = new ProjectIntelligenceRetriever().Retrieve("רעננה 99", catalog, ProjectIntelligenceLayers.Facts, 5);
        Assert.DoesNotContain(hits, h => ProjectIntelligenceRetriever.HasMeaningfulMatch(h) && h.ProjectId is 101 or 202);
    }

    [Fact]
    public void Retriever_prefers_exact_number_and_explains_signals()
    {
        var catalog = BuildCatalog();
        var retriever = new ProjectIntelligenceRetriever();
        var hits = retriever.Retrieve("RE: תיאום תכנון יבנה מזרח מגרש 166", catalog, ProjectIntelligenceLayers.All, 5);

        var top = Assert.Single(hits.Where(h => h.ProjectId == 3070).Take(1));
        Assert.Equal(3070, hits[0].ProjectId);
        Assert.True(top.Score >= 70);
        Assert.Contains(top.Signals, static s => s.Contains("יבנה מזרח", StringComparison.Ordinal));
    }

    [Fact]
    public void Retriever_hold_out_does_not_count_the_eval_subject_as_historical()
    {
        var catalog = BuildCatalog();
        var held = ProjectIntelligenceCatalogBuilder.WithHeldOutSubject(
            catalog.Single(p => p.ProjectId == 3070),
            "תיאום תכנון יבנה מזרח");
        Assert.DoesNotContain(held.Observed.CleanedSubjects, static s => s.Contains("תיאום תכנון יבנה מזרח", StringComparison.Ordinal));

        var hits = new ProjectIntelligenceRetriever().Retrieve(
            "תיאום תכנון יבנה מזרח",
            catalog.Select(p => p.ProjectId == 3070 ? held : p).ToArray(),
            ProjectIntelligenceLayers.FactsAndObserved,
            5);

        Assert.Equal(3070, hits[0].ProjectId);
        Assert.DoesNotContain(
            hits[0].Signals,
            static s => s.Contains("תיאום תכנון יבנה מזרח", StringComparison.Ordinal));
    }

    [Fact]
    public void Reranker_never_accepts_an_id_that_was_not_supplied()
    {
        var ids = ProjectIntelligenceReranker.ParseIds(
            """{"rankedProjectIds":[3070,8888,101]}""",
            new HashSet<int> { 3070, 101 });

        Assert.Equal([3070, 101], ids);
    }

    [Fact]
    public void Fixture_eval_historical_beats_facts_on_informal_subjects()
    {
        var projects = FixtureProjects();
        var confirmed = FixtureConfirmed();
        var byProject = confirmed
            .GroupBy(static x => x.ProjectId)
            .ToDictionary(static g => g.Key, static g => (IReadOnlyList<string>)g.Select(x => x.Subject).ToArray());
        var catalog = ProjectIntelligenceCatalogBuilder.BuildCatalog(projects, byProject);
        var samples = ProjectIntelligenceEvaluator.BuildEvalSet(
            confirmed,
            projects.ToDictionary(static p => p.ProjectId),
            targetCount: 30);
        Assert.True(samples.Count >= 30);

        var retriever = new ProjectIntelligenceRetriever();
        var facts = ProjectIntelligenceEvaluator.Measure(
            "Facts",
            samples,
            sample => RetrieverRank(retriever, catalog, sample, ProjectIntelligenceLayers.Facts));
        var historical = ProjectIntelligenceEvaluator.Measure(
            "Facts+Historical",
            samples,
            sample => RetrieverRank(retriever, catalog, sample, ProjectIntelligenceLayers.FactsAndObserved));
        var existing = ProjectIntelligenceEvaluator.Measure(
            "Existing",
            samples,
            sample => ExistingProjectSearchBaseline.Rank(sample.Subject, projects));

        Assert.True(historical.Top1 >= facts.Top1);
        Assert.True(existing.SampleCount == samples.Count);
        Assert.Contains("intelligence", ProjectIntelligenceEvaluator.Recommend([existing, facts, historical]), StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(@"d:\repos2026\SiNetProjectManager_GitHub\artifacts\project-intelligence");
        File.WriteAllText(
            @"d:\repos2026\SiNetProjectManager_GitHub\artifacts\project-intelligence\phase1-fixture.txt",
            ProjectIntelligenceEvaluator.FormatReport(new ProjectIntelligenceEvalReport(
                confirmed.Count,
                samples.Count,
                catalog.Take(3).ToArray(),
                [existing, facts, historical],
                ProjectIntelligenceCatalogBuilder.EvaluateHierarchy(catalog),
                null,
                "fixture",
                "none",
                null,
                null,
                ProjectIntelligenceEvaluator.Recommend([existing, facts, historical]))));
    }

    private static IReadOnlyList<int> RetrieverRank(
        ProjectIntelligenceRetriever retriever,
        IReadOnlyList<ProjectIntelligenceProfile> catalog,
        ProjectIntelligenceEvalSample sample,
        ProjectIntelligenceLayers layers)
    {
        var working = catalog
            .Select(p => p.ProjectId == sample.KnownProjectId
                ? ProjectIntelligenceCatalogBuilder.WithHeldOutSubject(p, sample.Subject)
                : p)
            .ToArray();
        return retriever.Retrieve(sample.Subject, working, layers, 5).Select(static h => h.ProjectId).ToArray();
    }

    private static IReadOnlyList<ProjectIntelligenceProfile> BuildCatalog()
    {
        var projects = FixtureProjects();
        var byProject = FixtureConfirmed()
            .GroupBy(static x => x.ProjectId)
            .ToDictionary(static g => g.Key, static g => (IReadOnlyList<string>)g.Select(x => x.Subject).ToArray());
        var ai = new Dictionary<int, ProjectIntelligenceAiDerived>
        {
            [3070] = new(["יבנה מזרח"], ["YM"], ["מגרש 166"], ["יבנה"], ["נספח תנועה 166"], "fixture", "none"),
        };
        return ProjectIntelligenceCatalogBuilder.BuildCatalog(projects, byProject, ai);
    }

    private static IReadOnlyList<ProjectSummaryDto> FixtureProjects() =>
    [
        P(3070, "3070", "יבנה מזרח", "יבנה", "עיריית יבנה"),
        P(3045, "3045", "יבנה מערב", "יבנה", "עיריית יבנה"),
        P(101, "101", "קלאוזנר 7", "רעננה", "עיריית רעננה"),
        P(202, "202", "הרצל 12", "רעננה", "חברת נתיבי איילון"),
        P(303, "303", "מגדל הים", "נתניה", "יזם אלפא"),
        P(404, "404", "גבעת שמואל צפון", "גבעת שמואל", "עיריית גבעת שמואל"),
        P(505, "505", "נס ציונה מדע", "נס ציונה", "עיריית נס ציונה"),
        P(606, "606", "אשדוד נמל", "אשדוד", "חברת נמל אשדוד"),
    ];

    private static IReadOnlyList<ConfirmedProjectSubject> FixtureConfirmed()
    {
        var rows = new List<ConfirmedProjectSubject>();
        void Add(int id, string subject)
            => rows.Add(new ConfirmedProjectSubject(id, subject, DateTime.UtcNow, "t" + rows.Count));

        Add(3070, "תיאום תכנון יבנה מזרח");
        Add(3070, "מגרש 166 יבנה מזרח");
        Add(3070, "נספח תנועה 166");
        Add(3070, "RE: יבנה מזרח הגשה");
        Add(3045, "יבנה מערב עדכון סטטוס");
        Add(3045, "עיריית יבנה מערב");
        Add(101, "קלאוזנר 7 רעננה | הגשה מלאה כולל נספח תנועה עדכני");
        Add(101, "נספח תנועה קלאוזנר");
        Add(101, "קלאוזנר 7");
        Add(202, "הרצל 12 רעננה תאום");
        Add(202, "נתיבי איילון הרצל");
        Add(303, "מגדל הים נתניה");
        Add(303, "יזם אלפא מגדל הים");
        Add(404, "גבעת שמואל צפון תכנית");
        Add(505, "נס ציונה מדע הגשה");
        Add(606, "אשדוד נמל שער 3");
        Add(3070, "3070 יבנה מזרח");
        Add(101, "101 קלאוזנר");
        Add(202, "202 הרצל");
        Add(303, "303 מגדל הים");
        Add(404, "404 גבעת שמואל");
        Add(505, "505 נס ציונה");
        Add(606, "606 אשדוד");
        Add(3045, "3045 יבנה");
        Add(101, "עיריית רעננה קלאוזנר");
        Add(202, "רעננה הרצל 12");
        Add(404, "עיריית גבעת שמואל צפון");
        Add(505, "עיריית נס ציונה מדע");
        Add(606, "חברת נמל אשדוד");
        Add(303, "נתניה מגדל");
        Add(3070, "תיאום יבנה מזרח מגרש 166");
        Add(101, "השב: קלאוזנר רעננה");
        Add(3045, "יבנה מערב מגרש 12");
        return rows;
    }

    private static ProjectSummaryDto P(int id, string number, string name, string place, string company)
        => new(id, number, name, place, company, null, null, null, true, null, null, number + " " + name);
}
