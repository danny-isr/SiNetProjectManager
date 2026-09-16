namespace SiNet.Application.Projects;

/// <summary>
/// Numeric sort key for the formatted project-number display string.
/// Does not change stored <c>Project.Number</c>.
/// </summary>
public static class ProjectNumberSort
{
    /// <summary>
    /// Missing / non-finite values sort before every real number when ascending
    /// (and after when descending).
    /// </summary>
    public static long ToSortKey(float? number)
    {
        if (number is not float value || float.IsNaN(value) || float.IsInfinity(value))
            return long.MinValue;

        var rounded = Math.Round((double)value);
        if (rounded >= long.MaxValue)
            return long.MaxValue;
        if (rounded <= long.MinValue)
            return long.MinValue;
        return (long)rounded;
    }

    public static int CompareKeys(long left, long right) => left.CompareTo(right);

    public static IReadOnlyList<T> OrderByKey<T>(
        IEnumerable<T> items,
        Func<T, long> sortKey,
        bool descending)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sortKey);

        return descending
            ? items.OrderByDescending(sortKey).ToList()
            : items.OrderBy(sortKey).ToList();
    }
}
