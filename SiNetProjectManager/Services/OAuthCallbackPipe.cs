using Serilog;
using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SiNetProjectManager.Services;

/// <summary>
/// Named pipe server/client for inter-process OAuth callback communication.
/// When the OS activates a second app instance via the custom URI scheme,
/// that instance sends the callback URI through this pipe to the running instance
/// which is waiting in <see cref="CustomSchemeCodeReceiver.ReceiveCodeAsync"/>.
/// </summary>
internal static class OAuthCallbackPipe
{
    private const string PipeName = "SiNetProjectManager_OAuthCallback";

    /// <summary>
    /// Starts a named pipe server and waits for a single callback message.
    /// Called by <see cref="CustomSchemeCodeReceiver"/> in the running instance.
    /// </summary>
    internal static async Task<string> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        await using var server = new NamedPipeServerStream(
            PipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(cancellationToken);

        var buffer = new byte[4096];
        var totalRead = 0;

        int bytesRead;
        do
        {
            bytesRead = await server.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken);
            totalRead += bytesRead;
        }
        while (bytesRead > 0 && totalRead < buffer.Length);

        var result = Encoding.UTF8.GetString(buffer, 0, totalRead);
        Log.Information("OAuth callback received via named pipe ({Bytes} bytes)", totalRead);
        return result;
    }

    /// <summary>
    /// Sends a callback URI to the running instance via named pipe.
    /// Called by the protocol-activated second instance.
    /// Returns true if the message was sent successfully.
    /// </summary>
    internal static bool SendCallback(string uri)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 5000);

            var bytes = Encoding.UTF8.GetBytes(uri);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (TimeoutException)
        {
            Log.Warning("OAuth callback pipe timeout — no running instance is waiting for authentication.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send OAuth callback via named pipe.");
            return false;
        }
    }
}
