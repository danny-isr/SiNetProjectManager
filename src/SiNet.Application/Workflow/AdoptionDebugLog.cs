using System.Text;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Application.Workflow;

/// <summary>
/// Temporary click-path trace for Existing Workflow Adoption. Remove after the dialog-open failure is found.
/// </summary>
public static class AdoptionDebugLog
{
    public const string Prefix = "[AdoptionDebug]";

    public static string LogFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiNet",
        "adoption-debug.log");

    public static void Reset()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);
        }
        catch
        {
            // Logging must not hide the failure under investigation.
        }
    }

    public static void Write(string step, string? details = null, IAppLogger? logger = null)
    {
        var line = $"{Prefix} {DateTime.Now:HH:mm:ss.fff} {step}";
        if (!string.IsNullOrWhiteSpace(details))
            line += " | " + details;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Logging must not hide the failure under investigation.
        }

        System.Diagnostics.Trace.WriteLine(line);
        try
        {
            logger?.Info(line);
        }
        catch
        {
            // Same as above.
        }
    }

    public static void Error(string step, Exception exception, IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = new StringBuilder();
        text.Append(exception.GetType().FullName);
        text.Append(": ");
        text.Append(exception.Message);
        text.AppendLine();
        text.Append(exception);
        if (exception.InnerException is not null)
        {
            text.AppendLine();
            text.Append("INNER ");
            text.Append(exception.InnerException.GetType().FullName);
            text.Append(": ");
            text.Append(exception.InnerException.Message);
            text.AppendLine();
            text.Append(exception.InnerException);
        }

        Write(step, text.ToString(), logger);
        try
        {
            logger?.Error($"{Prefix} {step}", exception);
        }
        catch
        {
            // Same as above.
        }
    }
}
