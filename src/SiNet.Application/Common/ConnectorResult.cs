namespace SiNet.Application.Common;

/// <summary>
/// Result envelope for connector / infrastructure operations, allowing callers to handle
/// success, failure and retry without exceptions for expected error paths.
/// </summary>
/// <typeparam name="T">The value type produced on success.</typeparam>
public sealed class ConnectorResult<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public string? Error { get; init; }

    public bool ShouldRetry { get; init; }

    public static ConnectorResult<T> Ok(T value) =>
        new() { Success = true, Value = value };

    public static ConnectorResult<T> Fail(string error, bool shouldRetry = false) =>
        new() { Success = false, Error = error, ShouldRetry = shouldRetry };
}
