using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

internal static partial class EmailListViewModelTestFixtures
{
    internal sealed class TrackingAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; set; }

        public string? ConnectedAccountEmail { get; set; } = "test@example.com";

        public bool LoginSucceeds { get; set; } = true;

        public string LoginConnectedEmail { get; set; } = "new-user@example.com";

        public bool RestoreSessionOnFailedLogin { get; set; }

        public string? RestoredAccountEmail { get; set; }

        public ConnectorLoginOptions? LastLoginOptions { get; private set; }

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastLoginOptions = options;
            if (!LoginSucceeds)
            {
                return Task.FromResult(false);
            }

            IsAuthenticated = true;
            ConnectedAccountEmail = LoginConnectedEmail;
            AuthStateChanged?.Invoke(true);
            return Task.FromResult(true);
        }

        public void Logout()
        {
            IsAuthenticated = false;
            ConnectedAccountEmail = null;
            AuthStateChanged?.Invoke(false);
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            Logout();
            return Task.CompletedTask;
        }

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        {
            if (!RestoreSessionOnFailedLogin)
            {
                return Task.FromResult(IsAuthenticated);
            }

            IsAuthenticated = true;
            ConnectedAccountEmail = RestoredAccountEmail;
            AuthStateChanged?.Invoke(true);
            return Task.FromResult(true);
        }

        public Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    internal sealed class StubAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; set; } = true;

        public string? ConnectedAccountEmail { get; set; } = "test@example.com";

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

        public void Logout()
        {
            IsAuthenticated = false;
            ConnectedAccountEmail = null;
            AuthStateChanged?.Invoke(false);
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            Logout();
            return Task.CompletedTask;
        }

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

        public Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
    internal sealed class StubThreadLinkQuery : IEmailThreadLinkQueryService
    {
        public IReadOnlyDictionary<string, EmailProjectLinkInfo> ThreadStates { get; init; } =
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByInternetMessageIdsAsync(
            IReadOnlyList<string> internetMessageIds,
            CancellationToken cancellationToken = default)
        {
            var map = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["abc@mail.com"] = new EmailProjectLinkInfo(
                    IsLinked: true,
                    ProjectId: 1042,
                    ProjectNumber: "1042",
                    ProjectName: "North",
                    DisplayName: "1042 — North"),
            };

            return Task.FromResult<IReadOnlyDictionary<string, EmailProjectLinkInfo>>(map);
        }

        public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByGmailThreadIdsAsync(
            IReadOnlyList<string> gmailThreadIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ThreadStates);
    }

    internal sealed class FailingThreadLinkQuery : IEmailThreadLinkQueryService
    {
        public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByInternetMessageIdsAsync(
            IReadOnlyList<string> internetMessageIds,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DB enrichment failed");

        public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByGmailThreadIdsAsync(
            IReadOnlyList<string> gmailThreadIds,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DB enrichment failed");
    }

    internal sealed class StubCurrentProjectContext(ProjectSummaryDto? project) : ICurrentProjectContext
    {
        public ProjectSummaryDto? CurrentProject { get; } = project;

        public event EventHandler<ProjectChangedEventArgs>? CurrentProjectChanged;

        public Task SetCurrentProjectAsync(ProjectSummaryDto? project, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    internal sealed class StubCurrentUser(int userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }

    internal sealed class RecordingFilingService : IEmailFilingService
    {
        public bool FileCalled { get; private set; }

        public bool UnfileCalled { get; private set; }

        public int FileCallCount { get; private set; }

        public FileEmailToProjectCommand? LastFileCommand { get; private set; }

        public UnfileEmailCommand? LastUnfileCommand { get; private set; }

        public Task<EmailFilingResult> FileToProjectAsync(
            FileEmailToProjectCommand command,
            CancellationToken cancellationToken = default)
        {
            FileCalled = true;
            FileCallCount++;
            LastFileCommand = command;
            return Task.FromResult(new EmailFilingResult(true, AssignedProjectId: command.TargetProjectId));
        }

        public Task<EmailFilingResult> UnfileFromProjectAsync(
            UnfileEmailCommand command,
            CancellationToken cancellationToken = default)
        {
            UnfileCalled = true;
            LastUnfileCommand = command;
            return Task.FromResult(new EmailFilingResult(true));
        }
    }

    internal sealed class DelayingFilingService : IEmailFilingService
    {
        private readonly TaskCompletionSource _fileGate = new();
        private readonly TaskCompletionSource _unfileGate = new();

        public int FileCallCount { get; private set; }

        public int UnfileCallCount { get; private set; }

        public void Release() => _fileGate.TrySetResult();

        public void ReleaseUnfile() => _unfileGate.TrySetResult();

        public async Task<EmailFilingResult> FileToProjectAsync(
            FileEmailToProjectCommand command,
            CancellationToken cancellationToken = default)
        {
            FileCallCount++;
            await _fileGate.Task.ConfigureAwait(true);
            return new EmailFilingResult(true, AssignedProjectId: command.TargetProjectId);
        }

        public async Task<EmailFilingResult> UnfileFromProjectAsync(
            UnfileEmailCommand command,
            CancellationToken cancellationToken = default)
        {
            UnfileCallCount++;
            await _unfileGate.Task.ConfigureAwait(true);
            return new EmailFilingResult(true);
        }
    }

    internal sealed class FailingFilingService(string errorMessage) : IEmailFilingService
    {
        public Task<EmailFilingResult> FileToProjectAsync(
            FileEmailToProjectCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailFilingResult(false, errorMessage));

        public Task<EmailFilingResult> UnfileFromProjectAsync(
            UnfileEmailCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailFilingResult(false, errorMessage));
    }

    internal sealed class DelayingStatusService : IEmailStatusService
    {
        private readonly TaskCompletionSource _gate = new();

        public void Release() => _gate.TrySetResult();

        public async Task<EmailStatusResult> SetStatusAsync(
            SetEmailStatusCommand command,
            CancellationToken cancellationToken = default)
        {
            await _gate.Task.ConfigureAwait(true);
            return new EmailStatusResult(true);
        }
    }
    internal sealed class RecordingStatusService : IEmailStatusService
    {
        public bool StatusCalled { get; private set; }

        public EmailTriageStatus? LastStatus { get; private set; }

        public Task<EmailStatusResult> SetStatusAsync(
            SetEmailStatusCommand command,
            CancellationToken cancellationToken = default)
        {
            StatusCalled = true;
            LastStatus = command.Status;
            return Task.FromResult(new EmailStatusResult(true));
        }
    }
}

