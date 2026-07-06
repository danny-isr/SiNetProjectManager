namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Load lifecycle for the standalone email list component.</summary>
public enum EmailListLoadState
{
    Idle,
    Loading,
    Loaded,
    PartialFailure,
    Error,
    NoResults,
}
