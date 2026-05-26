using System.Windows;
using System.Windows.Input;

namespace StreamOrchestra.App.Views;

public partial class SavePresetDialog : Window
{
    public SavePresetDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PresetNameTextBox.Focus();
    }

    public string PresetName => PresetNameTextBox.Text.Trim();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        TryAccept();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void PresetNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        TryAccept();
    }

    private void TryAccept()
    {
        if (string.IsNullOrWhiteSpace(PresetName))
        {
            return;
        }

        DialogResult = true;
    }
}
