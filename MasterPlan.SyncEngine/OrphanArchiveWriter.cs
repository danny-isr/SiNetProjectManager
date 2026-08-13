using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MasterPlan.SyncEngine;

/// <summary>
/// JSON archive of replica rows about to be deleted (DEV-025). Write+flush before DELETE.
/// Folder: <c>{MasterPlanBakup}/OrphanArchive/</c>. File name:
/// <c>orphan-purge-{entity}-{yyyyMMdd-HHmmss}.json</c>. Retention 30 days.
/// </summary>
public static class OrphanArchiveWriter
{
    public const string FilePrefix = "orphan-purge-";
    public const int DefaultRetentionDays = 30;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static string BuildFileName(string entityName, DateTime deletedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        var stamp = deletedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return $"{FilePrefix}{entityName}-{stamp}.json";
    }

    public static string WriteEventFile(
        string directory,
        string entityName,
        DateTime deletedAtUtc,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentNullException.ThrowIfNull(rows);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, BuildFileName(entityName, deletedAtUtc));

        var rowsArray = new JsonArray();
        foreach (var row in rows)
        {
            var obj = new JsonObject();
            foreach (var pair in row)
                obj[pair.Key] = ToJsonNode(pair.Value);
            rowsArray.Add(obj);
        }

        var root = new JsonObject
        {
            ["entity"] = entityName,
            ["deletedAtUtc"] = deletedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            ["rowCount"] = rows.Count,
            ["rows"] = rowsArray
        };

        var json = root.ToJsonString(WriteOptions);
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(json);
        writer.Flush();
        stream.Flush(flushToDisk: true);
        return path;
    }

    public static int DeleteExpiredFiles(string directory, DateTime utcNow, int retentionDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (retentionDays <= 0 || !Directory.Exists(directory))
            return 0;

        var cutoff = utcNow.AddDays(-retentionDays);
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(directory, $"{FilePrefix}*.json"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                File.Delete(file);
                deleted++;
            }
        }

        return deleted;
    }

    public static Dictionary<string, object?> ToJsonRow(IDictionary<string, object> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
            row[pair.Key] = Normalize(pair.Value);
        return row;
    }

    private static JsonNode? ToJsonNode(object? value)
    {
        if (value is null or DBNull)
            return null;

        return value switch
        {
            JsonNode node => node,
            string s => s,
            bool b => b,
            byte or sbyte or short or ushort or int or uint or long or ulong
                => JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            float or double or decimal
                => JsonValue.Create(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static object? Normalize(object? value)
    {
        if (value is null or DBNull)
            return null;

        return value switch
        {
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value
        };
    }
}
