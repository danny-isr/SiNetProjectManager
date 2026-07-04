using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SiNet.Infrastructure.Autodesk;
using SiNetSQL.Data;
using SiNetSQL.Services;
using SiNetSQL.Services.AccBootstrap;
using SiNetSQL.Services.AccBootstrap.Contracts;
using SiNetSQL.Services.Logging;
using SiOffice.AccService.Auth;
using SiOffice.AccService.Endpoints;

// ─────────────────────────────────────────────────────────────────────────────
//  CLI provisioning mode: --import-secret <key> <base64-utf8-value>
//
//  Used by the WPF SecretSetupWindow to write secrets to the vault under the
//  LocalSystem account (the same account the Windows service runs as), via a
//  one-shot scheduled task. Value is Base64-encoded UTF8 to avoid escaping
//  issues when the task scheduler passes the argument through cmd.
// ─────────────────────────────────────────────────────────────────────────────
if (args.Length >= 1 && args[0] == "--import-secret")
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("Usage: SiOffice.AccService.exe --import-secret <key> <base64-utf8-value>");
        return 2;
    }
    try
    {
        var key = args[1];
        var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args[2]));
        CredentialVaultService.SetSecret(key, value);
        Console.WriteLine($"OK: '{key}' written to vault for user '{Environment.UserName}'.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL: {ex.Message}");
        return 1;
    }
}

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

// Wire TokenProvider diagnostics (Gap 18B/18C) to the service's Serilog logger so
// [TokenProvider] lines (path, windows user, pid, clientIdTail, refreshTokenFileExists,
// browser-auth trigger, safe-delete decisions) reach the central log on the service side
// — including when the service runs under LocalSystem / a different Windows user than
// the WPF client and therefore resolves a different %LOCALAPPDATA% token store.
MyOffice.AutodeskConnector.TokenProvider.LogInfo = msg => Log.Information("{Msg}", msg);
MyOffice.AutodeskConnector.TokenProvider.LogWarn = msg => Log.Warning("{Msg}", msg);
MyOffice.AutodeskConnector.TokenProvider.LogError = msg => Log.Error("{Msg}", msg);

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
            // Production fallback: persist a self-signed cert next to the
            // executable so the service boots cleanly on Windows Server where
            // the ASP.NET Core dev-cert is not available. Replace with a real
            // cert by setting AccService:Certificate:Path/Password.
            var cert = LoadOrCreateSelfSignedCertificate();
            listen.UseHttps(cert);
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
builder.Services.AddSingleton<MyOffice.AutodeskConnector.ITokenProvider>(_ =>
{
    var clientId = SiNetSQL.Services.CredentialProvider.AutodeskClientId ?? string.Empty;
    var clientSecret = SiNetSQL.Services.CredentialProvider.AutodeskClientSecret ?? string.Empty;
    return new MyOffice.AutodeskConnector.TokenProvider(clientId, clientSecret);
});
builder.Services.AddSiNetAutodeskLocalFileTransfer();
builder.Services.AddTransient<IAccProjectProvisioningService, AccProjectProvisioningService>();

