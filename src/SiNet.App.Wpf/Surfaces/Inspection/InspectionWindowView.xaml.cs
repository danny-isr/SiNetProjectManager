using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Theme;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Window for the Inspection report surface (visual clone of legacy FloatingInspectionView).
/// Chrome + tree selection only; business logic lives in <see cref="InspectionWindowViewModel"/>.
/// </summary>
public partial class InspectionWindowView : Window
{
    /// <summary>Design/standalone constructor: shows the clone with fake design-time data.</summary>
    public InspectionWindowView()
        : this(new InspectionWindowViewModel())
    {
    }

    /// <summary>Primary constructor: binds to the supplied view model.</summary>
    public InspectionWindowView(InspectionWindowViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    /// <summary>The bound view model.</summary>
    public InspectionWindowViewModel ViewModel { get; }

    /// <summary>
    /// Task-mode entry. Prefer <see cref="ApplyContextAsync"/> when the caller can await load completion.
    /// </summary>
    public void ApplyContext(WorkSurfaceContext? context) => ViewModel.ApplyContext(context);

    /// <summary>Task-mode entry that awaits exact report load (no first/last fallback).</summary>
    public Task<bool> ApplyContextAsync(WorkSurfaceContext? context, CancellationToken cancellationToken = default)
        => ViewModel.ApplyContextAsync(context, cancellationToken);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (ViewModel.IsTaskMode)
        {
            // Task path also applies layout in WorkSurfaceLauncher before Show; keep Loaded as a
            // safety net when the window is shown without that caller.
            TaskSurfaceWindowLayout.ApplyComplementaryToWorkbench(this);
            return;
        }

        try
        {
            await ViewModel.InitializeBrowseAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[InspectionWindow] InitializeBrowse failed: {ex.Message}");
        }
    }

    private void InspectionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        ViewModel.OnTreeSelectionChanged(e.NewValue);

    private async void GeneralField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InspectionGeneralFieldItem field })
            await ViewModel.SaveGeneralFieldAsync(field).ConfigureAwait(true);
    }

    private async void AutoManualToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InspectionGeneralFieldItem field })
            await ViewModel.SaveGeneralFieldAsync(field).ConfigureAwait(true);
    }

    private async void NoteRichEditor_EditCompleted(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InspectionNoteItem note })
            return;

        await ViewModel.SaveNoteTextAsync(note).ConfigureAwait(true);
        await ViewModel.ReviewNoteAiAsync(note).ConfigureAwait(true);
    }

    private async void NoteRichEditor_AiReviewRequested(object sender, InspectionNoteRichEditor.AiReviewRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InspectionNoteItem note })
            return;

        await ViewModel.ApplyAiSuggestionAsync(note, e.ReviewType, e.SuggestedText).ConfigureAwait(true);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
