using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SiNet.App.Wpf.Infrastructure;

namespace SiNet.App.Wpf.Inbox;

/// <summary>
/// Small async-aware <see cref="ICommand"/> for the vertical-slice UI. Prevents re-entrancy while
/// an operation is running and surfaces <see cref="CanExecuteChanged"/> for button enablement.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _execute().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppErrorReporter.Report(ex, "AsyncRelayCommand");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(RaiseCanExecuteChangedCore, DispatcherPriority.Normal);
            return;
        }

        RaiseCanExecuteChangedCore();
    }

    private void RaiseCanExecuteChangedCore() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Parameterized async <see cref="ICommand"/> for row-scoped actions (e.g. context menu on email list items).
/// </summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly bool _allowConcurrentParameters;
    private bool _isExecuting;

    public AsyncRelayCommand(
        Func<T?, Task> execute,
        Func<T?, bool>? canExecute = null,
        bool allowConcurrentParameters = false)
    {
        _execute = execute;
        _canExecute = canExecute;
        _allowConcurrentParameters = allowConcurrentParameters;
    }

    public bool CanExecute(object? parameter)
    {
        if (!_allowConcurrentParameters && _isExecuting)
        {
            return false;
        }

        return _canExecute?.Invoke(parameter is T t ? t : default) ?? true;
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        var typed = parameter is T t ? t : default;

        try
        {
            if (!_allowConcurrentParameters)
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
            }

            await _execute(typed).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppErrorReporter.Report(ex, "AsyncRelayCommand");
        }
        finally
        {
            if (!_allowConcurrentParameters)
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(RaiseCanExecuteChangedCore, DispatcherPriority.Normal);
            return;
        }

        RaiseCanExecuteChangedCore();
    }

    private void RaiseCanExecuteChangedCore() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
