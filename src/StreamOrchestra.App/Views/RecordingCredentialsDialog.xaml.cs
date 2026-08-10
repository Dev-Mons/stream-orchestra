using System.Windows;

namespace StreamOrchestra.App.Views;

public partial class RecordingCredentialsDialog : Window
{
    public RecordingCredentialsDialog(string? username)
    {
        InitializeComponent();
        UsernameTextBox.Text = username ?? "";
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                UsernameTextBox.Focus();
            }
            else
            {
                PasswordBox.Focus();
            }
        };
    }

    public string Username { get; private set; } = "";

    public string Password { get; private set; } = "";

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UsernameTextBox.Text) || string.IsNullOrEmpty(PasswordBox.Password))
        {
            MessageBox.Show(this, "SOOP ID와 비밀번호를 모두 입력해 주세요.", "계정 확인");
            return;
        }

        Username = UsernameTextBox.Text.Trim();
        Password = PasswordBox.Password;
        PasswordBox.Clear();
        DialogResult = true;
    }
}
