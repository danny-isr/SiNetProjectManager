using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// A data-driven entry in the new shell menu (see <c>docs/APP_SHELL.md</c> §6/§7).
/// May be a <b>leaf</b> (opens a surface) or a <b>group</b> (submenu with <see cref="Children"/>),
/// matching the legacy top-menu / submenu pattern.
/// </summary>
public sealed class NewShellMenuItem
{
    private readonly Action? _open;

    /// <summary>Creates a leaf menu item that opens a migrated surface.</summary>
    public NewShellMenuItem(string title, Action open, string? description = null, bool isAvailable = true)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        Description = description;
        IsAvailable = isAvailable;
        Children = new ObservableCollection<NewShellMenuItem>();
        // UIA InvokePattern / FlaUI may raise Command off the WPF UI thread; marshal so
        // Window.Show and in-shell NavigateTo always run on the dispatcher.
        OpenCommand = new RelayCommand(_ => InvokeOpen(), _ => IsAvailable);
    }

    private NewShellMenuItem(string title, IEnumerable<NewShellMenuItem> children, string? description)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        _open = null;
        Description = description;
        IsAvailable = true;
        Children = new ObservableCollection<NewShellMenuItem>(children);
        OpenCommand = null;
    }

    /// <summary>Creates a top-level (or nested) group whose children appear as a submenu.</summary>
    public static NewShellMenuItem Group(string title, IEnumerable<NewShellMenuItem> children, string? description = null)
        => new(title, children, description);

    /// <summary>Display label shown in the shell menu.</summary>
    public string Title { get; }

    /// <summary>Optional secondary text / tooltip.</summary>
    public string? Description { get; }

    /// <summary>
    /// Whether the item's surface is available. Unavailable leaves are shown but disabled.
    /// Groups are always available when present.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>Submenu items. Empty for leaf actions.</summary>
    public ObservableCollection<NewShellMenuItem> Children { get; }

    /// <summary>True when this item is a submenu group.</summary>
    public bool IsGroup => Children.Count > 0;

    /// <summary>Command for leaf items; <see langword="null"/> for groups (submenu only).</summary>
    public ICommand? OpenCommand { get; }

    /// <summary>Opens the migrated surface. No-op for groups.</summary>
    public void Open() => InvokeOpen();

    private void InvokeOpen()
    {
        if (_open is null)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _open();
            return;
        }

        dispatcher.Invoke(_open);
    }
}
