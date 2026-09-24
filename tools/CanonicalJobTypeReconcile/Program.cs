using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNetSQL.Data;

var apply = args.Any(a => string.Equals(a, "--apply", StringComparison.OrdinalIgnoreCase));
var connection = ReadArg(args, "--connection")
    ?? Environment.GetEnvironmentVariable("SINET_RECONCILE_CONNECTION");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("Pass --connection or set SINET_RECONCILE_CONNECTION. Default mode is dry-run.");
    return 2;
}

var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
    .UseSqlServer(connection, sql => sql.CommandTimeout(300))
    .Options;
await using var db = new SiNetSQLDbContext(options);
var preview = await CanonicalJobTypeReconciliation.PreviewAsync(db, CancellationToken.None);
Console.WriteLine($"ReviewJobTypeId={preview.ReviewJobTypeId?.ToString() ?? "missing"}");
Console.WriteLine($"LegacyJobTypeIds={string.Join(",", preview.LegacyJobTypeIds)}");
Console.WriteLine($"OpinionJobTypeId={preview.OpinionJobTypeId?.ToString() ?? "missing"}");
Console.WriteLine($"Conflicts: {preview.Conflicts.Count}");
foreach (var conflict in preview.Conflicts)
    Console.WriteLine(conflict);

if (!apply)
{
    Console.WriteLine("Dry-run only. No rows were written. Pass --apply to write when Conflicts is 0.");
    return preview.Conflicts.Count == 0 ? 0 : 3;
}

if (preview.Conflicts.Count > 0)
{
    Console.Error.WriteLine("Apply refused. Resolve the conflicts first. No rows were written.");
    return 3;
}

await CanonicalJobTypeReconciliation.ApplyAsync(db, CancellationToken.None);
Console.WriteLine("Apply completed.");
return 0;

static string? ReadArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}
