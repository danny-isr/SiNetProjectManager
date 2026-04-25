using System.Windows;
using SiNetSQL.FileIndex;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// WPF implementation of <see cref="IFileReplacePrompts"/> — the three
/// confirmation dialogs the drag-and-drop replace flow can raise.
/// All prompts are RTL Hebrew to match the rest of the app's UI.
/// Defaults are intentionally conservative (No) so an accidental drop
/// never causes data loss.
/// </summary>
public sealed class FileReplacePrompts : IFileReplacePrompts
{
    /// <inheritdoc />
    public bool ConfirmReuploadIdentical(string fileName)
    {
        var result = MessageBox.Show(
            $"הקובץ '{fileName}' זהה למה שכבר קיים (אותו שם, גודל ותאריך).\n" +
            "להעלות בכל זאת?",
            "אין שינוי בקובץ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    /// <inheritdoc />
    public bool ConfirmOverwriteSameName(string fileName)
    {
        var result = MessageBox.Show(
            $"הקובץ '{fileName}' קיים בשם זה אך עם תוכן שונה.\n" +
            "האם להעלות גרסה חדשה?",
            "גרסה חדשה",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        return result == MessageBoxResult.Yes;
    }

    /// <inheritdoc />
    public bool ConfirmMateriallyDifferent(string existingName, string droppedName, double similarity)
    {
        var pct = (int)System.Math.Round(similarity * 100);
        var result = MessageBox.Show(
            $"השם של הקובץ שנגרר שונה משמעותית מהקיים:\n" +
            $"  קיים:  {existingName}\n" +
            $"  חדש:   {droppedName}\n" +
            $"  התאמה: {pct}%\n\n" +
            "האם זו גרסה חדשה של הקובץ הקיים?\n" +
            "(כן = מחליף את הקיים ושומר את השם הישן בהיסטוריה. לא = ביטול.)",
            "גרסה חדשה — שם שונה",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
