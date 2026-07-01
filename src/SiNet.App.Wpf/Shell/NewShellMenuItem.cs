using System.Windows.Input;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// A single, data-driven entry in the new shell menu (see <c>docs/APP_SHELL.md</c> §6/§7).
/// <para>
/// The shell menu is a list of these descriptors, built <b>only</b> from surfaces that already exist
/// in the refactored stack. A menu item carries no business logic: it only exposes a display label and
/// an <see cref="Open"/> action that resolves the target surface from DI / a factory and shows it. The
/// shell never mutates workflow from here (see <c>AI_DEVELOPMENT_GUIDE.md</c> rule 11).
/// </para>
/// </summary>
public sealed class NewShellMenuItem
{
    private readonly Action _open;

    /// <summary>
    /// Creates a migrated-surface menu item.
    /// </summary>
    /// <param name="title">Display label (he-IL).</param>
    /// <param name="open">Action that opens the migrated surface (resolve from DI / factory).</param>
    /// <param name="description">Optional secondary text / tooltip.</param>
    /// <param name="isAvailable">
    /// <see langword="false"/> to show the item as present-but-disabled (e.g. a documented placeholder
    /// that is not yet implemented). Defaults to <see langword="true"/>.
    /// </param>
    public NewShellMenuItem(string title, Action open, string? description = null, bool isAvailable = true)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        Description = description;
        IsAvailable = isAvailable;
        OpenCommand = new RelayCommand(_ => _open(), _ => IsAvailable);
    }

    /// <summary>Display label shown in the shell menu.</summary>
    public string Title { get; }

    /// <summary>Optional secondary text / tooltip.</summary>
    public string? Description { get; }

    /// <summary>
    /// Whether the item's surface is available in the new stack. Unavailable items are shown but
    /// disabled so the menu documents what is coming without throwing.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>Command bound by the shell menu; invokes <see cref="Open"/> when available.</summary>
    public ICommand OpenCommand { get; }

    /// <summary>Opens the migrated surface. No-op safety is the caller's responsibility.</summary>
    public void Open() => _open();
}
