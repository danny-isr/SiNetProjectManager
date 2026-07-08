namespace SiNet.Application.Email.Acc;

/// <summary>Host-provided close confirmation when ACC background work is active.</summary>
public interface IEmailAccClosePrompt
{
    bool ConfirmCloseIfNeeded(object? owner);
}
