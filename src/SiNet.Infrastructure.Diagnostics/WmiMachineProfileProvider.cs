using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SiNet.Application.Diagnostics;

namespace SiNet.Infrastructure.Diagnostics;

/// <summary>
/// Collects the local hardware / OS profile through WMI and the registry. Every probe is isolated:
/// a failing query adds a warning to the report instead of losing the whole profile (DEV-010).
/// </summary>
public sealed class WmiMachineProfileProvider : IMachineProfileProvider
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const double BytesPerGb = 1024d * 1024d * 1024d;

    private static readonly string[] AutodeskProductTokens = ["AutoCAD", "Civil 3D", "Revit"];

    private sealed class WmiRow(IReadOnlyDictionary<string, string> values)
    {
        public string? String(string property)
            => values.TryGetValue(property, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;

        public int? Int(string property)
            => int.TryParse(String(property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        public ulong? UInt64(string property)
            => ulong.TryParse(String(property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
    }

    public Task<MachineProfileDto> GetProfileAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Collect(cancellationToken), cancellationToken);

    private static MachineProfileDto Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();

        var os = QueryFirst("SELECT Caption, Version, LastBootUpTime FROM Win32_OperatingSystem", warnings);
        var cpu = QueryFirst("SELECT Name, NumberOfLogicalProcessors FROM Win32_Processor", warnings);
        var computer = QueryFirst("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", warnings);

        var (freeGb, totalGb) = ReadSystemDrive(warnings);
        var totalMemoryBytes = computer?.UInt64("TotalPhysicalMemory");

        return new MachineProfileDto(
            Environment.MachineName,
            Environment.UserName,
            os?.String("Caption") ?? "Windows",
            os?.String("Version") ?? Environment.OSVersion.VersionString,
            cpu?.String("Name") ?? "unknown",
            cpu?.Int("NumberOfLogicalProcessors") ?? Environment.ProcessorCount,
            totalMemoryBytes is { } bytes ? bytes / BytesPerGb : 0d,
            freeGb,
            totalGb,
            ReadGraphicsAdapters(warnings),
            ReadAutodeskProducts(warnings),
            ReadUptime(os),
            ReadLastWindowsUpdate(warnings),
            warnings);
    }

    private static (double FreeGb, double TotalGb) ReadSystemDrive(List<string> warnings)
    {
        try
        {
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
            if (string.IsNullOrWhiteSpace(systemRoot))
            {
                return (0d, 0d);
            }

            var drive = new DriveInfo(systemRoot);
            return (drive.AvailableFreeSpace / BytesPerGb, drive.TotalSize / BytesPerGb);
        }
        catch (IOException ex)
        {
            warnings.Add($"System drive: {ex.Message}");
            return (0d, 0d);
        }
        catch (UnauthorizedAccessException ex)
        {
            warnings.Add($"System drive: {ex.Message}");
            return (0d, 0d);
        }
    }

    private static IReadOnlyList<GraphicsAdapterDto> ReadGraphicsAdapters(List<string> warnings)
        => Query("SELECT Name, DriverVersion, DriverDate, VideoProcessor FROM Win32_VideoController", warnings)
            .Select(row => new GraphicsAdapterDto(
                row.String("Name") ?? "unknown",
                row.String("DriverVersion"),
                ParseWmiDate(row.String("DriverDate")),
                row.String("VideoProcessor")))
            .ToList();

    private static IReadOnlyList<string> ReadAutodeskProducts(List<string> warnings)
    {
        var products = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = baseKey.OpenSubKey(UninstallKey);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var subKey = uninstall.OpenSubKey(subKeyName);
                    if (subKey?.GetValue("DisplayName") is not string displayName)
                    {
                        continue;
                    }

                    if (!AutodeskProductTokens.Any(token =>
                            displayName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var version = subKey.GetValue("DisplayVersion") as string;
                    products.Add(string.IsNullOrWhiteSpace(version)
                        ? displayName
                        : $"{displayName} ({version})");
                }
            }
            catch (System.Security.SecurityException ex)
            {
                warnings.Add($"Installed products ({view}): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                warnings.Add($"Installed products ({view}): {ex.Message}");
            }
        }

        return [.. products];
    }

    private static TimeSpan ReadUptime(WmiRow? os)
    {
        var lastBoot = ParseWmiDate(os?.String("LastBootUpTime"));
        return lastBoot is null ? TimeSpan.Zero : DateTimeOffset.Now - lastBoot.Value;
    }

    private static DateTimeOffset? ReadLastWindowsUpdate(List<string> warnings)
    {
        DateTimeOffset? latest = null;

        foreach (var row in Query("SELECT InstalledOn FROM Win32_QuickFixEngineering", warnings))
        {
            var raw = row.String("InstalledOn");
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                && !DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
            {
                continue;
            }

            var candidate = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Local));
            if (latest is null || candidate > latest)
            {
                latest = candidate;
            }
        }

        return latest;
    }

    private static WmiRow? QueryFirst(string wql, List<string> warnings)
        => Query(wql, warnings).FirstOrDefault();

    /// <summary>
    /// Materializes the whole result set before returning: the caller must not depend on the
    /// lifetime of the underlying <see cref="ManagementObjectCollection"/>.
    /// </summary>
    private static IReadOnlyList<WmiRow> Query(string wql, List<string> warnings)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var collection = searcher.Get();

            var rows = new List<WmiRow>();
            foreach (var item in collection)
            {
                using var row = item;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var property in row.Properties)
                {
                    if (property.Value is { } value)
                    {
                        values[property.Name] = value.ToString() ?? string.Empty;
                    }
                }

                rows.Add(new WmiRow(values));
            }

            return rows;
        }
        catch (ManagementException ex)
        {
            warnings.Add($"WMI [{wql}]: {ex.Message}");
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            warnings.Add($"WMI [{wql}]: {ex.Message}");
            return [];
        }
        catch (COMException ex)
        {
            warnings.Add($"WMI [{wql}]: {ex.Message}");
            return [];
        }
    }

    /// <summary>WMI datetime is <c>yyyyMMddHHmmss.ffffff±UUU</c>; anything else is treated as unknown.</summary>
    private static DateTimeOffset? ParseWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 14)
        {
            return null;
        }

        return DateTime.TryParseExact(
            value[..14],
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Local))
            : null;
    }
}
