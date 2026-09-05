using System.Text;

namespace SiNet.Application.Identity;

/// <summary>
/// Non-secret AccService Admin token export package metadata (drop folder / sidecar).
/// Never contains access/refresh tokens or client secrets.
/// </summary>
public static class AccServiceTokenPackageMeta
{
    public const string TokenPurposeValue = "AccServiceAdmin";
    public const string MetaFileName = "export_meta.txt";
    public const string SidecarFileName = "token_identity.txt";

    public static string Format(
        string expectedAdminEmail,
        string actualAdminEmail,
        string? autodeskUserId,
        string sourceMachine,
        DateTimeOffset exportedUtc,
        string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAdminEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualAdminEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMachine);

        var sb = new StringBuilder();
        sb.Append("TokenPurpose=").Append(TokenPurposeValue).AppendLine();
        sb.Append("ExpectedAdminEmail=").Append(expectedAdminEmail.Trim()).AppendLine();
        sb.Append("ActualAdminEmail=").Append(actualAdminEmail.Trim()).AppendLine();
        if (!string.IsNullOrWhiteSpace(autodeskUserId))
        {
            sb.Append("AutodeskUserId=").Append(autodeskUserId.Trim()).AppendLine();
        }

        sb.Append("ExportedUtc=").Append(exportedUtc.UtcDateTime.ToString("o")).AppendLine();
        sb.Append("SourceMachine=").Append(sourceMachine.Trim()).AppendLine();
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            sb.Append("SourcePath=").Append(sourcePath.Trim()).AppendLine();
        }

        return sb.ToString();
    }

    public static AccServiceTokenPackageMetaDto Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        map.TryGetValue("TokenPurpose", out var purpose);
        map.TryGetValue("ExpectedAdminEmail", out var expected);
        map.TryGetValue("ActualAdminEmail", out var actual);
        map.TryGetValue("AutodeskUserId", out var userId);
        map.TryGetValue("ExportedUtc", out var exportedUtc);
        map.TryGetValue("SourceMachine", out var machine);
        map.TryGetValue("SourcePath", out var sourcePath);

        return new AccServiceTokenPackageMetaDto(
            TokenPurpose: purpose,
            ExpectedAdminEmail: expected,
            ActualAdminEmail: actual,
            AutodeskUserId: userId,
            ExportedUtc: exportedUtc,
            SourceMachine: machine,
            SourcePath: sourcePath);
    }

    public static AccServiceTokenPackageValidation ValidateForInstall(
        AccServiceTokenPackageMetaDto meta,
        string? configuredExpectedAdminEmail = null)
    {
        ArgumentNullException.ThrowIfNull(meta);

        if (!string.Equals(meta.TokenPurpose, TokenPurposeValue, StringComparison.OrdinalIgnoreCase))
        {
            return new AccServiceTokenPackageValidation(
                Accepted: false,
                Reason: $"TokenPurpose must be {TokenPurposeValue}.");
        }

        if (string.IsNullOrWhiteSpace(meta.ExpectedAdminEmail)
            || string.IsNullOrWhiteSpace(meta.ActualAdminEmail))
        {
            return new AccServiceTokenPackageValidation(
                Accepted: false,
                Reason: "ExpectedAdminEmail and ActualAdminEmail are required.");
        }

        if (!IdentityEmailComparer.EqualsNormalized(meta.ExpectedAdminEmail, meta.ActualAdminEmail))
        {
            return new AccServiceTokenPackageValidation(
                Accepted: false,
                Reason:
                    $"Package ActualAdminEmail '{meta.ActualAdminEmail}' does not match ExpectedAdminEmail '{meta.ExpectedAdminEmail}'.");
        }

        var configured = string.IsNullOrWhiteSpace(configuredExpectedAdminEmail)
            ? null
            : configuredExpectedAdminEmail.Trim();
        if (configured is not null
            && !IdentityEmailComparer.EqualsNormalized(configured, meta.ActualAdminEmail))
        {
            return new AccServiceTokenPackageValidation(
                Accepted: false,
                Reason:
                    $"Package ActualAdminEmail '{meta.ActualAdminEmail}' does not match configured AccBootstrapAdminEmail '{configured}'.");
        }

        return new AccServiceTokenPackageValidation(Accepted: true, Reason: null);
    }

    /// <summary>
    /// True when <paramref name="tokenPath"/> is the dedicated AccService Admin store
    /// (…\SiNet\Autodesk\AccService\refresh_token.json), not the desktop UserContext file.
    /// </summary>
    public static bool IsDedicatedAccServiceTokenPath(string? tokenPath)
    {
        if (string.IsNullOrWhiteSpace(tokenPath))
        {
            return false;
        }

        var full = Path.GetFullPath(tokenPath.Trim());
        var fileName = Path.GetFileName(full);
        if (!string.Equals(fileName, "refresh_token.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(full) ?? string.Empty;
        var leaf = Path.GetFileName(dir);
        if (!string.Equals(leaf, "AccService", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parent = Path.GetDirectoryName(dir) ?? string.Empty;
        return string.Equals(Path.GetFileName(parent), "Autodesk", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGenericDesktopTokenPath(string? tokenPath)
    {
        if (string.IsNullOrWhiteSpace(tokenPath))
        {
            return false;
        }

        var full = Path.GetFullPath(tokenPath.Trim());
        if (!string.Equals(Path.GetFileName(full), "refresh_token.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(full) ?? string.Empty;
        var leaf = Path.GetFileName(dir);
        return string.Equals(leaf, "Autodesk", StringComparison.OrdinalIgnoreCase)
            && !IsDedicatedAccServiceTokenPath(full);
    }
}

public sealed record AccServiceTokenPackageMetaDto(
    string? TokenPurpose,
    string? ExpectedAdminEmail,
    string? ActualAdminEmail,
    string? AutodeskUserId,
    string? ExportedUtc,
    string? SourceMachine,
    string? SourcePath);

public sealed record AccServiceTokenPackageValidation(bool Accepted, string? Reason);
