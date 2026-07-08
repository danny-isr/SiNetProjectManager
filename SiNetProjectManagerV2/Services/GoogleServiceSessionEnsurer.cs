using System.IO;
using SiNet.Application.Email.Acc;
using SiOffice.GoogleConnector;
using SiOffice.GoogleConnector.Logging;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Bridges the new Gmail connector auth to the legacy <see cref="GoogleService"/> session used by ACC ingest.
/// </summary>
internal sealed class GoogleServiceSessionEnsurer(GoogleService googleService) : IGoogleIngestSessionEnsurer
{
    private readonly GoogleService _googleService =
        googleService ?? throw new ArgumentNullException(nameof(googleService));
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    public Task<bool> EnsureAuthenticatedForAccIngestAsync(CancellationToken cancellationToken = default) =>
        EnsureAuthenticatedAsync("AccIngest", cancellationToken);

    private async Task<bool> EnsureAuthenticatedAsync(string operationName, CancellationToken cancellationToken)
    {
        ReportLogger.Info(
            $"Operation={operationName} Step=EnsureGmailAuthenticated GoogleServiceLoggedIn={_googleService.IsAuthenticated} " +
            $"GmailServiceAvailable={_googleService.IsGmailServiceAvailable} Result=Started Reason=(none)");

        if (_googleService.IsAuthenticated && _googleService.IsGmailServiceAvailable)
        {
            return true;
        }

        await _loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_googleService.IsAuthenticated && _googleService.IsGmailServiceAvailable)
            {
                return true;
            }

            var credentialsPath = ResolveGoogleCredentialsPath();
            if (string.IsNullOrWhiteSpace(credentialsPath))
            {
                ReportLogger.Warn(
                    $"Operation={operationName} Step=EnsureGmailAuthenticated Result=Failed Reason=CredentialsNotFound");
                return false;
            }

            await _googleService.LoginAsync(credentialsPath).ConfigureAwait(false);
            return _googleService.IsAuthenticated && _googleService.IsGmailServiceAvailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ReportLogger.Warn(
                $"Operation={operationName} Step=EnsureGmailAuthenticated Result=Failed Reason={ex.Message}");
            return false;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private static string? ResolveGoogleCredentialsPath()
    {
        var configured = AppConfiguration.GetGoogleClientSecretsPath();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var credentialsPaths = new[]
        {
            "credentials.json",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SiOffice", "credentials.json"),
        };

        return credentialsPaths.FirstOrDefault(File.Exists);
    }
}
