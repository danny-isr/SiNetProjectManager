using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Creates fully-wired <see cref="EmailWindowView"/> instances through DI so every window shares the
/// singleton <see cref="ICurrentProjectContext"/> (see <c>docs/PROJECTS.md</c> §5/§9).
/// <para>
/// Hosts should open the Email visual clone through this factory instead of <c>new EmailWindowView()</c>,
/// which would otherwise construct an isolated in-memory context per window. Opening several windows via
/// the factory therefore reflects the <b>same</b> Current Project. This is fake-data only; the factory
/// wires no DB, email, or workflow behavior.
/// </para>
/// </summary>
public interface IEmailWindowFactory
{
    /// <summary>Creates a new Email visual-clone window bound to a DI-resolved view model (shared context).</summary>
    EmailWindowView Create();
}

/// <summary>
/// Default <see cref="IEmailWindowFactory"/>. Resolves a transient <see cref="EmailWindowViewModel"/>
/// (which depends on the singleton <see cref="ICurrentProjectContext"/> and the fake
/// <see cref="IProjectQueryService"/>) and binds it to a new <see cref="EmailWindowView"/>.
/// </summary>
public sealed class EmailWindowFactory(IServiceProvider services) : IEmailWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public EmailWindowView Create()
    {
        // Resolve a fresh view model per window; it shares the singleton current-project context.
        var viewModel = _services.GetRequiredService<EmailWindowViewModel>();
        return new EmailWindowView(viewModel);
    }
}
