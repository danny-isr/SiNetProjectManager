namespace SiNet.Application.Email.Acc;

/// <summary>Tracks in-flight ACC ingest / external-download work for close-dialog UX.</summary>
public interface IEmailAccBackgroundWorkTracker
{
    int ActiveCount { get; }

    event Action<int>? ActiveCountChanged;

    IDisposable BeginWork();
}
