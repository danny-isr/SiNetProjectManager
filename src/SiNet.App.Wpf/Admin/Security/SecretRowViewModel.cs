using SiNet.App.Wpf.Inspection;
using SiNet.Application.Configuration;

namespace SiNet.App.Wpf.Admin.Security;

public sealed class SecretRowViewModel : ObservableObject
{
    private string _textValue = string.Empty;
    private string _passwordValue = string.Empty;
    private string _jsonFileLabel = string.Empty;
    private SecretStatusLevel _statusLevel = SecretStatusLevel.Missing;
    private string _statusToolTip = "חסר — לא הוגדר";

    public SecretRowViewModel(SecretCatalogEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    public SecretCatalogEntry Entry { get; }

    public string Key => Entry.Key;

    public string DisplayName => Entry.DisplayName;

    public SecretKind Kind => Entry.Kind;

    public bool IsPassword => Kind is SecretKind.Password;

    public bool IsJsonFile => Kind is SecretKind.JsonFile;

    public bool IsMultiline => Kind is SecretKind.ConnectionString;

    public bool IsAccServiceApiKey => Key == SecretCatalog.AccServiceApiKey;

    public string TextValue
    {
        get => _textValue;
        set => SetField(ref _textValue, value);
    }

    public string PasswordValue
    {
        get => _passwordValue;
        set => SetField(ref _passwordValue, value);
    }

    public string JsonFileLabel
    {
        get => _jsonFileLabel;
        set => SetField(ref _jsonFileLabel, value);
    }

    public SecretStatusLevel StatusLevel
    {
        get => _statusLevel;
        set => SetField(ref _statusLevel, value);
    }

    public string StatusToolTip
    {
        get => _statusToolTip;
        set => SetField(ref _statusToolTip, value);
    }

    public string? GetPendingValue()
    {
        return Kind switch
        {
            SecretKind.Password => string.IsNullOrWhiteSpace(PasswordValue) ? null : PasswordValue,
            SecretKind.JsonFile => null,
            _ => string.IsNullOrWhiteSpace(TextValue) ? null : TextValue,
        };
    }

    internal void ApplyStatus(SecretStatusDto status)
    {
        StatusLevel = status.Level;
        StatusToolTip = status.ToolTip ?? string.Empty;
    }
}
