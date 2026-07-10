using System.Windows;
using SiNet.Application.Email.Detail;
using SiNetSQL.MVVM;
using SiNetProjectManagerV2.WPF_Window;

namespace SiNetProjectManagerV2.Services.Email;

internal sealed class EmailAlternativeNamePromptHost : IEmailAlternativeNamePromptHost
{
    public bool IsAvailable => true;

    public Task<string?> PromptForNewAlternativeNameAsync(
        IReadOnlyList<string> existingNames,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? result = null;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current.MainWindow;
            var vm = new AlternativeNameViewModel(initialName: "", existingNames: existingNames);
            var dialog = new AlternativeNameWindow(vm)
            {
                Owner = owner,
            };

            result = dialog.ShowDialog() == true
                && !string.IsNullOrWhiteSpace(vm.AlternativeName)
                ? vm.AlternativeName
                : null;
        });

        return Task.FromResult(result);
    }
}
