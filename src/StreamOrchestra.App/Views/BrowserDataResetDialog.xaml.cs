using System.Windows;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Views;

public sealed record BrowserDataGroupOption(ProfileGroup Group, string DisplayName);

public partial class BrowserDataResetDialog : Window
{
    public BrowserDataResetDialog(
        IReadOnlyList<BrowserDataGroupOption> groups,
        string? initiallySelectedGroupId = null)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
        {
            throw new ArgumentException("At least one browser group is required.", nameof(groups));
        }

        InitializeComponent();
        ProfileGroupComboBox.ItemsSource = groups;
        ProfileGroupComboBox.SelectedItem = groups.FirstOrDefault(option =>
            option.Group.Id.Equals(initiallySelectedGroupId, StringComparison.OrdinalIgnoreCase)) ?? groups[0];
    }

    public BrowserDataGroupOption SelectedGroup =>
        (BrowserDataGroupOption)ProfileGroupComboBox.SelectedItem;

    public BrowserDataClearOptions Options => new(
        SiteDataCheckBox.IsChecked == true,
        CacheCheckBox.IsChecked == true);

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Options.ClearSiteData && !Options.ClearCache)
        {
            StatusTextBlock.Text = "삭제할 데이터 항목을 하나 이상 선택하세요.";
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
