using System.Windows;
using System.Windows.Controls;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class AddRecordingDialog : Window
{
    public AddRecordingDialog(string? suggestedUrl)
    {
        InitializeComponent();
        StreamUrlTextBox.Text = suggestedUrl ?? "";
        Loaded += (_, _) =>
        {
            StreamUrlTextBox.Focus();
            StreamUrlTextBox.SelectAll();
        };
    }

    public string StreamUrl { get; private set; } = "";

    public string QualityId { get; private set; } = "best";

    public string? Username { get; private set; }

    public string? Password { get; private set; }

    private void SubscriberRecordingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        CredentialsPanel.Visibility = SubscriberRecordingCheckBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var url = new StreamNavigationService().NormalizeUrl(StreamUrlTextBox.Text);
        if (!SoopRecordingService.IsSupportedSoopUrl(url))
        {
            MessageBox.Show(
                this,
                "유효한 SOOP 방송 주소를 입력해 주세요.",
                "주소 확인",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StreamUrlTextBox.Focus();
            return;
        }

        var useCredentials = SubscriberRecordingCheckBox.IsChecked == true;
        var username = UsernameTextBox.Text.Trim();
        var password = PasswordBox.Password;
        if (useCredentials && (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)))
        {
            MessageBox.Show(
                this,
                "구독자 전용 방송을 녹화하려면 SOOP ID와 비밀번호를 모두 입력해 주세요.",
                "계정 확인",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        StreamUrl = url;
        QualityId = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
        Username = useCredentials ? username : null;
        Password = useCredentials ? password : null;
        PasswordBox.Clear();
        DialogResult = true;
    }
}
