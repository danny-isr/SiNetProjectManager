using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// View model for the first-visible <see cref="StartupModeSelectionWindow"/> (see
/// <c>docs/APP_SHELL.md</c> §2/§3). Startup mode is a <b>user decision</b>, not a splash: this view
/// model has <b>no timer, no delay, and no auto-selection</b> — it simply holds the chosen
/// <see cref="StartupMode"/> and waits for the user to confirm.
/// <para>
/// The default selection is <see cref="StartupMode.NewSystem"/> (the refactored app is the primary
/// path now); the user can explicitly switch to <see cref="StartupMode.Legacy"/>. Closing the dialog
/// with X is treated by the window as cancel/exit — it is never silently coerced to Legacy.
/// </para>
/// It is deliberately WPF-window-free so the default/routing behavior can be unit-tested without a UI.
/// </summary>
public sealed class StartupModeSelectionViewModel : INotifyPropertyChanged
{
    /// <summary>The mode selected by default when the chooser first appears: New System.</summary>
    public const StartupMode DefaultMode = StartupMode.NewSystem;

    private StartupMode _selectedMode = DefaultMode;

    /// <summary>
    /// Creates the view model with the default selection (<see cref="DefaultMode"/> = New System) and
    /// the Continue command. No timer is started — the choice waits for the user indefinitely.
    /// </summary>
    public StartupModeSelectionViewModel()
    {
        ContinueCommand = new RelayCommand(_ => Confirm());
    }

    /// <summary>Header prompt shown above the options.</summary>
    public string Prompt => "בחר מצב הפעלה:";

    /// <summary>Label for the New System option (recommended/default).</summary>
    public string NewSystemLabel => "מערכת חדשה";

    /// <summary>Label for the Legacy option.</summary>
    public string LegacyLabel => "מערכת ישנה";

    /// <summary>Label for the confirm button.</summary>
    public string ContinueLabel => "המשך";

    /// <summary>The currently selected startup mode. Defaults to <see cref="StartupMode.NewSystem"/>.</summary>
    public StartupMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetField(ref _selectedMode, value))
            {
                // Keep the two radio-bound flags in sync so either data path (enum or bool) works.
                OnPropertyChanged(nameof(IsNewSystemSelected));
                OnPropertyChanged(nameof(IsLegacySelected));
            }
        }
    }

    /// <summary>
    /// Two-way radio binding for the New System option. Setting it to <see langword="true"/> selects
    /// New System; WPF sets the other radio's flag to <see langword="false"/> automatically.
    /// </summary>
    public bool IsNewSystemSelected
    {
        get => _selectedMode == StartupMode.NewSystem;
        set
        {
            if (value)
            {
                SelectedMode = StartupMode.NewSystem;
            }
        }
    }

    /// <summary>Two-way radio binding for the Legacy option.</summary>
    public bool IsLegacySelected
    {
        get => _selectedMode == StartupMode.Legacy;
        set
        {
            if (value)
            {
                SelectedMode = StartupMode.Legacy;
            }
        }
    }

    /// <summary>
    /// <see langword="true"/> once the user confirmed a choice (pressed Continue). Remains
    /// <see langword="false"/> if the dialog is cancelled/closed — so the host can exit rather than
    /// silently defaulting to Legacy.
    /// </summary>
    public bool Confirmed { get; private set; }

    /// <summary>Command bound to the Continue button; confirms the current <see cref="SelectedMode"/>.</summary>
    public ICommand ContinueCommand { get; }

    /// <summary>Raised when the user confirms, so the window can close with a positive dialog result.</summary>
    public event EventHandler? ConfirmRequested;

    /// <summary>
    /// Confirms the current selection. Idempotent; sets <see cref="Confirmed"/> and raises
    /// <see cref="ConfirmRequested"/> so the hosting window closes with success.
    /// </summary>
    public void Confirm()
    {
        Confirmed = true;
        ConfirmRequested?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
