using System.Windows.Controls;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public partial class EmailDetailView : UserControl
{
    public EmailDetailView()
    {
        InitializeComponent();
    }

    public void SetBodyRenderer(IEmailBodyRenderer? bodyRenderer) =>
        ViewerPane.SetBodyRenderer(bodyRenderer);
}
