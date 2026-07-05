namespace SiNet.Application.DevTools;

public sealed record DevDataResetTableResult(string TableName, int RowsDeleted, string? Error);
