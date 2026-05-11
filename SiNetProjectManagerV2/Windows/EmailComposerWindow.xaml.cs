using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SiNetProjectManagerV2.Windows;

public partial class EmailComposerWindow : Window
{
    public EmailComposerWindow()
    {
        InitializeComponent();
    }

    private void RecipientSuggestion_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is EmailComposerViewModel { SelectedRecipientSuggestion: { } suggestion } viewModel)
            viewModel.AddRecipientSuggestionCommand.Execute(suggestion);
    }

    private void RecipientTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || DataContext is not EmailComposerViewModel viewModel)
            return;

        viewModel.RefreshRecipientSuggestions(GetRecipientField(textBox), textBox.Text, textBox.CaretIndex);
    }

    private void RecipientTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not EmailComposerViewModel { IsRecipientSuggestionsOpen: true } viewModel)
            return;

        if (e.Key is Key.Enter or Key.Tab && viewModel.SelectedRecipientSuggestion != null)
        {
            viewModel.AddRecipientSuggestionCommand.Execute(viewModel.SelectedRecipientSuggestion);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CloseRecipientSuggestions();
            e.Handled = true;
        }
    }

    private void RecipientTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is ListBoxItem or ListBox)
            return;

        if (DataContext is EmailComposerViewModel viewModel)
            viewModel.CloseRecipientSuggestions();
    }

    private static RecipientField GetRecipientField(TextBox textBox)
    {
        return textBox.Name switch
        {
            "CcTextBox" => RecipientField.Cc,
            "BccTextBox" => RecipientField.Bcc,
            _ => RecipientField.To
        };
    }
}
