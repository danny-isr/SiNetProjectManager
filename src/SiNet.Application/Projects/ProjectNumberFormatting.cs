using System.Globalization;

namespace SiNet.Application.Projects;

/// <summary>
/// Shared display formatting for the legacy <c>Project.Number</c> float column.
/// Authority used by project selector / dashboard / task cards — keep one implementation.
/// </summary>
public static class ProjectNumberFormatting
{
    /// <summary>
    /// Formats the legacy <c>float?</c> project number as the selector's display string: an integer when
    /// there is no fractional part (e.g. <c>1042</c>), otherwise an invariant round-trip value. Empty when
    /// the number is missing.
    /// </summary>
    public static string Format(float? number)
    {
        if (number is not float value)
        {
            return string.Empty;
        }

        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 0.0001f
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
    }
}
