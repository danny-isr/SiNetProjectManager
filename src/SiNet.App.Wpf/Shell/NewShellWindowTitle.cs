using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shell;

/// <summary>Formats the New System shell window title from the shared project context.</summary>
public static class NewShellWindowTitle
{
    public const string BaseTitle = "שיא חדש — מנהל פרויקטים";

    public static string Format(ProjectSummaryDto? project)
    {
        if (project is null)
        {
            return BaseTitle;
        }

        var segments = new List<string> { BaseTitle };

        var number = project.ProjectNumber?.Trim();
        if (!string.IsNullOrEmpty(number))
        {
            segments.Add(number);
        }

        var name = project.ProjectName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            segments.Add(name);
        }

        return segments.Count == 1 ? BaseTitle : string.Join(" — ", segments);
    }

    public static string? FormatHeaderDisplay(ProjectSummaryDto? project)
    {
        if (project is null)
        {
            return null;
        }

        var name = project.ProjectName?.Trim();
        var number = project.ProjectNumber?.Trim();

        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(number))
        {
            return $"{name} — {number}";
        }

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return !string.IsNullOrEmpty(number) ? number : null;
    }
}
