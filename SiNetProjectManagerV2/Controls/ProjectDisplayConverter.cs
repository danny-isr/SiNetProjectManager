using System.Globalization;
using System.Windows.Data;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.Controls;

/// <summary>
/// Converts a Project to compact display format: [Number] Title | Company
/// Used in SearchableProjectSelector for concise dropdown items.
/// </summary>
public class ProjectDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Project project)
        {
            // Handle Number - use various fallbacks
            string numberDisplay = GetNumberDisplay(project);

            var title = project.Title ?? string.Empty;
            var company = project.Company?.Title;
            var place = project.Place?.Title;

            // Format: [123] Project Title | Company Name (City)
            var result = $"[{numberDisplay}] {title}";

            if (!string.IsNullOrEmpty(company) && !string.IsNullOrEmpty(place))
            {
                return $"{result} | {company} ({place})";
            }
            else if (!string.IsNullOrEmpty(company))
            {
                return $"{result} | {company}";
            }
            else if (!string.IsNullOrEmpty(place))
            {
                return $"{result} ({place})";
            }

            return result;
        }

        // Fallback for non-Project items
        return value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Gets the display string for the project number.
    /// Tries multiple sources in order of preference.
    /// </summary>
    private static string GetNumberDisplay(Project project)
    {
        // 1. Use Number if it's a valid positive value
        if (project.Number.HasValue && project.Number.Value > 0)
        {
            // Format without decimals for whole numbers, with 1 decimal otherwise
            var num = project.Number.Value;
            return num == Math.Floor(num) 
                ? num.ToString("0", CultureInfo.InvariantCulture) 
                : num.ToString("0.#", CultureInfo.InvariantCulture);
        }

        // 2. Try to extract from NameAndNumber (format: "123 - Title" or "123.5 - Title")
        if (!string.IsNullOrEmpty(project.NameAndNumber))
        {
            var parts = project.NameAndNumber.Split(" - ", 2, StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                return parts[0];
            }
        }

        // 3. Use ID as last resort (always valid)
        return project.Id.ToString(CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("ProjectDisplayConverter does not support ConvertBack.");
    }
}
