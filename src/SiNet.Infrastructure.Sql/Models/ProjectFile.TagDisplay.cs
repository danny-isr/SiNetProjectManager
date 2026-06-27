namespace SiNetSQL.Models;

public partial class ProjectFile
{
    /// <summary>
    /// Display label for the tag ComboBox in email attachment tagging.
    /// Shows "FolderName / FileTitle" for clear identification.
    /// </summary>
    public string TagDisplayLabel =>
        Folder?.Title is { Length: > 0 } folderTitle
            ? $"{folderTitle} / {Title}"
            : Title ?? "(ללא שם)";
}
