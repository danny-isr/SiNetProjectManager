using Microsoft.EntityFrameworkCore;
using Serilog;
using SiNetSQL.Data;
using SiNetSQL.Services;
using SiNetSQL.Services.AccBootstrap;
using SiNetSQL.Services.AccBootstrap.Contracts;
using SiNetSQL.Services.Logging;
using SiOffice.AccService.Auth;
using SiOffice.AccService.Endpoints;

// ─────────────────────────────────────────────────────────────────────────────
//  SiOffice.AccService — Privileged Operations Service
//
//  Centralizes all ACC operations that require Account Admin / Project Admin /
//  Folder CONTROL. Runs as a Windows Service on the office Windows Server 2019.
//  Regular employees use the WPF client which calls this service over HTTPS
//  using an API key, instead of holding admin credentials themselves.
//
//  Phase B scaffold: a single live endpoint (GET /v1/acc/templates) wired
//  end-to-end through IAccProjectProvisioningService, plus /v1/acc/health.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Run as a Windows Service when launched by the SCM. Harmless when launched
// from a console (UseWindowsService no-ops in that case).
builder.Host.UseWindowsService(o => o.ServiceName = "SiOffice.AccService");

// ─── Credential vault bridge (Windows Credential Manager, same as WPF client) ─
// All secrets — Autodesk client id/secret, connection strings, etc. — live in the
// machine-scoped Windows Credential Manager and are looked up by the well-known
// keys in SiNetSQL.Services.SecretKeys. Wiring this BEFORE building services so
// any DI factory that resolves credentials at construction time gets them.
CredentialProvider.GetSecret = CredentialVaultService.GetSecret;

// ─── Logging: Serilog with shared SiNet sinks layout ────────────────────────
// Settings (central path, levels, retention) come from the SystemSettings table
// in SQL — single source of truth shared with the WPF client. Falls back to
// compile-time defaults when the DB is unreachable so the service still boots.
var loggingConnectionString =
    CredentialVaultService.GetSecret(SecretKeys.SiNetDatabase)
    ?? builder.Configuration.GetConnectionString("SiNetDatabase");

var loggingConfig = CentralLoggingSettings.LoadFromDatabase(
    loggingConnectionString,
    SiNetApp.AccService,
    enableConsole: true);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .AddSiNetCentralLogging(loggingConfig)
    .CreateLogger();
builder.Host.UseSerilog();

// ─── Kestrel HTTPS ──────────────────────────────────────────────────────────
// Internal-network service. HTTPS port + optional cert path/password are
// driven from configuration so production can swap in a real PFX without
// touching code. When no cert is configured Kestrel falls back to the dev
// certificate (developer machine only).
builder.WebHost.ConfigureKestrel((ctx, kestrel) =>
{
    var port = ctx.Configuration.GetValue<int?>("AccService:HttpsPort") ?? 8443;
    var certPath = ctx.Configuration["AccService:Certificate:Path"];
    var certPassword = ctx.Configuration["AccService:Certificate:Password"];

    kestrel.ListenAnyIP(port, listen =>
    {
        if (!string.IsNullOrWhiteSpace(certPath) && File.Exists(certPath))
        {
            listen.UseHttps(certPath, certPassword);
        }
        else
        {
            listen.UseHttps();
        }
    });
});

// ─── Database: shared SiNetSQL context, factory pattern (same as WPF client) ─
// Resolution order matches AppConfiguration.GetConnectionString in the WPF app:
//   1. Vault key  SiNet/ConnectionStrings/SiNetDatabase
//   2. appsettings.json  ConnectionStrings:SiNetDatabase  (fallback / dev only)
var connectionString =
    CredentialVaultService.GetSecret(SecretKeys.SiNetDatabase)
    ?? builder.Configuration.GetConnectionString("SiNetDatabase")
    ?? throw new InvalidOperationException(
        "Missing connection string 'SiNetDatabase'. Provision it in Windows Credential Manager " +
        $"under target '{SecretKeys.SiNetDatabase}' (use the WPF client's secret-setup dialog or SecretProvisioningService).");

builder.Services.AddDbContextFactory<SiNetSQLDbContext>(options =>
{
    // UseCompatibilityLevel(120) matches the WPF client — the SQL Server
    // database is below compat 130 so OPENJSON-based translation must be off.
    options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(120));
});

// ─── Application services from SiNetSQL ─────────────────────────────────────
builder.Services.AddSingleton<SystemSettingsService>();
builder.Services.AddTransient<IAccProjectProvisioningService, AccProjectProvisioningService>();

// ─── Autodesk credentials bridge ────────────────────────────────────────────
// Wire the static CredentialProvider used by AccProjectProvisioningService /
// Bim360Service. Reads from configuration first (Secrets:<key>), then falls
// back to environment variables (built-in to CredentialProvider).
CredentialProvider.GetSecret = key => builder.Configuration[$"Secrets:{key}"];

var app = builder.Build();

// ─── Middleware ─────────────────────────────────────────────────────────────
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers[AccServiceContracts.ApiVersionHeader] = "1.0";
    await next();
});

app.UseMiddleware<ApiKeyMiddleware>();

// ─── Endpoints ──────────────────────────────────────────────────────────────
app.MapAccEndpoints();

try
{
    Log.Information("SiOffice.AccService starting...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SiOffice.AccService terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
