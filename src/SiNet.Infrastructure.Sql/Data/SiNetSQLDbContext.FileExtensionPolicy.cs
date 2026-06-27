using System;
using System.Collections.Generic;

namespace SiNetSQL.Data;

/// <summary>
/// Pure (platform-agnostic) file-extension policy for <see cref="SiNetSQLDbContext"/>.
/// <para>
/// Ported verbatim from the legacy <c>OnCreated()</c> initializer in
/// <c>SiNetSQL\My Partial Class\SiNetSQLDbContext.cs</c>. Only the contamination-free
/// behavior was re-homed here (allowed-extension data and the two membership checks);
/// the legacy Windows/COM/impersonation members (CheckUser, hardcoded user-group lists,
/// ProjectDirectory, ProjectFullPhate) were intentionally NOT moved into the clean module.
/// </para>
/// </summary>
public partial class SiNetSQLDbContext
{
    // These fields store the allowed and the non-read-only extension lists.
    private SortedList<string, string>? _allowedExtension;
    private SortedList<string, string>? _notSetToReadOnlyExtension;

    /// <summary>
    /// File extensions recognized by the application, ordered case-insensitively.
    /// </summary>
    public IEnumerable<string> AvailableExtensions
    {
        get
        {
            EnsureExtensionPolicy();
            return _allowedExtension!.Keys;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is an allowed extension.
    /// </summary>
    public bool CheckallowedExtension(string value)
    {
        EnsureExtensionPolicy();
        return _allowedExtension!.ContainsKey(value);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is an extension that is
    /// not forced to read-only.
    /// </summary>
    public bool CheckNotSetToReadOnlyExtension(string value)
    {
        EnsureExtensionPolicy();
        return _notSetToReadOnlyExtension!.ContainsKey(value);
    }

    private void EnsureExtensionPolicy()
    {
        if (_allowedExtension is not null)
        {
            return;
        }

        _notSetToReadOnlyExtension = new SortedList<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            { ".SKN", ".SKN" },
            { ".ATP", ".ATP" },
        };

        _allowedExtension = new SortedList<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            { ".DWG", ".DWG" },
            { ".DWF", ".DWF" },
            { ".DWFX", ".DWFX" },
            { ".DOC", ".DOC" },
            { ".PDF", ".PDF" },
            { ".SKN", ".SKN" },
            { ".ATP", ".ATP" },
            { ".DOCX", ".DOCX" },
            { ".DST", ".DST" },
            { ".MSG", ".MSG" },
            { ".PLT", ".PLT" },
            { ".JPG", ".JPG" },
            { ".TIF", ".TIF" },
            { ".PNG", ".PNG" },
            { ".DXF", ".DXF" },
            { ".XPS", ".XPS" },
            { ".XLS", ".XLS" },
            { ".XLSX", ".XLSX" },
            { ".XLSM", ".XLSM" },
            { ".PPTX", ".PPTX" },
            { ".PPT", ".PPT" },
        };
    }
}
