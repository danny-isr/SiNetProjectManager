using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SiNetProjectManager.Controls;

/// <summary>
/// Attached behavior for highlighting matched text in a TextBlock.
/// Uses efficient Inlines manipulation for performance.
/// </summary>
public static class HighlightBehavior
{
    #region Attached Properties

    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.RegisterAttached(
        "SourceText",
        typeof(string),
        typeof(HighlightBehavior),
        new PropertyMetadata(null, OnTextChanged));

    public static readonly DependencyProperty HighlightTextProperty = DependencyProperty.RegisterAttached(
        "HighlightText",
        typeof(string),
        typeof(HighlightBehavior),
        new PropertyMetadata(null, OnTextChanged));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.RegisterAttached(
        "HighlightBrush",
        typeof(Brush),
        typeof(HighlightBehavior),
        new PropertyMetadata(Brushes.Yellow, OnTextChanged));

    #endregion

    #region Getters/Setters

    public static string GetSourceText(DependencyObject obj) => (string)obj.GetValue(SourceTextProperty);
    public static void SetSourceText(DependencyObject obj, string value) => obj.SetValue(SourceTextProperty, value);

    public static string GetHighlightText(DependencyObject obj) => (string)obj.GetValue(HighlightTextProperty);
    public static void SetHighlightText(DependencyObject obj, string value) => obj.SetValue(HighlightTextProperty, value);

    public static Brush GetHighlightBrush(DependencyObject obj) => (Brush)obj.GetValue(HighlightBrushProperty);
    public static void SetHighlightBrush(DependencyObject obj, Brush value) => obj.SetValue(HighlightBrushProperty, value);

    #endregion

    #region Private Methods

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
            return;

        var sourceText = GetSourceText(textBlock);
        var highlightText = GetHighlightText(textBlock);
        var highlightBrush = GetHighlightBrush(textBlock);

        UpdateTextBlock(textBlock, sourceText, highlightText, highlightBrush);
    }

    private static void UpdateTextBlock(TextBlock textBlock, string? sourceText, string? highlightText, Brush highlightBrush)
    {
        textBlock.Inlines.Clear();

        if (string.IsNullOrEmpty(sourceText))
            return;

        // No highlight text - just show source
        if (string.IsNullOrEmpty(highlightText))
        {
            textBlock.Inlines.Add(new Run(sourceText));
            return;
        }

        // Find and highlight all matches (case-insensitive)
        var sourceLower = sourceText.ToLowerInvariant();
        var highlightLower = highlightText.ToLowerInvariant();

        int currentIndex = 0;
        int matchIndex;

        while ((matchIndex = sourceLower.IndexOf(highlightLower, currentIndex, StringComparison.Ordinal)) >= 0)
        {
            // Add text before match
            if (matchIndex > currentIndex)
            {
                textBlock.Inlines.Add(new Run(sourceText[currentIndex..matchIndex]));
            }

            // Add highlighted match (preserving original case)
            var matchedText = sourceText.Substring(matchIndex, highlightText.Length);
            textBlock.Inlines.Add(new Run(matchedText) { Background = highlightBrush });

            currentIndex = matchIndex + highlightText.Length;
        }

        // Add remaining text after last match
        if (currentIndex < sourceText.Length)
        {
            textBlock.Inlines.Add(new Run(sourceText[currentIndex..]));
        }
    }

    #endregion
}
