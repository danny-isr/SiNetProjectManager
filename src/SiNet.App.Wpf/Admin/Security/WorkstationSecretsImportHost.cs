using System.Text;
using System.Windows;
using Microsoft.Win32;
using SiNet.Application.Configuration;
using SiNet.Application.Runtime;

namespace SiNet.App.Wpf.Admin.Security;

/// <summary>
/// Employee-reachable <c>.secrets</c> import. Does not open Secret Setup.
/// </summary>
public sealed class WorkstationSecretsImportHost(
    ISecretSetupService secretSetupService,
    IRuntimeSubsystemStatusService? runtimeStatus = null)
{
    private readonly ISecretSetupService _secretSetupService =
        secretSetupService ?? throw new ArgumentNullException(nameof(secretSetupService));
    private readonly IRuntimeSubsystemStatusService? _runtimeStatus = runtimeStatus;

    public async Task RunAsync(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "ייבוא מפתחות תחנה",
            Filter = "SiNet Secrets (*.secrets)|*.secrets|All Files (*.*)|*.*",
            DefaultExt = ".secrets",
        };

        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        var passwordDialog = new ProvisioningPasswordWindow(
            requireConfirmation: false,
            title: "ייבוא מפתחות תחנה");
        if (owner is not null)
        {
            passwordDialog.Owner = owner;
        }

        if (passwordDialog.ShowDialog() != true)
        {
            return;
        }

        var preview = await _secretSetupService
            .PreviewImportAsync(dialog.FileName, passwordDialog.EnteredPassword)
            .ConfigureAwait(true);

        var mode = SecretImportModeWindow.ChooseMode(owner, preview);
        if (mode is null)
        {
            return;
        }

        var result = await _secretSetupService
            .ImportAsync(dialog.FileName, passwordDialog.EnteredPassword, mode.Value)
            .ConfigureAwait(true);

        if (_runtimeStatus is not null)
        {
            await _runtimeStatus.RefreshAsync().ConfigureAwait(true);
        }

        var sb = new StringBuilder(result.Message);
        foreach (var line in result.SkippedSummaries)
        {
            sb.AppendLine(line);
        }

        MessageBox.Show(
            owner,
            sb.ToString().Trim(),
            "ייבוא הושלם",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
