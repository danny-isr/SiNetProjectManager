namespace SiNet.Application.Tasks;

public sealed record TaskCommandResult(
    bool Succeeded,
    string Message,
    int? TaskId = null);
