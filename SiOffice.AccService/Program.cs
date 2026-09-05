using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using SiNetSQL.Services.AccBootstrap;
using SiOffice.AccService.Contracts;
using SiOffice.AccService;
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
        CredentialVault.SetSecret(key, value);
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

// ─── Logging: Serilog with shared SiNet sinks layout ────────────────────────
// Settings (central path, levels, retention) come from the SystemSettings table
// in SQL — single source of truth shared with the WPF client. Falls back to
// compile-time defaults when the DB is unreachable so the service still boots.
var loggingConnectionString =
    CredentialVault.GetSecret(SecretCatalog.SiNetDatabase)
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
// Internal-network service. Resolution order (see LoadTlsCertificate):
// store thumbprint → explicit PFX path → vault-backed self-signed PFX
// (SiNet/AccService/CertificatePassword). No purchased CA required.
builder.WebHost.ConfigureKestrel((ctx, kestrel) =>
{
    var port = ctx.Configuration.GetValue<int?>("AccService:HttpsPort") ?? 8443;

    kestrel.ListenAnyIP(port, listen =>
    {
        var cert = LoadTlsCertificate(ctx.Configuration);
        AccServiceRuntimeTlsState.CertificateThumbprint = cert.Thumbprint?
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        listen.UseHttps(cert);
    });
});

// ─── Database: Infrastructure.Sql factory (same SiNetSQLDbContext / compat-120) ─
// Resolution order matches AppConfiguration.GetConnectionString in the WPF app:
//   1. Vault key  SiNet/ConnectionStrings/SiNetDatabase
//   2. appsettings.json  ConnectionStrings:SiNetDatabase  (fallback / dev only)
var connectionString =
    CredentialVault.GetSecret(SecretCatalog.SiNetDatabase)
    ?? builder.Configuration.GetConnectionString("SiNetDatabase")
    ?? throw new InvalidOperationException(
        "Missing connection string 'SiNetDatabase'. Provision it in Windows Credential Manager " +
        $"under target '{SecretCatalog.SiNetDatabase}' (use the WPF client's secret-setup dialog or SecretProvisioningService).");

builder.Services.AddSiNetSql(connectionString);
builder.Services.AddSiNetAuthorizationSql();
builder.Services.AddSiNetSystemSettingsSql();

