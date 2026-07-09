using System.Windows.Controls;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public partial class EmailViewerPaneView : UserControl
{
    private IEmailBodyRenderer? _bodyRenderer;

    public EmailViewerPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public void SetBodyRenderer(IEmailBodyRenderer? bodyRenderer)
    {
        _bodyRenderer = bodyRenderer;
        if (_bodyRenderer?.IsAvailable == true)
        {
            _bodyRenderer.AttachHost(BodyHost);
        }
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is EmailViewerPaneViewModel vm && _bodyRenderer?.IsAvailable == true)
        {
            _bodyRenderer.AttachHost(BodyHost);
        }
    }
}
