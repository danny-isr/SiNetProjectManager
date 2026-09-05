using System.Text;
using Google.Apis.Gmail.v1.Data;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Race-safe get-or-create helpers for Gmail labels: list → create → on 409/conflict re-list and reuse.
/// Matching accounts for Gmail collapsing consecutive whitespace in label names.
/// </summary>
internal static class GmailLabelIdempotency
{
    public static string NormalizeLabelName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Gmail stores labels in a form that collapses runs of whitespace (observed: DB
        // NameAndNumber with a double space cannot be persisted as a distinct label name).
        var formC = name.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(formC.Length);
        var prevSpace = false;
        foreach (var c in formC)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    public static bool NamesMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            NormalizeLabelName(left),
            NormalizeLabelName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<Label> FindNormalizedMatches(IEnumerable<Label>? labels, string fullPath)
    {
        if (labels is null || string.IsNullOrWhiteSpace(fullPath))
            return Array.Empty<Label>();

        var intended = NormalizeLabelName(fullPath);
        return labels
            .Where(label =>
                !string.IsNullOrWhiteSpace(label.Name)
                && !string.IsNullOrWhiteSpace(label.Id)
                && string.Equals(NormalizeLabelName(label.Name), intended, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Project-label identity: 0 → null, 1 → label, &gt;1 → ambiguous failure (never FirstOrDefault).
    /// </summary>
    public static Label? FindExactByName(IEnumerable<Label>? labels, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        var matches = FindNormalizedMatches(labels, fullPath);
        if (matches.Count == 0)
            return null;
        if (matches.Count == 1)
            return matches[0];

        throw CreateAmbiguousException(fullPath, matches, labels);
    }

    public static bool IsLabelExistsOrConflicts(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            var message = cur.Message ?? string.Empty;
            if (message.Contains("exists or conflicts", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Label name exists", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (cur is global::Google.GoogleApiException googleEx
                && (int)googleEx.HttpStatusCode == 409)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// After a create conflict, resolve the intended label from a fresh list.
    /// Returns the unique exact match (including Gmail whitespace-normalized equality),
    /// or throws with a precise diagnostic.
    /// </summary>
    public static Label ResolveIntendedAfterConflict(
        IEnumerable<Label>? labels,
        string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        var matches = FindNormalizedMatches(labels, fullPath);
        if (matches.Count == 1)
            return matches[0];

        var near = FormatNearCandidates(fullPath, labels);

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Gmail label create conflict for '{fullPath}', but re-list found no exact match. " +
                $"Near candidates: [{string.Join("; ", near)}]");
        }

        throw CreateAmbiguousException(fullPath, matches, labels);
    }

    private static InvalidOperationException CreateAmbiguousException(
        string fullPath,
        IReadOnlyList<Label> matches,
        IEnumerable<Label>? allLabels)
    {
        var matchDiag = matches
            .Select(label => $"'{label.Name}'(id={label.Id},len={label.Name!.Length})")
            .Take(12);
        return new InvalidOperationException(
            $"Gmail label path '{fullPath}' is ambiguous: {matches.Count} labels normalize to the same canonical name. " +
            $"Matches: [{string.Join("; ", matchDiag)}]. " +
            $"Near candidates: [{string.Join("; ", FormatNearCandidates(fullPath, allLabels))}]");
    }

    private static IEnumerable<string> FormatNearCandidates(string fullPath, IEnumerable<Label>? labels)
    {
        var parentPrefix = fullPath.Contains('/')
            ? fullPath[..(fullPath.LastIndexOf('/') + 1)]
            : fullPath + "/";

        return (labels ?? [])
            .Where(label =>
                !string.IsNullOrWhiteSpace(label.Name)
                && (label.Name.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase)
                    || NamesMatch(label.Name, fullPath)))
            .Select(label => $"'{label.Name}'(id={label.Id},len={label.Name!.Length})")
            .Take(12);
    }
}
