using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.Domain.Files;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// Base class for a node in the unified ProjectWork tree (folder / file / alternative / version). Clean
/// presentation models — they carry no DB or IO logic; the tree view model builds and populates them
/// from Application DTOs and scanned files.
/// </summary>
public abstract class ProjectWorkNodeVm : ObservableObject
{
    private string _title = string.Empty;
    private bool _isExpanded;

    /// <summary>Display title shown in the tree.</summary>
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    /// <summary>Whether the node is expanded in the tree (preserved across in-place rescans).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>Child nodes (heterogeneous — folders, files, alternatives or versions).</summary>
    public ObservableCollection<ProjectWorkNodeVm> Children { get; } = new();
}

/// <summary>A DB-defined project folder node. Children are subfolders and file nodes.</summary>
public sealed class ProjectFolderNodeVm : ProjectWorkNodeVm
{
    /// <summary>DB folder id (non-positive for synthetic / user-created folders).</summary>
    public int FolderId { get; init; }

    /// <summary>Resolved absolute file-server path, when known.</summary>
    public string? FullPath { get; set; }

    private bool _hasPhysicalFiles;
    private bool _hasDefinedFiles;

    /// <summary>True when this folder (or a descendant) has at least one scanned physical version.</summary>
    public bool HasPhysicalFiles
    {
        get => _hasPhysicalFiles;
        set
        {
            if (SetField(ref _hasPhysicalFiles, value))
                OnPropertyChanged(nameof(HasFiles));
        }
    }

    /// <summary>True when this folder (or a descendant) has project-type file definitions (non-unfiled).</summary>
    public bool HasDefinedFiles
    {
        get => _hasDefinedFiles;
        set => SetField(ref _hasDefinedFiles, value);
    }

    /// <summary>Alias for <see cref="HasPhysicalFiles"/> — kept for existing callers/tests.</summary>
    public bool HasFiles
    {
        get => HasPhysicalFiles;
        set => HasPhysicalFiles = value;
    }
}

/// <summary>A logical project file node. Children are its alternatives.</summary>
public sealed class ProjectFileNodeVm : ProjectWorkNodeVm
{
    /// <summary>DB id of the underlying <c>ProjectFile</c>, when this node maps to a definition.</summary>
    public int? FileId { get; init; }

    /// <summary>Configured storage destination for this file.</summary>
    public FileStorageDestination StorageDestination { get; init; }

    /// <summary>Project type / discipline id (for building canonical version names). Null when unfiled.</summary>
    public int? ProjectType { get; init; }

    /// <summary>File number within the project/type (for building canonical version names). Null when unfiled.</summary>
    public int? Number { get; init; }

    /// <summary>DB id of the folder that owns this file (used to resolve the storage folder handle).</summary>
    public int ParentFolderId { get; init; }

    /// <summary>Adds a new alternative/version to this file from a picked source file. Set by the tree view model.</summary>
    public ICommand? AddVersionCommand { get; set; }

    /// <summary>True for the synthetic "unfiled" bucket holding files that match no DB definition.</summary>
    public bool IsUnfiled { get; init; }

    /// <summary>Short storage label for the badge (FileServer / ACC / Drive).</summary>
    public string StorageLabel => StorageDestination switch
    {
        FileStorageDestination.Acc => "ACC",
        FileStorageDestination.GoogleDrive => "Drive",
        _ => "FS",
    };
}

/// <summary>An alternative (variant) under a file. Children are its versions.</summary>
public sealed class AlternativeNodeVm : ProjectWorkNodeVm
{
    /// <summary>Alternative label (e.g. "1", "A").</summary>
    public string AlternativeName { get; init; } = string.Empty;

    /// <summary>Adds a new version to this alternative from a picked/dropped source file. Set by the tree view model.</summary>
    public ICommand? AddVersionCommand { get; set; }
}

/// <summary>Status of a ProjectWork write operation (add version / replace / rename / delete).</summary>
public enum FileWriteStatus
{
    /// <summary>The operation completed and the tree was rescanned.</summary>
    Success,

    /// <summary>Blocked: a same-base-name file with a different extension already exists.</summary>
    ExtensionConflict,

    /// <summary>Blocked by the ACC-write gate.</summary>
    Gated,

    /// <summary>The destination does not support the operation (e.g. ACC rename/replace).</summary>
    NotSupported,

