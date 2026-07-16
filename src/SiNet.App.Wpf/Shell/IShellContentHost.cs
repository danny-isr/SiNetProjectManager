namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Navigation hub for the New System shell content area. Surfaces that should live inside the main
/// window (rather than as popup <see cref="System.Windows.Window"/>s) call
/// <see cref="NavigateTo"/> after the shell has been attached via <see cref="Attach"/>.
/// Mirrors the legacy <c>MainWindow.NavigateToView</c> role.
/// </summary>
public interface IShellContentHost
{
    /// <summary>Wires the live shell view model. Called once when the shell window is created.</summary>
    void Attach(NewShellViewModel shell);

    /// <summary>
    /// Shows <paramref name="content"/> in the shell content host. Pass <see langword="null"/> to
    /// clear. Does not dispose previous content — callers that need singleton reuse keep their own cache.
    /// </summary>
    void NavigateTo(object? content);

    /// <summary>True after <see cref="Attach"/> has been called for the current shell instance.</summary>
    bool IsAttached { get; }
}
