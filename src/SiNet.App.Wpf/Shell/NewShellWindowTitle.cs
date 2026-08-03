using System.Reflection;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shell;

/// <summary>Formats the New System shell window title from the shared project context.</summary>
public static class NewShellWindowTitle
{
    public const string BrandTitle = "שיא חדש — מנהל פרויקטים";

    /// <summary>
    /// Default OS window title when no project is selected.
    /// Includes the SiNet.App.Wpf package version (<c>AssemblyInformationalVersion</c> / csproj <c>Version</c>).
    /// </summary>
    public static string BaseTitle { get; } = $"{BrandTitle} — {ResolveAppVersion()}";

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

    /// <summary>
    /// Reads the host assembly version used for publish (prefer InformationalVersion; strip SourceLink <c>+sha</c>).
    /// </summary>
    internal static string ResolveAppVersion()
    {
        var asm = typeof(NewShellWindowTitle).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
