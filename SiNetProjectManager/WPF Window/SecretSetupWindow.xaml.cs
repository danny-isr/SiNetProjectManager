using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SiNetProjectManager.Services;
using SiNetSQL.Services;

namespace SiNetProjectManager.WPF_Window;

/// <summary>
/// Setup dialog for configuring application secrets in Windows Credential Manager.
/// Shown on first launch or when secrets are missing.
/// Each secret is encrypted per-user via DPAPI — only the current Windows user can access them.
/// </summary>
public partial class SecretSetupWindow : Window
{
    private static readonly Brush _greenBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
    private static readonly Brush _orangeBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0));
    private static readonly Brush _redBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));

    /// <summary>Content of the selected credentials.json file (stored in vault, not as file path).</summary>
    private string? _googleCredentialsContent;

    public SecretSetupWindow()
    {
        InitializeComponent();
        Loaded += SecretSetupWindow_Loaded;
    }

    private void SecretSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshStatusIndicators();
        PreFillExistingValues();
    }

    /// <summary>
    /// Updates the colored status dots for each secret (green = exists, red = missing).
    /// </summary>
    private void RefreshStatusIndicators()
    {
        var status = CredentialVaultService.GetVaultStatus();

        StatusGemini.Fill = status.GetValueOrDefault(SecretKeys.GeminiApiKey) ? _greenBrush : _redBrush;
        StatusAdClientId.Fill = status.GetValueOrDefault(SecretKeys.AutodeskClientId) ? _greenBrush : _redBrush;
        StatusAdClientSecret.Fill = status.GetValueOrDefault(SecretKeys.AutodeskClientSecret) ? _greenBrush : _redBrush;
        StatusGoogleSecrets.Fill = status.GetValueOrDefault(SecretKeys.GoogleClientSecrets) ? _greenBrush : _redBrush;
        StatusCsSiNet.Fill = status.GetValueOrDefault(SecretKeys.SiNetDatabase) ? _greenBrush : _redBrush;
        StatusCsReplica.Fill = status.GetValueOrDefault(SecretKeys.ReplicaDatabase) ? _greenBrush : _redBrush;
        StatusCsMasterPlan.Fill = status.GetValueOrDefault(SecretKeys.MasterPlanDatabase) ? _greenBrush : _redBrush;
        StatusAdUser.Fill = status.GetValueOrDefault(SecretKeys.AdUsername) ? _greenBrush : _redBrush;
        StatusAdPass.Fill = status.GetValueOrDefault(SecretKeys.AdPassword) ? _greenBrush : _redBrush;
    }

    /// <summary>
    /// Pre-fills text boxes with existing vault values so the user can see/edit them.
    /// PasswordBox fields are not pre-filled for security (only status dot shows they exist).
    /// </summary>
    private void PreFillExistingValues()
    {
        // Non-sensitive fields: pre-fill from vault
        TxtAdClientId.Text = CredentialVaultService.GetSecret(SecretKeys.AutodeskClientId) ?? "";
        TxtCsSiNet.Text = CredentialVaultService.GetSecret(SecretKeys.SiNetDatabase) ?? "";
        TxtCsReplica.Text = CredentialVaultService.GetSecret(SecretKeys.ReplicaDatabase) ?? "";
        TxtCsMasterPlan.Text = CredentialVaultService.GetSecret(SecretKeys.MasterPlanDatabase) ?? "";

        // Active Directory username: pre-fill from vault
        TxtAdUsername.Text = CredentialVaultService.GetSecret(SecretKeys.AdUsername) ?? "";

        // Google credentials: show status text
        if (CredentialVaultService.HasSecret(SecretKeys.GoogleClientSecrets))
        {
            TxtGoogleCredentialsPath.Text = "(מוגדר ב-Vault)";
        }
    }

    private void BtnBrowseCredentials_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "בחר קובץ credentials.json",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _googleCredentialsContent = File.ReadAllText(dialog.FileName);
                TxtGoogleCredentialsPath.Text = dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"שגיאה בקריאת הקובץ:\n{ex.Message}",
                    "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        Cursor = System.Windows.Input.Cursors.Wait;
        BtnSave.IsEnabled = false;

        try
        {
            var saved = 0;

            // Gemini API Key
            var geminiKey = TxtGeminiApiKey.Password;
            if (!string.IsNullOrWhiteSpace(geminiKey))
            {
                CredentialVaultService.SetSecret(SecretKeys.GeminiApiKey, geminiKey);
                saved++;
            }

            // Autodesk Client ID
            if (!string.IsNullOrWhiteSpace(TxtAdClientId.Text))
            {
                CredentialVaultService.SetSecret(SecretKeys.AutodeskClientId, TxtAdClientId.Text.Trim());
                saved++;
            }

            // Autodesk Client Secret
            var adSecret = TxtAdClientSecret.Password;
            if (!string.IsNullOrWhiteSpace(adSecret))
            {
                CredentialVaultService.SetSecret(SecretKeys.AutodeskClientSecret, adSecret);
                saved++;
            }

            // Google Client Secrets (credentials.json content)
            if (!string.IsNullOrWhiteSpace(_googleCredentialsContent))
            {
                CredentialVaultService.SetSecret(SecretKeys.GoogleClientSecrets, _googleCredentialsContent);
                saved++;
            }

            // Connection Strings (normalize backslashes — users often paste from JSON/C# with escaped \\)
            if (!string.IsNullOrWhiteSpace(TxtCsSiNet.Text))
            {
                CredentialVaultService.SetSecret(SecretKeys.SiNetDatabase, NormalizeConnectionString(TxtCsSiNet.Text));
                saved++;
            }

            if (!string.IsNullOrWhiteSpace(TxtCsReplica.Text))
            {
                CredentialVaultService.SetSecret(SecretKeys.ReplicaDatabase, NormalizeConnectionString(TxtCsReplica.Text));
                saved++;
            }

            if (!string.IsNullOrWhiteSpace(TxtCsMasterPlan.Text))
            {
                CredentialVaultService.SetSecret(SecretKeys.MasterPlanDatabase, NormalizeConnectionString(TxtCsMasterPlan.Text));
                saved++;
            }

            // Active Directory Username
            if (!string.IsNullOrWhiteSpace(TxtAdUsername.Text))
            {
                CredentialVaultService.SetSecret(SecretKeys.AdUsername, TxtAdUsername.Text.Trim());
                saved++;
            }

            // Active Directory Password
            var adDomainPass = TxtAdPassword.Password;
            if (!string.IsNullOrWhiteSpace(adDomainPass))
            {
                CredentialVaultService.SetSecret(SecretKeys.AdPassword, adDomainPass);
                saved++;
            }

            // ═══════════════════════════════════════════════════════════════════
            // VALIDATION: Test ALL secrets against their actual services.
            // Green = verified working, Orange = saved but test failed, Red = missing.
            // All network tests run concurrently for faster feedback.
            // ═══════════════════════════════════════════════════════════════════
            var passed = new List<string>();
            var failed = new List<string>();

            // Launch all validations concurrently
            var connSiNetTask = Task.Run(() => TestConnectionFromVault(SecretKeys.SiNetDatabase));
            var connReplicaTask = Task.Run(() => TestConnectionFromVault(SecretKeys.ReplicaDatabase));
            var connMasterPlanTask = Task.Run(() => TestConnectionFromVault(SecretKeys.MasterPlanDatabase));
            var geminiTask = TestGeminiFromVaultAsync();
            var autodeskTask = TestAutodeskFromVaultAsync();
            var googleResult = TestGoogleFromVault(); // synchronous — local JSON parsing
            var adTask = Task.Run(TestAdFromVault);

            await Task.WhenAll(connSiNetTask, connReplicaTask, connMasterPlanTask, geminiTask, autodeskTask, adTask);

            // Apply results to UI dots (must run on UI thread)
            ApplyResult(StatusCsSiNet, "SiNet DB", connSiNetTask.Result, passed, failed);
            ApplyResult(StatusCsReplica, "Replica DB", connReplicaTask.Result, passed, failed);
            ApplyResult(StatusCsMasterPlan, "MasterPlan DB", connMasterPlanTask.Result, passed, failed);
            ApplyResult(StatusGemini, "Gemini API", geminiTask.Result, passed, failed);
            ApplyResult(StatusGoogleSecrets, "Google OAuth", googleResult, passed, failed);

            // Paired secrets: both dots share the validation result
            ApplyPairResult(StatusAdClientId, StatusAdClientSecret,
                SecretKeys.AutodeskClientId, SecretKeys.AutodeskClientSecret,
                "Autodesk APS", autodeskTask.Result, passed, failed);

            ApplyPairResult(StatusAdUser, StatusAdPass,
                SecretKeys.AdUsername, SecretKeys.AdPassword,
                "Active Directory", adTask.Result, passed, failed);

            // Build validation summary
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"נשמרו {saved} מפתחות ב-Credential Manager.");

            if (passed.Count > 0)
            {
                sb.AppendLine();
                foreach (var p in passed)
                    sb.AppendLine($"✅ {p}");
            }

            if (failed.Count > 0)
            {
                sb.AppendLine();
                foreach (var f in failed)
                    sb.AppendLine($"❌ {f}");
            }

            if (failed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ניתן לתקן ולשמור שוב, או ללחוץ 'בטל' לסגירה.");
                MessageBox.Show(sb.ToString(), "⚠ חלק מהבדיקות נכשלו",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                // Stay open — let user fix and re-save
            }
            else
            {
                MessageBox.Show(sb.ToString(), "✅ נשמר ונבדק בהצלחה",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירת המפתחות:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Restore cursor (only reached if window stays open)
        Cursor = System.Windows.Input.Cursors.Arrow;
        BtnSave.IsEnabled = true;
    }

    /// <summary>
    /// Normalizes a connection string pasted by users.
    /// Fixes double backslash in server names (e.g., from JSON or C# copy-paste)
    /// and ensures TrustServerCertificate=True for Microsoft.Data.SqlClient 5.x+ compatibility.
    /// </summary>
    private static string NormalizeConnectionString(string raw)
    {
        var trimmed = raw.Trim();
        try
        {
            var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(trimmed);
            if (csb.DataSource.Contains("\\\\"))
                csb.DataSource = csb.DataSource.Replace("\\\\", "\\");
            if (!csb.TrustServerCertificate)
                csb.TrustServerCertificate = true;
            return csb.ConnectionString;
        }
        catch
        {
            // If parsing fails, return trimmed original
            return trimmed;
        }
    }

    /// <summary>
    /// Tests a database connection by opening and immediately closing a connection.
    /// Uses a 5-second timeout to avoid long waits on unreachable servers.
    /// </summary>
    private static bool TestConnectionString(string connectionString, out string? error)
    {
        error = null;
        try
        {
            var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = 5
            };
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(csb.ConnectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Validation tests — run on background threads, return results only.
    // UI updates are applied afterwards on the UI thread via ApplyResult.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests a database connection string from the vault.
    /// </summary>
    private static (bool exists, bool success, string? detail) TestConnectionFromVault(string secretKey)
    {
        if (!CredentialVaultService.HasSecret(secretKey))
            return (false, false, null);

        var connStr = CredentialVaultService.GetSecret(secretKey)!;
        if (TestConnectionString(connStr, out var error))
        {
            try
            {
                var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);
                return (true, true, $"{csb.DataSource}/{csb.InitialCatalog}");
            }
            catch { return (true, true, null); }
        }
        return (true, false, error);
    }

    /// <summary>
    /// Tests the Gemini API key by listing available models (no token consumption).
    /// </summary>
    private static async Task<(bool exists, bool success, string? detail)> TestGeminiFromVaultAsync()
    {
        if (!CredentialVaultService.HasSecret(SecretKeys.GeminiApiKey))
            return (false, false, null);

        var apiKey = CredentialVaultService.GetSecret(SecretKeys.GeminiApiKey)!;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}");

            if (response.IsSuccessStatusCode)
                return (true, true, "Gemini API");

            var body = await response.Content.ReadAsStringAsync();
            return (true, false, $"HTTP {(int)response.StatusCode}: {Truncate(body, 150)}");
        }
        catch (Exception ex) { return (true, false, ex.Message); }
    }

    /// <summary>
    /// Tests Autodesk credentials by requesting a 2-legged OAuth token (client_credentials).
    /// Both Client ID and Client Secret must be present in the vault.
    /// </summary>
    private static async Task<(bool bothExist, bool success, string? detail)> TestAutodeskFromVaultAsync()
    {
        var hasId = CredentialVaultService.HasSecret(SecretKeys.AutodeskClientId);
        var hasSecret = CredentialVaultService.HasSecret(SecretKeys.AutodeskClientSecret);

        if (!hasId || !hasSecret)
        {
            var missing = !hasId && !hasSecret ? null
                : !hasId ? "חסר Client ID" : "חסר Client Secret";
            return (false, false, missing);
        }

        var clientId = CredentialVaultService.GetSecret(SecretKeys.AutodeskClientId)!;
        var clientSecret = CredentialVaultService.GetSecret(SecretKeys.AutodeskClientSecret)!;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var authBytes = System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}");
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://developer.api.autodesk.com/authentication/v2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(authBytes));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = "data:read"
            });
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
                return (true, true, "Autodesk APS");

            var body = await response.Content.ReadAsStringAsync();
            return (true, false, $"HTTP {(int)response.StatusCode}: {Truncate(body, 150)}");
        }
        catch (Exception ex) { return (true, false, ex.Message); }
    }

    /// <summary>
    /// Validates Google OAuth credentials JSON structure.
    /// Checks for valid 'installed' or 'web' type with required client_id and client_secret fields.
    /// (Full OAuth flow requires browser interaction — structural validation is the best we can do here.)
    /// </summary>
    private static (bool exists, bool success, string? detail) TestGoogleFromVault()
    {
        if (!CredentialVaultService.HasSecret(SecretKeys.GoogleClientSecrets))
            return (false, false, null);

        var json = CredentialVaultService.GetSecret(SecretKeys.GoogleClientSecrets)!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("installed", out var section) ||
                root.TryGetProperty("web", out section))
            {
                if (!section.TryGetProperty("client_id", out _))
                    return (true, false, "חסר client_id ב-JSON");
                if (!section.TryGetProperty("client_secret", out _))
                    return (true, false, "חסר client_secret ב-JSON");
                return (true, true, "Google OAuth");
            }
            return (true, false, "JSON לא מכיל 'installed' או 'web' — אינו credentials.json תקין");
        }
        catch (Exception ex) { return (true, false, $"JSON לא תקין: {ex.Message}"); }
    }

    /// <summary>
    /// Tests Active Directory credentials via LDAP SimpleBind.
    /// Both username and password must be present in the vault.
    /// Uses the configured domain name from appsettings.json, or auto-detects.
    /// </summary>
    private static (bool bothExist, bool success, string? detail) TestAdFromVault()
    {
        var hasUser = CredentialVaultService.HasSecret(SecretKeys.AdUsername);
        var hasPass = CredentialVaultService.HasSecret(SecretKeys.AdPassword);

        if (!hasUser || !hasPass)
        {
            var missing = !hasUser && !hasPass ? null
                : !hasUser ? "חסר שם משתמש" : "חסרה סיסמה";
            return (false, false, missing);
        }

        var username = CredentialVaultService.GetSecret(SecretKeys.AdUsername)!;
        var password = CredentialVaultService.GetSecret(SecretKeys.AdPassword)!;
        try
        {
            var domainName = AppConfiguration.AdDomainName;
            using var context = !string.IsNullOrEmpty(domainName)
                ? new PrincipalContext(ContextType.Domain, domainName)
                : new PrincipalContext(ContextType.Domain);

            if (context.ValidateCredentials(username, password, ContextOptions.SimpleBind))
                return (true, true, username);
            return (true, false, "שם משתמש או סיסמה שגויים");
        }
        catch (PrincipalServerDownException)
        {
            return (true, false, "שרת ה-Domain לא זמין — ודא VPN פעיל");
        }
        catch (Exception ex) { return (true, false, ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // UI helpers — apply validation results to status dots (UI thread only).
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies a single-secret validation result to a status dot.
    /// </summary>
    private void ApplyResult(System.Windows.Shapes.Ellipse dot, string label,
        (bool exists, bool success, string? detail) result, List<string> passed, List<string> failed)
    {
        if (!result.exists)
        {
            dot.Fill = _redBrush;
            dot.ToolTip = "חסר — לא הוגדר";
            return;
        }
        if (result.success)
        {
            dot.Fill = _greenBrush;
            dot.ToolTip = $"✅ פעיל ונבדק{(result.detail != null ? $" ({result.detail})" : "")}";
            passed.Add($"{label}{(result.detail != null ? $" — ({result.detail})" : "")}");
        }
        else
        {
            dot.Fill = _orangeBrush;
            dot.ToolTip = $"❌ {result.detail}";
            failed.Add($"{label} — {result.detail}");
        }
    }

    /// <summary>
    /// Applies a paired-secret validation result to two status dots.
    /// When both secrets exist, both dots share the test result.
    /// When only one exists, the present one shows orange (incomplete pair), the missing one shows red.
    /// </summary>
    private void ApplyPairResult(
        System.Windows.Shapes.Ellipse dot1, System.Windows.Shapes.Ellipse dot2,
        string key1, string key2, string label,
        (bool bothExist, bool success, string? detail) result,
        List<string> passed, List<string> failed)
    {
        var has1 = CredentialVaultService.HasSecret(key1);
        var has2 = CredentialVaultService.HasSecret(key2);

        if (!has1 && !has2)
        {
            dot1.Fill = _redBrush; dot1.ToolTip = "חסר — לא הוגדר";
            dot2.Fill = _redBrush; dot2.ToolTip = "חסר — לא הוגדר";
            return;
        }

        if (!has1 || !has2)
        {
            // One present, one missing — can't test the pair
            var hint = result.detail ?? "חסר ערך משלים לבדיקה";
            dot1.Fill = has1 ? _orangeBrush : _redBrush;
            dot1.ToolTip = has1 ? $"⚠ {hint}" : "חסר — לא הוגדר";
            dot2.Fill = has2 ? _orangeBrush : _redBrush;
            dot2.ToolTip = has2 ? $"⚠ {hint}" : "חסר — לא הוגדר";
            failed.Add($"{label} — {hint}");
            return;
        }

        // Both exist — apply test result to both dots
        if (result.success)
        {
            var tip = $"✅ פעיל ונבדק{(result.detail != null ? $" ({result.detail})" : "")}";
            dot1.Fill = _greenBrush; dot1.ToolTip = tip;
            dot2.Fill = _greenBrush; dot2.ToolTip = tip;
            passed.Add($"{label}{(result.detail != null ? $" — ({result.detail})" : "")}");
        }
        else
        {
            var tip = $"❌ {result.detail}";
            dot1.Fill = _orangeBrush; dot1.ToolTip = tip;
            dot2.Fill = _orangeBrush; dot2.ToolTip = tip;
            failed.Add($"{label} — {result.detail}");
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "שמור חבילת הגדרות מוצפנת",
                Filter = "SiNet Secrets (*.secrets)|*.secrets",
                DefaultExt = ".secrets",
                FileName = "SiNet.secrets"
            };

            if (dialog.ShowDialog() != true) return;

            var pwDialog = ProvisioningPasswordDialog.ForExport();
            if (pwDialog.ShowDialog() != true) return;

            var count = SecretProvisioningService.ExportToFile(dialog.FileName, pwDialog.EnteredPassword);

            MessageBox.Show(
                $"יוצאו {count} מפתחות לקובץ מוצפן בהצלחה.\n\n" +
                "העבר את הקובץ למחשב היעד והפעל 'ייבוא חבילה' עם אותה סיסמה.",
                "ייצוא הצליח", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בייצוא:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        ImportFromFile();
    }

    /// <summary>
    /// Imports secrets from an encrypted provisioning file.
    /// Can be called externally with a pre-selected file path.
    /// </summary>
    public void ImportFromFile(string? filePath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath))
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "בחר קובץ חבילת הגדרות",
                    Filter = "SiNet Secrets (*.secrets)|*.secrets|All Files (*.*)|*.*",
                    DefaultExt = ".secrets"
                };

                if (dialog.ShowDialog() != true) return;
                filePath = dialog.FileName;
            }

            var pwDialog = ProvisioningPasswordDialog.ForImport();
            if (pwDialog.ShowDialog() != true) return;

            var count = SecretProvisioningService.ImportFromFile(filePath, pwDialog.EnteredPassword);

            RefreshStatusIndicators();
            PreFillExistingValues();

            MessageBox.Show(
                $"יובאו {count} מפתחות ל-Windows Credential Manager בהצלחה.",
                "ייבוא הצליח", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בייבוא:\n{ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
