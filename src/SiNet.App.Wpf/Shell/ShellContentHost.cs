namespace SiNet.App.Wpf.Shell;

/// <inheritdoc />
public sealed class ShellContentHost : IShellContentHost
{
    private NewShellViewModel? _shell;

    /// <inheritdoc />
    public bool IsAttached => _shell is not null;

    /// <inheritdoc />
    public void Attach(NewShellViewModel shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    /// <inheritdoc />
    public void NavigateTo(object? content)
    {
        if (_shell is null)
        {
            throw new InvalidOperationException(
                "Shell content host is not attached. Create the New System shell before navigating.");
        }

        _shell.CurrentContent = content;
    }
}
