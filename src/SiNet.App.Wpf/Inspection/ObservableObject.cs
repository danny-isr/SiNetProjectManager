using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base for the new Inspection screen's view models.
/// Mirrors the lightweight pattern used by the Inbox slice (no MVVM toolkit dependency) so the new
/// shell stays self-contained while the Inspection screen is rebuilt area by area.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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
