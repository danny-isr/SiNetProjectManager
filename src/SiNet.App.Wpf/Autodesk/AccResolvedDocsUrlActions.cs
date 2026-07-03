using System.Diagnostics;
using System.Windows;

namespace SiNet.App.Wpf.Autodesk;

public interface IAccResolvedDocsUrlLauncher
{
    void Open(string url);
}

public interface IClipboardTextWriter
{
    void SetText(string text);
}

internal sealed class ShellExecuteAccResolvedDocsUrlLauncher : IAccResolvedDocsUrlLauncher
{
    public void Open(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}

internal sealed class WpfClipboardTextWriter : IClipboardTextWriter
{
    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Clipboard.SetText(text);
    }
}