// NOTE: CredentialProvider.GetSecret was already set to CredentialVaultService.GetSecret
// at line 58 above. That wiring is correct and must NOT be overwritten here.
// The previous code (CredentialProvider.GetSecret = key => builder.Configuration[$"Secrets:{key}"])
// was a BUG that caused the service to read from empty appsettings instead of the vault.

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
    // Rich startup banner — confirms key configuration in the central log so an
    // operator can verify environment without attaching a debugger.
    var listenPort = builder.Configuration.GetValue<int?>("AccService:HttpsPort") ?? 8443;
    var hasApiKey = !string.IsNullOrWhiteSpace(
        CredentialVaultService.GetSecret(SecretKeys.AccServiceApiKey)
        ?? builder.Configuration["AccService:ApiKey"]);
    // Lifecycle lines are emitted at Warning level on purpose: the central
    // log share's default minimum level for AccService is Warning, so logging
    // service-up / service-down at Information would only land in the local
    // file. Warning guarantees they reach \\si-win-2k19\AutoCAD Data\log\…
    // even with the default DB settings.
    Log.Warning(
        "SiOffice.AccService starting — version {Version}, machine {Machine}, user {User}, https port {Port}, api key configured: {HasApiKey}.",
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "?",
        Environment.MachineName,
        Environment.UserName,
        listenPort,
        hasApiKey);

    // Resolved log targets — always emitted so the local log states exactly
    // which network folder/file the central log is being written to. Makes
    // "central folder is empty" trivial to diagnose.
    Log.Warning(
        "SiOffice.AccService log targets — local file: {LocalFile}, central file: {CentralFile}, central enabled: {CentralEnabled}.",
        CentralLoggingBuilder.LocalSinkTargetFile ?? "(none)",
        CentralLoggingBuilder.CentralSinkTargetFile ?? "(disabled — Logging.CentralLogPath empty)",
        CentralLoggingBuilder.CentralSinkEnabled);

    if (CentralLoggingBuilder.CentralSinkBootstrapError is { } centralErr)
    {
        Log.Warning("SiOffice.AccService: {Detail}", centralErr);
    }

    // [AccService][ApiKey] startup diagnostics — safe metadata for cross-machine key mismatch debugging.
    // NEVER logs the actual key value. Logs: hasKey, keyLength, SHA256 hash prefix (first 12 chars).
    try
    {
        var apiKeyRaw = CredentialVaultService.GetSecret(SecretKeys.AccServiceApiKey);
        var apiKeyFromConfig = builder.Configuration["AccService:ApiKey"];
        var effectiveKey = apiKeyRaw ?? apiKeyFromConfig;
        var keySource = apiKeyRaw != null ? "CredentialManager" : (apiKeyFromConfig != null ? "appsettings" : "none");
        var keyLength = effectiveKey?.Length ?? 0;
        var keyHashPrefix = "(none)";
        if (!string.IsNullOrEmpty(effectiveKey))
        {
            var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(effectiveKey));
            keyHashPrefix = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
        }
        string windowsUserForKey;
        try { windowsUserForKey = Environment.UserDomainName + "\\" + Environment.UserName; }
        catch { windowsUserForKey = "(unknown)"; }
        Log.Warning(
            "[AccService][ApiKey] startup diagnostics — windowsUser={WindowsUser}, hasApiKey={HasKey}, " +
            "keySource={KeySource}, keyLength={KeyLength}, keyHashPrefix={KeyHashPrefix}.",
            windowsUserForKey, effectiveKey != null, keySource, keyLength, keyHashPrefix);
    }
    catch (Exception apiKeyDiagEx)
    {
        Log.Warning("[AccService][ApiKey] startup diagnostics failed: {Error}", apiKeyDiagEx.Message);
    }

    // [AccService][TokenProvider] startup diagnostics — answers the cross-process
    // questions raised in Gap 18A/18C: which Windows user the service runs as,
    // which %LOCALAPPDATA% it sees, and whether the Autodesk refresh_token.json is
    // already present on disk for it. No secrets are printed (only last 4 chars of
    // the configured client_id, and only whether the file exists — never its content).
    try
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var tokenDir = System.IO.Path.Combine(localAppData, "SiNet", "Autodesk");
        var tokenPath = System.IO.Path.Combine(tokenDir, "refresh_token.json");
        var clientIdRaw = CredentialProvider.AutodeskClientId ?? string.Empty;
        var clientIdTail = clientIdRaw.Length < 4 ? "(empty)" : "***" + clientIdRaw[^4..];
        string windowsUser;
        try { windowsUser = Environment.UserDomainName + "\\" + Environment.UserName; }
        catch { windowsUser = "(unknown)"; }
        Log.Warning(
            "[AccService][TokenProvider] startup diagnostics — windowsUser={WindowsUser}, " +
            "process={Process} (pid={Pid}), environment={EnvName}, " +
            "currentDirectory={Cwd}, baseDirectory={BaseDir}, localAppData={LocalAppData}, " +
            "tokenStoragePath={TokenPath}, refreshTokenFileExists={Exists}, clientIdTail={ClientIdTail}.",
            windowsUser,
            System.Diagnostics.Process.GetCurrentProcess().ProcessName,
            Environment.ProcessId,
            builder.Environment.EnvironmentName,
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            localAppData,
            tokenPath,
            System.IO.File.Exists(tokenPath),
            clientIdTail);
    }
    catch (Exception diagEx)
    {
        Log.Warning("[AccService][TokenProvider] startup diagnostics failed: {Error}", diagEx.Message);
    }

    // Hook the host lifetime so we get explicit started / stopping / stopped lines.
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStarted.Register(() =>
        Log.Warning("SiOffice.AccService started — listening on https://*:{Port}.", listenPort));
    lifetime.ApplicationStopping.Register(() =>
        Log.Warning("SiOffice.AccService stopping..."));
    lifetime.ApplicationStopped.Register(() =>
        Log.Warning("SiOffice.AccService stopped."));

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

return 0;

static X509Certificate2 LoadOrCreateSelfSignedCertificate()
{
    var certFile = Path.Combine(AppContext.BaseDirectory, "accservice.pfx");
    const string password = "siofficeaccservice"; // local-only, file is ACL'd to LocalSystem
    if (File.Exists(certFile))
    {
        return X509CertificateLoader.LoadPkcs12FromFile(
            certFile, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
    }

    using var rsa = RSA.Create(2048);
    var req = new CertificateRequest(
        $"CN={Environment.MachineName}",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);

    var sanBuilder = new SubjectAlternativeNameBuilder();
    sanBuilder.AddDnsName(Environment.MachineName);
    sanBuilder.AddDnsName("localhost");
    req.CertificateExtensions.Add(sanBuilder.Build());
    req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
    req.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
    req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
        new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // Server Auth

    using var generated = req.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddYears(10));

    var pfxBytes = generated.Export(X509ContentType.Pfx, password);
    File.WriteAllBytes(certFile, pfxBytes);

    return X509CertificateLoader.LoadPkcs12(
        pfxBytes, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
}
