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

        if (DataContext is EmailViewerPaneViewModel vm)
        {
            vm.SetBodyRenderer(_bodyRenderer);
        }
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is EmailViewerPaneViewModel vm)
        {
            if (_bodyRenderer?.IsAvailable == true)
            {
                _bodyRenderer.AttachHost(BodyHost);
            }

            vm.SetBodyRenderer(_bodyRenderer);
        }
    }
}
