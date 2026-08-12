using System.Text.Json;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Persists orphan ID sets between full reconciles for G6 (2 consecutive sightings).
/// Default root: %ProgramData%\SiOffice\MasterPlanSync\orphan-candidates\
/// </summary>
public sealed class OrphanSightingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _directory;

    public OrphanSightingsStore(string? directory = null)
    {
        _directory = directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SiOffice",
                "MasterPlanSync",
                "orphan-candidates");
    }

    public string DirectoryPath => _directory;

    public IReadOnlySet<int> Load(string entityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var path = GetPath(entityName);
        if (!File.Exists(path))
        {
            return new HashSet<int>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<SightingsFile>(json, JsonOptions);
            if (doc?.Ids is null)
            {
                return new HashSet<int>();
            }

            return doc.Ids.ToHashSet();
        }
        catch (Exception)
        {
            return new HashSet<int>();
        }
    }

    public void Save(string entityName, IReadOnlyList<int> ids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentNullException.ThrowIfNull(ids);

        Directory.CreateDirectory(_directory);
        var payload = new SightingsFile
        {
            Entity = entityName,
            UpdatedUtc = DateTime.UtcNow,
            Ids = ids.OrderBy(id => id).ToArray()
        };
        File.WriteAllText(GetPath(entityName), JsonSerializer.Serialize(payload, JsonOptions));
    }

    private string GetPath(string entityName)
        => Path.Combine(_directory, $"{Sanitize(entityName)}.json");

    private static string Sanitize(string entityName)
        => string.Concat(entityName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));

    private sealed class SightingsFile
    {
        public string Entity { get; set; } = string.Empty;
        public DateTime UpdatedUtc { get; set; }
        public int[] Ids { get; set; } = [];
    }
}
