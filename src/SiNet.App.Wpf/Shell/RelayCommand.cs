using System.Windows.Input;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Minimal synchronous <see cref="ICommand"/> for the shell menu (mirrors the vertical-slice
/// <c>AsyncRelayCommand</c> style). Used by <see cref="NewShellMenuItem"/> to open a migrated surface
/// on click while respecting an optional <c>canExecute</c> gate (e.g. an unavailable item).
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute(parameter);
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
