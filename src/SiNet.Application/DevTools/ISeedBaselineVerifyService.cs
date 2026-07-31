namespace SiNet.Application.DevTools;

/// <summary>
/// Read-only check that essential basic-seed Codes still exist in SQL.
/// Does not write and does not run seed.
/// </summary>
public interface ISeedBaselineVerifyService
{
    Task<SeedBaselineVerifyResult> VerifyAsync(CancellationToken cancellationToken = default);
}
