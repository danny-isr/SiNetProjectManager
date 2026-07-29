using System.IO;
using Microsoft.Web.WebView2.Core;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.App.Wpf.WebViewHosting;

/// <summary>
/// Minimal shared WebView2 profile helper for New System surfaces (ACC viewer).
/// Does not pull V2 <c>WebView2Helper</c> (Gmail/downloads/calendar).
/// Namespace is <c>WebViewHosting</c> (not <c>WebView2</c>) to avoid shadowing
/// <see cref="Microsoft.Web.WebView2.Wpf.WebView2"/>.
/// </summary>
public static class WebView2SharedEnvironment
{
    /// <summary>Chrome UA spoof — Autodesk/ACC rejects some embedded-browser UAs.</summary>
    public const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    public const string SharedUserDataFolderName = "Default";

    /// <summary>
    /// Base folder under LocalAppData. Full profile:
    /// <c>%LOCALAPPDATA%\SiNet\WebView2UserData\Default</c>.
    /// </summary>
    public static string DefaultUserDataBasePath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiNet",
            "WebView2UserData");

    public static async Task<CoreWebView2Environment> CreateSharedAsync(
        string source,
        IAppLogger? logger = null,
        string? userDataBasePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var basePath = string.IsNullOrWhiteSpace(userDataBasePath)
            ? DefaultUserDataBasePath
            : userDataBasePath.Trim();
        var userDataFolder = Path.Combine(basePath, SharedUserDataFolderName);
        Directory.CreateDirectory(userDataFolder);

        logger?.Info($"[WebView2][SharedProfile] source={source} folder={userDataFolder}");

        cancellationToken.ThrowIfCancellationRequested();
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder)
            .ConfigureAwait(true);
    }
}
