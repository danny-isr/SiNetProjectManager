using SiNet.App.Wpf.Inspection;
using SiNet.Application.FileCatalog;
using SiNet.Domain.Files;

namespace SiNet.App.Wpf.Admin.FileCatalog;

public sealed class FileCatalogFileRowVm : ObservableObject
{
    public static FileStorageDestination[] StorageDestinationOptions { get; } =
        Enum.GetValues<FileStorageDestination>();

    private string? _title;
    private string? _typefile;
    private bool? _lookAtDes;
    private bool? _outSidData;
    private FileStorageDestination _storageDestination;
    private string? _templateLocation;
    private string? _description;
    private bool _isRequired;
    private int? _folderId;
    private bool _isDirty;

    public FileCatalogFileRowVm(FileCatalogFileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        FileId = dto.FileId;
        Number = dto.Number;
        JobTypeId = dto.JobTypeId;
        Code = dto.Code;
        _title = dto.Title;
        _typefile = dto.Typefile;
        _lookAtDes = dto.LookAtDes;
        _outSidData = dto.OutSidData;
        _storageDestination = dto.StorageDestination;
        _templateLocation = dto.TemplateLocation;
        _description = dto.Description;
        _isRequired = dto.IsRequired;
        _folderId = dto.FolderId;
    }

    public int FileId { get; }
    public float? Number { get; }
    public int? JobTypeId { get; }
    public string? Code { get; }
    public bool HasCatalogCode => !string.IsNullOrWhiteSpace(Code);

    public string? Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value))
                MarkDirty();
        }
    }

    public string? Typefile
    {
        get => _typefile;
        set
        {
            if (SetField(ref _typefile, value))
                MarkDirty();
        }
    }

    public bool? LookAtDes
    {
        get => _lookAtDes;
        set
        {
            if (SetField(ref _lookAtDes, value))
                MarkDirty();
        }
    }

    public bool? OutSidData
    {
        get => _outSidData;
        set
        {
            if (SetField(ref _outSidData, value))
                MarkDirty();
        }
    }

    public FileStorageDestination StorageDestination
    {
        get => _storageDestination;
        set
        {
            if (SetField(ref _storageDestination, value))
                MarkDirty();
        }
    }

    public string? TemplateLocation
    {
        get => _templateLocation;
        set
        {
            if (SetField(ref _templateLocation, value))
                MarkDirty();
        }
    }

    public string? Description
    {
        get => _description;
        set
        {
            if (SetField(ref _description, value))
                MarkDirty();
        }
    }

    public bool IsRequired
    {
        get => _isRequired;
        set
        {
            if (SetField(ref _isRequired, value))
                MarkDirty();
        }
    }

    public int? FolderId
    {
        get => _folderId;
        set => SetField(ref _folderId, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetField(ref _isDirty, value);
    }

    public void ClearDirty() => IsDirty = false;

    public void ApplyFolderId(int folderId)
    {
        FolderId = folderId;
    }

    public FileCatalogFileEditDto ToEditDto() =>
        new(
            FileId,
            Title,
            Typefile,
            LookAtDes,
            OutSidData,
            StorageDestination,
            TemplateLocation,
            Description,
            IsRequired);

    private void MarkDirty() => IsDirty = true;
}
