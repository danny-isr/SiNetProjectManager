namespace SiNetProjectManagerV2.Services.Email;

/// <summary>
/// INACTIVE — superseded by <c>SiNet.App.Wpf.Surfaces.Email.WebView2EmailBodyRenderer</c>.
/// Status: not registered; kept temporarily so historical V2 path references remain discoverable.
/// May return only if V2 needs a host-specific fork; otherwise delete after standalone HTML viewer verification.
/// </summary>
[Obsolete("Use SiNet.App.Wpf.Surfaces.Email.WebView2EmailBodyRenderer via DI (IEmailBodyRenderer).")]
internal static class WebView2EmailBodyRenderer
{
}
