using System.Windows;

namespace SiNet.App.Wpf.Shell;

/// <summary>Lightweight branded splash shown while the standalone host initializes.</summary>
public partial class StartupSplashWindow : Window
{
    public StartupSplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        StatusText.Text = message;
    }
}
