namespace SiNet.Infrastructure.Google;

/// <summary>
/// Configuration for the native Gmail integration. Kept free of hard-coded paths so the
/// host can point the gateway at its own, independent OAuth client and token store.
/// </summary>
public sealed class GmailOptions
{
    /// <summary>
    /// Absolute path to the OAuth <c>client_secret.json</c> downloaded from the Google Cloud
    /// console. When missing/empty, the gateway stays unauthenticated and reads return empty.
    /// </summary>
    public string? ClientSecretsPath { get; set; }

    /// <summary>
    /// Folder used by the OAuth <c>FileDataStore</c> to persist the refresh token. This is the
    /// new stack's own store and is intentionally separate from the legacy service's token folder.
    /// Environment variables are expanded.
    /// </summary>
    public string TokenStorePath { get; set; } = "sinet-google-token";

    /// <summary>OAuth application name reported to the Google API initializers.</summary>
    public string ApplicationName { get; set; } = "SiNet";

    /// <summary>
    /// Root Gmail label under which projects are filed. Project emails live at
    /// <c>{RootLabel}/{location}/{projectName}</c>. Defaults to the legacy root.
    /// </summary>
    public string RootLabel { get; set; } = "פרויקטים_משרד";

    /// <summary>
    /// Default Gmail search query for the general mailbox list (legacy: <c>label:INBOX</c>).
    /// </summary>
    public string DefaultMailboxQuery { get; set; } = "label:INBOX";

    /// <summary>
    /// When <c>true</c>, the provider may open a browser for interactive OAuth consent if no
    /// usable token exists yet. Defaults to <c>false</c> so application startup never triggers a
    /// surprise consent prompt; a dedicated "Connect Google" action can enable it later.
    /// </summary>
    public bool AllowInteractiveSignIn { get; set; }
}
