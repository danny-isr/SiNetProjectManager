using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Lightweight rich-text note editor for the Inspection visual clone.
/// Full AI context-menu parity with legacy RichTextNoteEditor is wired via bound suggestions.
/// </summary>
public sealed class InspectionRichTextNoteEditor : RichTextBox
{
    public static readonly DependencyProperty PlainTextProperty = DependencyProperty.Register(
        nameof(PlainText),
        typeof(string),
        typeof(InspectionRichTextNoteEditor),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnPlainTextChanged));

    private bool _suppress;

    public InspectionRichTextNoteEditor()
    {
        AcceptsReturn = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        TextChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            _suppress = true;
            try
            {
                PlainText = new TextRange(Document.ContentStart, Document.ContentEnd).Text.TrimEnd('\r', '\n');
            }
            finally
            {
                _suppress = false;
            }
        };
    }

    public string PlainText
    {
        get => (string)GetValue(PlainTextProperty);
        set => SetValue(PlainTextProperty, value);
    }

    private static void OnPlainTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InspectionRichTextNoteEditor editor || editor._suppress)
        {
            return;
        }

        editor._suppress = true;
        try
        {
            var text = e.NewValue as string ?? string.Empty;
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(new Run(text)));
        }
        finally
        {
            editor._suppress = false;
        }
    }
}
