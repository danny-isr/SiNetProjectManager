using System.Windows;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Theme;

/// <summary>
/// Merges native theme XAML dictionaries into <see cref="Application.Current"/> resources.
/// Required when the production host is <c>SiNetProjectManagerV2</c> (its App.xaml does not include
/// <c>SiNet.App.Wpf/App.xaml</c> merged dictionaries).
/// </summary>
public static class ThemeResourceLoader
{
    private static bool _merged;

    private static readonly string[] DictionaryPaths =
    [
        "Theme/TypographyResources.xaml",
        "Theme/BrushResources.xaml",
        "Theme/ThemeStyles.xaml",
    ];

    public static void EnsureApplicationResourcesMerged()
    {
        if (_merged)
        {
            return;
        }

        if (System.Windows.Application.Current?.Resources is not ResourceDictionary root)
        {
            return;
        }

        if (root.Contains(ThemeResourceKeys.TextNormalStyle))
        {
            _merged = true;
            return;
        }

        var assemblyName = typeof(ThemeResourceLoader).Assembly.GetName().Name;
        foreach (var relativePath in DictionaryPaths)
        {
            var uri = new Uri($"/{assemblyName};component/{relativePath}", UriKind.Relative);
            var dictionary = (ResourceDictionary)System.Windows.Application.LoadComponent(uri);
            root.MergedDictionaries.Add(dictionary);
        }

        _merged = true;
    }

    /// <summary>Test hook — allows re-running merge in isolated Application instances.</summary>
    public static void ResetForTests() => _merged = false;
}