    /// <summary>No store is registered for the destination.</summary>
    NoStore,

    /// <summary>The operation failed (see the message).</summary>
    Failed,
}

/// <summary>Result of a ProjectWork write operation.</summary>
public sealed record FileWriteOutcome(FileWriteStatus Status, string? Message = null);

/// <summary>A single physical version under an alternative. Leaf node.</summary>
public sealed class VersionNodeVm : ProjectWorkNodeVm
{
    /// <summary>Version number.</summary>
    public int VersionNumber { get; init; }

    /// <summary>Absolute path for FileServer versions; <see langword="null"/> otherwise.</summary>
    public string? FullPath { get; init; }

    /// <summary>ACC item id for ACC-backed versions; <see langword="null"/> otherwise.</summary>
    public string? AccItemId { get; init; }

    /// <summary>ACC viewer URL for ACC-backed versions; <see langword="null"/> otherwise.</summary>
    public string? AccViewerUrl { get; init; }

    /// <summary>Owning ACC project id for ACC-backed versions; <see langword="null"/> otherwise.</summary>
    public string? AccProjectId { get; init; }

    /// <summary>Google Drive file id for Drive-backed versions; <see langword="null"/> otherwise.</summary>
    public string? DriveFileId { get; init; }

    /// <summary>Storage destination of this physical version.</summary>
    public FileStorageDestination StorageDestination { get; init; }

    /// <summary>DB id of the folder that owns this version (used to resolve the storage folder handle).</summary>
    public int ParentFolderId { get; init; }

    /// <summary>True when the version lives in ACC.</summary>
    public bool IsAcc => StorageDestination == FileStorageDestination.Acc;

    /// <summary>True when the version lives in Google Drive.</summary>
    public bool IsDrive => StorageDestination == FileStorageDestination.GoogleDrive;

    private bool _isAccTabOpen;
    private bool _suppressAccTabToggle;

    /// <summary>
    /// Checked when this ACC version's viewer tab is open. Two-way: uncheck closes the tab.
    /// </summary>
    public bool IsAccTabOpen
    {
        get => _isAccTabOpen;
        set
        {
            if (_suppressAccTabToggle || _isAccTabOpen == value)
                return;
            if (!SetField(ref _isAccTabOpen, value))
                return;
            AccTabOpenChanged?.Invoke(this, value);
        }
    }

    /// <summary>Raised when the user toggles <see cref="IsAccTabOpen"/> (not when set programmatically via <see cref="SetAccTabOpenSilent"/>).</summary>
    public Action<VersionNodeVm, bool>? AccTabOpenChanged { get; set; }

    /// <summary>Updates the checkbox without raising <see cref="AccTabOpenChanged"/>.</summary>
    public void SetAccTabOpenSilent(bool isOpen)
    {
        _suppressAccTabToggle = true;
        try
        {
            if (_isAccTabOpen == isOpen)
                return;
            _isAccTabOpen = isOpen;
            OnPropertyChanged(nameof(IsAccTabOpen));
        }
        finally
        {
            _suppressAccTabToggle = false;
        }
    }

    /// <summary>Formatted size/date for display; may be empty.</summary>
    public string? Details { get; init; }

    private bool _isInFlight;

    /// <summary>True while this version is mid-upload — drives the "pending" (green) badge state.</summary>
    public bool IsInFlight
    {
        get => _isInFlight;
        set => SetField(ref _isInFlight, value);
    }

    private bool _hasExtensionConflict;

    /// <summary>True when another file shares this version's base name with a different extension.</summary>
    public bool HasExtensionConflict
    {
        get => _hasExtensionConflict;
        set => SetField(ref _hasExtensionConflict, value);
    }

    /// <summary>Opens this version (local app or ACC viewer). Set by the tree view model.</summary>
    public ICommand? OpenCommand { get; set; }

    /// <summary>Replaces this version's content from a dropped/picked file. Set by the tree view model.</summary>
    public ICommand? ReplaceCommand { get; set; }

    /// <summary>Renames this version (FileServer only). Set by the tree view model.</summary>
    public ICommand? RenameCommand { get; set; }

    /// <summary>Deletes this version. Set by the tree view model.</summary>
    public ICommand? DeleteCommand { get; set; }
}
