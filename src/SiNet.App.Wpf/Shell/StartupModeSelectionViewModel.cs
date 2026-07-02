namespace SiNet.App.Wpf.Shell;

/// <summary>
/// View model for <see cref="StartupModeSelectionWindow"/> (see <c>docs/APP_SHELL.md</c> §3).
/// </summary>
public sealed class StartupModeSelectionViewModel : Inspection.ObservableObject
{
    private StartupMode _selectedMode = StartupMode.NewSystem;

    public StartupMode SelectedMode
    {
        get => _selectedMode;
        set => SetField(ref _selectedMode, value);
    }

    public bool IsNewSystemSelected
    {
        get => SelectedMode == StartupMode.NewSystem;
        set
        {
            if (value)
            {
                SelectedMode = StartupMode.NewSystem;
            }
        }
    }

    public bool IsLegacySelected
    {
        get => SelectedMode == StartupMode.Legacy;
        set
        {
            if (value)
            {
                SelectedMode = StartupMode.Legacy;
            }
        }
    }
}
