using System.Windows;

using SiNet.Application.Email.Acc;

using SiNetProjectManagerV2.Dialogs;



namespace SiNetProjectManagerV2.Services;



internal sealed class EmailAccClosePrompt : IEmailAccClosePrompt

{

    public bool ConfirmCloseIfNeeded(object? owner)

    {

        if (!AccBackgroundWorkMonitor.HasActiveUploads)

        {

            return true;

        }



        var dialog = new BackgroundUploadsDialog(BackgroundCloseScope.EmailWindow)

        {

            Owner = owner as Window,

        };



        return dialog.ShowDialog() == true;

    }

}


