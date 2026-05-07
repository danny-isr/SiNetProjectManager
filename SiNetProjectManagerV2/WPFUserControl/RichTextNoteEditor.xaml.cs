using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SiNetSQL.MVVM;
using static SiNetSQL.Services.InspectionSync.RichTextCodec;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// Rich text note editor with live color preview.
/// Display mode: colored TextBlock with <see cref="Run"/> Inlines.
/// Edit mode: raw markup TextBox with context-menu color application.
/// Uses <see cref="RichTextCodec"/> for encoding/decoding and conflict-aware color resolution.
/// </summary>
public partial class RichTextNoteEditor : UserControl
{
    private bool _isEditing;
    private bool _suppressCallback;

    private static readonly SolidColorBrush RedBrush = CreateFrozenBrush(0xD3, 0x2F, 0x2F);
    private static readonly SolidColorBrush BlueBrush = CreateFrozenBrush(0x15, 0x65, 0xC0);
    private static readonly SolidColorBrush GreenBrush = CreateFrozenBrush(0x2E, 0x7D, 0x32);
    private static readonly SolidColorBrush GrayBrush = CreateFrozenBrush(0x75, 0x75, 0x75);
    private static readonly SolidColorBrush DefaultBrush = CreateFrozenBrush(0x00, 0x00, 0x00);

    #region DependencyProperty – EncodedText

