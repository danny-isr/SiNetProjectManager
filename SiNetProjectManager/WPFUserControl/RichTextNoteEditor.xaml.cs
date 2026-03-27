using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static SiNetSQL.Services.InspectionSync.RichTextCodec;

namespace SiNetProjectManager.WPFUserControl;

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
