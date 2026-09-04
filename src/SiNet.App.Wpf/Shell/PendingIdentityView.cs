using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Shell;

/// <summary>Restricted shell content for Pending / Unauthorized SIUser.</summary>
public sealed class PendingIdentityView : UserControl
{
    public PendingIdentityView(PendingIdentityViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        FlowDirection = FlowDirection.RightToLeft;

        var root = new StackPanel
        {
            Margin = new Thickness(32),
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        root.Children.Add(new TextBlock
        {
            Text = "ממתין לאישור מנהל מערכת",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16),
            TextAlignment = TextAlignment.Center,
        });

        root.Children.Add(new TextBlock
        {
            Text = IdentityStatusDisplay.PendingMessage,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 20),
            TextAlignment = TextAlignment.Center,
        });

        root.Children.Add(new TextBlock
        {
            Text = viewModel.UserLine,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 4),
            TextAlignment = TextAlignment.Center,
        });

        root.Children.Add(new TextBlock
        {
            Text = IdentityStatusDisplay.PendingStatusLine,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 24),
            TextAlignment = TextAlignment.Center,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        buttons.Children.Add(new Button
        {
            Content = "רענן הרשאות / בדוק שוב",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 12, 0),
            Command = viewModel.RefreshCommand,
        });

        buttons.Children.Add(new Button
        {
            Content = "יציאה",
            Padding = new Thickness(16, 8, 16, 8),
            Command = viewModel.ExitCommand,
        });

        root.Children.Add(buttons);

        var status = new TextBlock
        {
            Margin = new Thickness(0, 16, 0, 0),
            TextAlignment = TextAlignment.Center,
            Opacity = 0.85,
        };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(PendingIdentityViewModel.StatusMessage)));
        root.Children.Add(status);

        Content = root;
    }
}