// ─── Application services (provisioning/bootstrap now decoupled from SiNetSQL, B4) ──
// AccProjectProvisioningService and every AccService-owned settings/credential read use
// the Application ports directly — ISystemSettingsQueryService (AddSiNetSystemSettingsSql
// above) and CredentialVault + SecretCatalog. The legacy credential-provider and
// system-settings-service bridges to the SiNetSQL assembly are gone.
builder.Services.AddSingleton<MyOffice.AutodeskConnector.ITokenProvider>(_ =>
{
    var clientId = CredentialVault.GetSecret(SecretCatalog.AutodeskClientId) ?? string.Empty;
    var clientSecret = CredentialVault.GetSecret(SecretCatalog.AutodeskClientSecret) ?? string.Empty;
    return new MyOffice.AutodeskConnector.TokenProvider(clientId, clientSecret);
});
builder.Services.AddSiNetAutodeskLocalFileTransfer();
builder.Services.AddTransient<IAccProjectProvisioningService, AccProjectProvisioningService>();

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
        CredentialVault.GetSecret(SecretCatalog.AccServiceApiKey)
        ?? builder.Configuration["AccService:ApiKey"]);
    // Lifecycle lines are emitted at Warning level on purpose: the central
    // log share's default minimum level for AccService is Warning, so logging
    // service-up / service-down at Information would only land in the local
    // file. Warning guarantees they reach \\si-win-2k19\AutoCAD Data\log\…
    // even with the default DB settings.
    Log.Warning(
        "SiOffice.AccService starting — version {Version}, machine {Machine}, user {User}, https port {Port}, api key configured: {HasApiKey}, certificate thumbprint: {CertThumbprint}.",
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "?",
        Environment.MachineName,
        Environment.UserName,
        listenPort,
        hasApiKey,
        AccServiceRuntimeTlsState.CertificateThumbprint ?? "(unknown)");

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

    // [AccService][ApiKey] startup diagnostics — presence and source only.
    // Key length and hash prefixes are secret fingerprints and are deliberately not logged.
    try
    {
        var apiKeyRaw = CredentialVault.GetSecret(SecretCatalog.AccServiceApiKey);
        var apiKeyFromConfig = builder.Configuration["AccService:ApiKey"];
        var effectiveKey = apiKeyRaw ?? apiKeyFromConfig;
        var keySource = apiKeyRaw != null ? "CredentialManager" : (apiKeyFromConfig != null ? "appsettings" : "none");
        string windowsUserForKey;
        try { windowsUserForKey = Environment.UserDomainName + "\\" + Environment.UserName; }
        catch { windowsUserForKey = "(unknown)"; }
        Log.Warning(
            "[AccService][ApiKey] startup diagnostics — windowsUser={WindowsUser}, hasApiKey={HasKey}, " +
            "keySource={KeySource}.",
            windowsUserForKey, effectiveKey != null, keySource);
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
        var clientIdRaw = CredentialVault.GetSecret(SecretCatalog.AutodeskClientId) ?? string.Empty;
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

    // [AccService][AdminIdentity] — expected vs connected Autodesk Admin profile (emails only; never tokens).
    try
    {
        string? expectedAdmin = null;
        try
        {
            var settings = app.Services.GetRequiredService<SiNet.Application.Settings.ISystemSettingsQueryService>()
                .GetSystemSettingsAsync()
                .GetAwaiter()
                .GetResult();
            expectedAdmin = settings.Acc.AccServiceExpectedAdminEmail;
        }
        catch (Exception settingsEx)
        {
            Log.Warning("[AccService][AdminIdentity] SystemSettings read failed: {Error}", settingsEx.Message);
        }

        expectedAdmin = string.IsNullOrWhiteSpace(expectedAdmin)
            ? builder.Configuration["AccService:ExpectedAdminEmail"]
            : expectedAdmin;

        string? connectedEmail = null;
        try
        {
            var tokenProvider = app.Services.GetRequiredService<MyOffice.AutodeskConnector.ITokenProvider>();
            var accessToken = tokenProvider.GetThreeLeggedAdminTokenAsync().GetAwaiter().GetResult();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.userprofile.autodesk.com/userinfo");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = http.Send(req);
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var m = System.Text.RegularExpressions.Regex.Match(body, "\"email\"\\s*:\\s*\"([^\"]+)\"");
            if (m.Success)
            {
                connectedEmail = m.Groups[1].Value;
            }
        }
        catch (Exception profileEx)
        {
            Log.Warning("[AccService][AdminIdentity] Autodesk profile resolve failed: {Error}", profileEx.Message);
        }

        var check = SiNet.Application.Identity.AccServiceAdminIdentity.Evaluate(expectedAdmin, connectedEmail);
        Log.Warning(
            "[AccService][AdminIdentity] expected={Expected}, connected={Connected}, status={Status}",
            check.ExpectedAdminEmail ?? "(unset)",
            check.ConnectedProfileEmail ?? "(unavailable)",
            check.Status);
        if (check.WarningMessage is not null)
        {
            Log.Warning("{Warning}", check.WarningMessage);
        }
    }
    catch (Exception adminIdEx)
    {
        Log.Warning("[AccService][AdminIdentity] startup diagnostics failed: {Error}", adminIdEx.Message);
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

static X509Certificate2 LoadTlsCertificate(IConfiguration configuration)
{
    var certSection = configuration.GetSection("AccService:Certificate");
    var storeName = certSection["StoreName"];
    var thumbprint = certSection["Thumbprint"];
    var certPath = certSection["Path"];
    var certPassword =
        CredentialVault.GetSecret(SecretCatalog.AccServiceCertificatePassword)
        ?? certSection["Password"];

    if (!string.IsNullOrWhiteSpace(storeName) && !string.IsNullOrWhiteSpace(thumbprint))
    {
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var normalizedThumbprint = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalizedThumbprint, validOnly: false);
        if (matches.Count > 0)
        {
            return matches[0];
        }

        throw new InvalidOperationException(
            $"TLS certificate with thumbprint '{normalizedThumbprint}' was not found in store '{storeName}'.");
    }

    if (!string.IsNullOrWhiteSpace(certPath) && File.Exists(certPath))
    {
        if (string.IsNullOrWhiteSpace(certPassword))
        {
            throw new InvalidOperationException(
                "AccService:Certificate:Password (or vault key 'SiNet/AccService/CertificatePassword') " +
                "is required when AccService:Certificate:Path points to a PFX file.");
        }

        return OpenPfx(certPath, certPassword);
    }

    // Supported path for Dev and office server without a purchased CA:
    // ensure a vault password exists (bootstrap if missing), then load/create accservice.pfx.
    certPassword = EnsureAccServiceCertificatePassword(certPassword);
    return LoadOrCreateSelfSignedCertificate(certPassword);
}

static string EnsureAccServiceCertificatePassword(string? existingPassword)
{
    if (!string.IsNullOrWhiteSpace(existingPassword))
    {
        return existingPassword;
    }

    var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    CredentialVault.SetSecret(SecretCatalog.AccServiceCertificatePassword, generated);
    Log.Warning(
        "AccService CertificatePassword was missing in the vault for user {User}; " +
        "generated a new value and stored it under '{Key}'. " +
        "After startup, copy the certificate thumbprint into System Setting AccService.PinnedCertificateThumbprints.",
        Environment.UserName,
        SecretCatalog.AccServiceCertificatePassword);
    return generated;
}

static X509Certificate2 LoadOrCreateSelfSignedCertificate(string certPassword)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(certPassword);

    var certFile = Path.Combine(AppContext.BaseDirectory, "accservice.pfx");
    if (File.Exists(certFile))
    {
        try
        {
            return OpenPfx(certFile, certPassword);
        }
        catch (InvalidOperationException ex)
        {
            // Typical after CertificatePassword was rotated or first-boot bootstrap replaced an old PFX password.
            Log.Warning(
                ex,
                "Existing {CertFile} could not be opened with the current CertificatePassword; recreating the PFX.",
                certFile);
            File.Delete(certFile);
        }
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

    var pfxBytes = generated.Export(X509ContentType.Pfx, certPassword);
    File.WriteAllBytes(certFile, pfxBytes);

    return X509CertificateLoader.LoadPkcs12(
        pfxBytes,
        certPassword,
        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
}

static X509Certificate2 OpenPfx(string certFile, string certPassword)
{
    try
    {
        return X509CertificateLoader.LoadPkcs12FromFile(
            certFile,
            certPassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
    }
    catch (CryptographicException ex)
    {
        throw new InvalidOperationException(
            $"Failed to open '{certFile}' with the configured CertificatePassword. " +
            "If the password was rotated in Secret Setup, delete the PFX so AccService can create a new one, " +
            "then update AccService.PinnedCertificateThumbprints on clients.",
            ex);
    }
}
