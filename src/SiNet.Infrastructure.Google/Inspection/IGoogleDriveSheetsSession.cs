using Google.Apis.Drive.v3;
using Google.Apis.Sheets.v4;

namespace SiNet.Infrastructure.Google.Inspection;

/// <summary>
/// Supplies one authenticated Google user session shared by Drive and Sheets operations.
/// </summary>
public interface IGoogleDriveSheetsSession
{
    DriveService? DriveService { get; }

    SheetsService? SheetsService { get; }

    Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default);
}

internal sealed class GmailDriveSheetsSession(GmailClientProvider clientProvider)
    : IGoogleDriveSheetsSession
{
    private readonly GmailClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));

    public DriveService? DriveService { get; private set; }

    public SheetsService? SheetsService { get; private set; }

    public async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        DriveService = await _clientProvider
            .TryGetDriveServiceAsync(cancellationToken)
            .ConfigureAwait(false);
        SheetsService = await _clientProvider
            .TryGetSheetsServiceAsync(cancellationToken)
            .ConfigureAwait(false);

        if (DriveService is null || SheetsService is null)
        {
            throw new InvalidOperationException(
                "Google Drive and Sheets are unavailable. Connect the Google account and try again.");
        }
    }
}
