using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email;

public interface IEmailWorkItemWindowFactory
{
    EmailWorkItemWindow Create();
}

public sealed class EmailWorkItemWindowFactory(IServiceProvider services) : IEmailWorkItemWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public EmailWorkItemWindow Create()
    {
        var windowVm = _services.GetRequiredService<EmailWindowViewModel>();
        var bodyRenderer = _services.GetService<IEmailBodyRenderer>();
        return new EmailWorkItemWindow(windowVm, bodyRenderer);
    }
}