    public static readonly DependencyProperty EncodedTextProperty =
        DependencyProperty.Register(
            nameof(EncodedText),
            typeof(string),
            typeof(RichTextNoteEditor),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnEncodedTextChanged,
                null,
                false,
                System.Windows.Data.UpdateSourceTrigger.PropertyChanged));

    public string EncodedText
    {
        get => (string)GetValue(EncodedTextProperty);
        set => SetValue(EncodedTextProperty, value);
    }

    private static void OnEncodedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (RichTextNoteEditor)d;
        if (editor._suppressCallback) return;

        if (!editor._isEditing)
            editor.RenderPreview();
    }

    #endregion

    #region DependencyProperty – NoteStatus

    public static readonly DependencyProperty NoteStatusProperty =
        DependencyProperty.Register(
            nameof(NoteStatus),
            typeof(string),
            typeof(RichTextNoteEditor),
            new PropertyMetadata(string.Empty, OnNoteStatusChanged));

    public string NoteStatus
    {
        get => (string)GetValue(NoteStatusProperty);
        set => SetValue(NoteStatusProperty, value);
    }

    private static void OnNoteStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (RichTextNoteEditor)d;
        if (!editor._isEditing)
            editor.RenderPreview();
    }

    #endregion

    #region DependencyProperty – IsReadOnly

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(RichTextNoteEditor),
            new PropertyMetadata(false, OnIsReadOnlyChanged));

    /// <summary>
    /// When true, the editor cannot enter edit mode and the inner TextBox is read-only.
    /// Used to lock notes after a report is sent.
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (RichTextNoteEditor)d;
        var isReadOnly = (bool)e.NewValue;
        editor.EditBox.IsReadOnly = isReadOnly;
        // If currently editing and we just became read-only, exit edit mode safely.
        if (isReadOnly && editor._isEditing)
        {
            editor._isEditing = false;
            editor.EditBox.Visibility = Visibility.Collapsed;
            editor.DisplayBorder.Visibility = Visibility.Visible;
            editor.RenderPreview();
        }
    }

    #endregion

    #region RoutedEvent – EditCompleted

    public static readonly RoutedEvent EditCompletedEvent =
        EventManager.RegisterRoutedEvent(
            "EditCompleted",
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(RichTextNoteEditor));

    public event RoutedEventHandler EditCompleted
    {
        add => AddHandler(EditCompletedEvent, value);
        remove => RemoveHandler(EditCompletedEvent, value);
    }

    #endregion

    #region RoutedEvent – AiReviewRequested

    /// <summary>
    /// Routed event args carrying the AI review type and the suggested replacement text.
    /// </summary>
    public sealed class AiReviewRequestedEventArgs(RoutedEvent routedEvent, object source, string reviewType, string suggestedText)
        : RoutedEventArgs(routedEvent, source)
    {
        /// <summary>"grammar" or "rephrase".</summary>
        public string ReviewType { get; } = reviewType;

        /// <summary>The AI-suggested replacement text to apply.</summary>
        public string SuggestedText { get; } = suggestedText;
    }

    /// <summary>WPF-compatible delegate for <see cref="AiReviewRequestedEvent"/>.</summary>
    public delegate void AiReviewRequestedEventHandler(object sender, AiReviewRequestedEventArgs e);

    public static readonly RoutedEvent AiReviewRequestedEvent =
        EventManager.RegisterRoutedEvent(
            "AiReviewRequested",
            RoutingStrategy.Bubble,
            typeof(AiReviewRequestedEventHandler),
            typeof(RichTextNoteEditor));

    public event AiReviewRequestedEventHandler AiReviewRequested
    {
        add => AddHandler(AiReviewRequestedEvent, value);
        remove => RemoveHandler(AiReviewRequestedEvent, value);
    }

    /// <summary>
    /// Builds the AI context menu dynamically based on cached <see cref="NoteTreeItem"/> AI results.
    /// </summary>
    private void AiContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();

        if (DataContext is not NoteTreeItem note || string.IsNullOrWhiteSpace(note.NoteText))
        {
            menu.Items.Add(new MenuItem { Header = "🤖 אין טקסט לבדיקה", IsEnabled = false });
            return;
        }

        if (note.AiReviewInProgress)
        {
            menu.Items.Add(new MenuItem { Header = "⏳ AI בודק...", IsEnabled = false });
            return;
        }

        // Check if results are stale (text changed since review)
        var (currentPlain, _) = Parse(note.NoteText ?? "");
        if (note.AiOriginalText is null || note.AiOriginalText != currentPlain)
        {
            menu.Items.Add(new MenuItem { Header = "🤖 AI לא זמין — ערוך וצא כדי להפעיל", IsEnabled = false });
            return;
        }

        // Grammar result
        if (note.AiGrammarResult is not null)
        {
            if (note.HasAiGrammarChanges)
            {
                var grammarItem = new MenuItem
                {
                    Header = CreateAiMenuContent("🤖 תיקון תחבירי:", note.AiGrammarResult),
                    Tag = "grammar"
                };
                grammarItem.Click += AiApply_Click;
                menu.Items.Add(grammarItem);
            }
            else
            {
                menu.Items.Add(new MenuItem { Header = "✅ אין שגיאות תחביריות", IsEnabled = false });
            }
        }

        // Rephrase result
        if (note.AiRephraseResult is not null)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());

            var rephraseItem = new MenuItem
            {
                Header = CreateAiMenuContent("🤖 ניסוח מחדש:", note.AiRephraseResult),
                Tag = "rephrase"
            };
            rephraseItem.Click += AiApply_Click;
            menu.Items.Add(rephraseItem);
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "🤖 לא התקבלו תוצאות", IsEnabled = false });
    }

    /// <summary>Applies the selected AI suggestion by raising <see cref="AiReviewRequestedEvent"/>.</summary>
    private void AiApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string reviewType }) return;
        if (DataContext is not NoteTreeItem note) return;

        var suggestedText = reviewType == "grammar" ? note.AiGrammarResult : note.AiRephraseResult;
        if (string.IsNullOrWhiteSpace(suggestedText)) return;

        RaiseEvent(new AiReviewRequestedEventArgs(AiReviewRequestedEvent, this, reviewType, suggestedText));
    }

    /// <summary>
    /// Creates a multi-line visual header for an AI context menu item.
    /// Shows a bold title line followed by the full suggestion text with wrapping.
    /// </summary>
    private static StackPanel CreateAiMenuContent(string title, string bodyText)
    {
        var panel = new StackPanel { MaxWidth = 350 };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        panel.Children.Add(new TextBlock
        {
            Text = bodyText,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
        });

        return panel;
    }

    #endregion

    public RichTextNoteEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => RenderPreview();
    }

    #region Display (colored Inlines)

    private void RenderPreview()
    {
        DisplayBlock.Inlines.Clear();

        var encoded = EncodedText;
        if (string.IsNullOrEmpty(encoded))
            return;

        var (plainText, runs) = Parse(encoded);

        // RecurringFailed override: entire text as Bold+Red regardless of markup
        if (string.Equals(NoteStatus, "RecurringFailed", StringComparison.Ordinal))
        {
            if (!string.IsNullOrEmpty(plainText))
            {
                DisplayBlock.Inlines.Add(new Run(plainText)
                {
                    Foreground = RedBrush,
                    FontWeight = FontWeights.Bold
                });
            }
            return;
        }

        if (runs.Count == 0)
        {
            DisplayBlock.Inlines.Add(new Run(plainText));
            return;
        }

        int cursor = 0;
        foreach (var run in runs.OrderBy(r => r.StartIndex))
        {
            // Plain text before this styled run
            if (run.StartIndex > cursor)
            {
                DisplayBlock.Inlines.Add(
                    new Run(plainText[cursor..run.StartIndex]));
            }

            int end = Math.Min(run.StartIndex + run.Length, plainText.Length);
            var styled = new Run(plainText[run.StartIndex..end])
            {
                Foreground = BrushFor(run.Color),
                FontWeight = run.Bold ? FontWeights.Bold : FontWeights.Normal
            };
            DisplayBlock.Inlines.Add(styled);
            cursor = end;
        }

        // Trailing plain text
        if (cursor < plainText.Length)
            DisplayBlock.Inlines.Add(new Run(plainText[cursor..]));
    }

    private static SolidColorBrush BrushFor(RichTextColor color) => color switch
    {
        RichTextColor.Red => RedBrush,
        RichTextColor.Blue => BlueBrush,
        RichTextColor.Green => GreenBrush,
        RichTextColor.Gray => GrayBrush,
        _ => DefaultBrush
    };

    #endregion

    #region Edit-mode switching

    private void DisplayBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            EnterEditMode();
    }

    private void EnterEditMode()
    {
        if (_isEditing) return;
        if (IsReadOnly) return;
        _isEditing = true;

        EditBox.Text = EncodedText ?? string.Empty;
        DisplayBorder.Visibility = Visibility.Collapsed;
        EditBox.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            EditBox.Focus();
            EditBox.CaretIndex = EditBox.Text.Length;
        });
    }

    private void ExitEditMode()
    {
        if (!_isEditing) return;
        _isEditing = false;

        // Push final text to the DP
        _suppressCallback = true;
        try
        {
            EncodedText = EditBox.Text ?? string.Empty;
        }
        finally { _suppressCallback = false; }

        EditBox.Visibility = Visibility.Collapsed;
        DisplayBorder.Visibility = Visibility.Visible;

        RenderPreview();
        System.Diagnostics.Debug.WriteLine("[AI Flow] RichTextNoteEditor.ExitEditMode → raising EditCompleted");
        RaiseEvent(new RoutedEventArgs(EditCompletedEvent, this));
    }

    #endregion

    #region EditBox event handlers

    private void EditBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isEditing) return;

        _suppressCallback = true;
        try
        {
            EncodedText = EditBox.Text ?? string.Empty;
        }
        finally { _suppressCallback = false; }
    }

    private void EditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Defer: the ContextMenu opening steals focus briefly
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (!_isEditing) return;

            // Still focused (e.g. ContextMenu re-focused us) → stay in edit mode
            if (EditBox.IsKeyboardFocusWithin) return;

            // ContextMenu is open → stay in edit mode
            if (EditBox.ContextMenu is { IsOpen: true }) return;

            ExitEditMode();
        });
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (!_isEditing) return;

        // Re-focus the EditBox after context menu closes (unless a color was applied, which does its own focus)
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_isEditing && EditBox.Visibility == Visibility.Visible)
                EditBox.Focus();
        });
    }

    #endregion

    #region Color application (conflict-aware)

    private void ColorText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tagCode }) return;

        int selStart = EditBox.SelectionStart;
        int selLength = EditBox.SelectionLength;
        if (selLength == 0) return;

        var encoded = EditBox.Text ?? string.Empty;

        // Parse with position map to translate encoded→plain indices
        var (plainText, existingRuns, map) = ParseWithMap(encoded);

        int plainStart = MapEncodedToPlain(map, selStart, searchForward: true);
        int plainEnd = MapEncodedToPlain(map, selStart + selLength, searchForward: false);
        int plainLength = Math.Max(0, plainEnd - plainStart);

        if (plainLength == 0) return;

        // Determine the new color & bold from tag code
        var (newBold, newColor) = tagCode == "0"
            ? (false, RichTextColor.Default)
            : ParseCode(tagCode);

        // Resolve conflicts and produce updated runs
        var updatedRuns = ApplyColor(existingRuns, plainStart, plainLength, newColor, newBold);

        // Re-encode
        var newEncoded = Encode(plainText, updatedRuns);

        _suppressCallback = true;
        try
        {
            EditBox.Text = newEncoded;
            EncodedText = newEncoded;
        }
        finally { _suppressCallback = false; }

        // Restore caret near the end of the applied range
        EditBox.CaretIndex = Math.Min(EditBox.Text.Length, selStart + selLength);
        EditBox.Focus();
    }

    #endregion

    #region Helpers

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    #endregion
}
