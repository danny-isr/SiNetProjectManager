namespace SiNet.Application.DevTools;

public sealed class SeedResult
{
    public bool Succeeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = [];
}
