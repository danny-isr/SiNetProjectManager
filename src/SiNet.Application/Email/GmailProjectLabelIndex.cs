namespace SiNet.Application.Email;

/// <summary>
/// Per-mailbox ProjectNumber → project-label entries, derived from a Gmail label catalog snapshot.
/// </summary>
public sealed class GmailProjectLabelIndex
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<ProjectLabelEntry>> _byNumber;

    public GmailProjectLabelIndex(
        IReadOnlyDictionary<int, IReadOnlyList<ProjectLabelEntry>> byNumber,
        string? sessionKey,
        int catalogGeneration)
    {
        _byNumber = byNumber ?? throw new ArgumentNullException(nameof(byNumber));
        SessionKey = sessionKey;
        CatalogGeneration = catalogGeneration;
    }

    public string? SessionKey { get; }

    public int CatalogGeneration { get; }

    public int ProjectCount => _byNumber.Count;

    public IReadOnlyList<ProjectLabelEntry> Find(int projectNumber)
        => projectNumber > 0 && _byNumber.TryGetValue(projectNumber, out var matches)
            ? matches
            : [];

    public ProjectLabelEntry? FindByLabelId(string? labelId)
    {
        if (string.IsNullOrWhiteSpace(labelId))
        {
            return null;
        }

        foreach (var matches in _byNumber.Values)
        {
            foreach (var entry in matches)
            {
                if (string.Equals(entry.LabelId, labelId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    public static GmailProjectLabelIndex Build(
        IEnumerable<(string Id, string Name)> labels,
        string rootLabel,
        string? sessionKey,
        int catalogGeneration)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLabel);

        var grouped = new Dictionary<int, List<ProjectLabelEntry>>();
        foreach (var (id, name) in labels)
        {
            var entry = EmailProjectLabelParser.TryParseProjectLabel(id, name, rootLabel);
            if (entry is null)
            {
                continue;
            }

            if (!grouped.TryGetValue(entry.ProjectNumber, out var list))
            {
                list = [];
                grouped[entry.ProjectNumber] = list;
            }

            list.Add(entry);
        }

        IReadOnlyDictionary<int, IReadOnlyList<ProjectLabelEntry>> frozen =
            grouped.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<ProjectLabelEntry>)pair.Value);

        return new GmailProjectLabelIndex(frozen, sessionKey, catalogGeneration);
    }
}
