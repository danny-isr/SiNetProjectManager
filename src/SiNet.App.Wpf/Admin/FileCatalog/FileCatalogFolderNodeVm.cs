using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.FileCatalog;

namespace SiNet.App.Wpf.Admin.FileCatalog;

public sealed class FileCatalogFolderNodeVm : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public FileCatalogFolderNodeVm(FileCatalogFolderDto dto, FileCatalogFolderNodeVm? parent = null)
    {
        ArgumentNullException.ThrowIfNull(dto);
        FolderId = dto.FolderId;
        Title = dto.Title;
        Parent = parent;
        Children = new ObservableCollection<FileCatalogFolderNodeVm>(
            dto.Children.Select(c => new FileCatalogFolderNodeVm(c, this)));
    }

    public int FolderId { get; }
    public string Title { get; }
    public FileCatalogFolderNodeVm? Parent { get; }
    public ObservableCollection<FileCatalogFolderNodeVm> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetField(ref _isExpanded, value))
                return;
            if (_isExpanded && Parent is not null)
                Parent.IsExpanded = true;
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetField(ref _isSelected, value))
                return;
            if (_isSelected && Parent is not null)
                Parent.IsExpanded = true;
        }
    }

    public FileCatalogFolderNodeVm? Find(int folderId)
    {
        if (FolderId == folderId)
            return this;
        foreach (var child in Children)
        {
            var found = child.Find(folderId);
            if (found is not null)
                return found;
        }

        return null;
    }
}
