namespace SiNet.Application.Common;

/// <summary>
/// Options for an explicit connector sign-in initiated by the user (e.g. Email Workbench "Connect").
/// </summary>
public sealed record ConnectorLoginOptions(
    bool SkipSilentRestore = false,
    bool PromptAccountSelection = false);
