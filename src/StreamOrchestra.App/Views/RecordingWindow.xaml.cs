using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class RecordingWindow : Window
{
    private const int MaxConcurrentRecordings = 5;
    private readonly RecordingToolService _toolService = new();
    private readonly RecordingCatalogStorageService _catalogStorage = new();
    private readonly SoopStreamMetadataService _metadataService = new();
    private readonly ObservableCollection<RecordingSessionViewModel> _recordings = [];
    private readonly Dictionary<RecordingSessionViewModel, Task<RecordingResult>> _recordingTasks = [];
    private readonly DispatcherTimer _uiTimer;
    private readonly string? _suggestedUrl;
    private readonly ICollectionView _recordingsView;
    private string _outputFolder;
    private bool _isInstalling;
    private bool _isLoadingMetadata;
    private bool _allowClose;
    private bool _closingAfterStop;
    private int _diskRefreshTick;

    public RecordingWindow(string? suggestedUrl = null)
    {
        InitializeComponent();
        _suggestedUrl = suggestedUrl;
        _outputFolder = GetDefaultOutputFolder();
        RecordingListBox.ItemsSource = _recordings;
        _recordingsView = CollectionViewSource.GetDefaultView(_recordings);
        _recordingsView.Filter = FilterRecording;
        _recordings.CollectionChanged += (_, _) => RefreshChrome();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) => RefreshLiveState();
        Loaded += RecordingWindow_Loaded;
        SourceInitialized += (_, _) => EnableImmersiveDarkTitleBar();
        Closing += RecordingWindow_Closing;
        Closed += RecordingWindow_Closed;
    }

    private async void RecordingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadCatalog();
        _uiTimer.Start();
        RefreshChrome();
        RefreshDiskSpace();
        _ = RefreshMissingThumbnailsAsync();

        if (!string.IsNullOrWhiteSpace(_suggestedUrl))
        {
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            await ShowAddRecordingDialogAsync(_suggestedUrl);
        }
    }

    private async void AddRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowAddRecordingDialogAsync();
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task ShowAddRecordingDialogAsync(string? suggestedUrl = null)
    {
        var dialog = new AddRecordingDialog(suggestedUrl) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var existing = _recordings.FirstOrDefault(recording =>
            recording.StreamUrl.Equals(dialog.StreamUrl, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecordingListBox.SelectedItem = existing;
            MessageBox.Show(
                this,
                "이 방송은 이미 목록에 있습니다.",
                "방송 목록 확인",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await EnsureRecordingToolsAsync())
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_outputFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"저장 폴더를 만들 수 없습니다.\n\n{ex.Message}", "폴더 오류");
            return;
        }

        var request = new RecordingRequest(
            dialog.StreamUrl,
            _outputFolder,
            dialog.QualityId,
            DateTimeOffset.Now,
            dialog.Username,
            dialog.Password);
        var metadata = await TryResolveMetadataAsync(request);
        var recording = new RecordingSessionViewModel(
            Guid.NewGuid().ToString("N"),
            request,
            metadata?.ThumbnailPath,
            metadata?.DisplayName,
            metadata?.Title,
            requiresCredentials: !string.IsNullOrWhiteSpace(dialog.Username));
        recording.PropertyChanged += Recording_PropertyChanged;
        _recordings.Add(recording);
        RecordingListBox.SelectedItem = recording;
        SaveCatalog();
        RefreshChrome();
        await StartRecordingSessionAsync(recording, dialog.Username, dialog.Password);
    }

    private async void RecordingPrimaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecordingSessionViewModel recording)
        {
            return;
        }

        if (recording.CanStop)
        {
            recording.RequestStop();
            RefreshChrome();
            return;
        }

        string? username = recording.Request.Username;
        string? password = null;
        if (recording.RequiresCredentials)
        {
            var credentialsDialog = new RecordingCredentialsDialog(username) { Owner = this };
            if (credentialsDialog.ShowDialog() != true)
            {
                return;
            }

            username = credentialsDialog.Username;
            password = credentialsDialog.Password;
        }

        await StartRecordingSessionAsync(recording, username, password);
    }

    private async Task StartRecordingSessionAsync(
        RecordingSessionViewModel recording,
        string? username = null,
        string? password = null)
    {
        if (recording.IsActive)
        {
            return;
        }

        if (_recordings.Count(candidate => candidate.IsActive) >= MaxConcurrentRecordings)
        {
            MessageBox.Show(
                this,
                $"동시에 녹화할 수 있는 방송은 최대 {MaxConcurrentRecordings}개입니다.\n진행 중인 녹화가 끝난 뒤 다시 시작해 주세요.",
                "동시 녹화 수 확인",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await EnsureRecordingToolsAsync())
        {
            return;
        }

        var executablePath = _toolService.FindExecutable();
        var ffmpegExecutablePath = _toolService.FindFfmpegExecutable();
        if (executablePath is null || ffmpegExecutablePath is null)
        {
            MessageBox.Show(this, "녹화 도구를 찾을 수 없습니다.", "녹화 도구 확인");
            return;
        }

        Directory.CreateDirectory(_outputFolder);
        if (string.IsNullOrWhiteSpace(recording.ThumbnailSource))
        {
            var metadataRequest = recording.Request with { Username = username, Password = password };
            var metadata = await TryResolveMetadataAsync(metadataRequest);
            if (metadata is not null)
            {
                recording.UpdateMetadata(metadata);
                SaveCatalog();
            }
        }

        var task = recording.StartAsync(
            executablePath,
            ffmpegExecutablePath,
            _outputFolder,
            username,
            password);
        _recordingTasks[recording] = task;
        RefreshChrome();
        _ = ObserveRecordingCompletionAsync(recording, task);
    }

    private async Task ObserveRecordingCompletionAsync(
        RecordingSessionViewModel recording,
        Task<RecordingResult> task)
    {
        try
        {
            await task;
        }
        finally
        {
            _recordingTasks.Remove(recording);
            SaveCatalog();
            RefreshChrome();
            RefreshDiskSpace();
        }
    }

    private async Task<SoopResolvedStreamMetadata?> TryResolveMetadataAsync(RecordingRequest request)
    {
        var executablePath = _toolService.FindExecutable();
        if (executablePath is null)
        {
            return null;
        }

        _isLoadingMetadata = true;
        SystemStatusTextBlock.Text = "실제 방송 정보와 썸네일을 불러오는 중";
        try
        {
            return await _metadataService.ResolveAsync(executablePath, request);
        }
        catch
        {
            return null;
        }
        finally
        {
            _isLoadingMetadata = false;
            RefreshChrome();
        }
    }

    private async Task RefreshMissingThumbnailsAsync()
    {
        var executablePath = _toolService.FindExecutable();
        if (executablePath is null)
        {
            return;
        }

        foreach (var recording in _recordings.Where(recording =>
                     string.IsNullOrWhiteSpace(recording.ThumbnailSource) &&
                     !recording.RequiresCredentials))
        {
            var metadata = await TryResolveMetadataAsync(recording.Request);
            if (metadata is not null)
            {
                recording.UpdateMetadata(metadata);
                SaveCatalog();
            }
        }
    }

    private async Task<bool> EnsureRecordingToolsAsync()
    {
        if (_toolService.AreRequiredToolsAvailable())
        {
            return true;
        }

        var answer = MessageBox.Show(
            this,
            "라이브 녹화에 필요한 yt-dlp와 FFmpeg를 처음 한 번 설치해야 합니다.\n지금 공식 릴리스에서 안전하게 설치할까요?",
            "녹화 도구 설치",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return answer == MessageBoxResult.Yes && await InstallRecordingToolsAsync();
    }

    private async Task<bool> InstallRecordingToolsAsync()
    {
        if (_isInstalling)
        {
            return false;
        }

        _isInstalling = true;
        AddRecordingButton.IsEnabled = false;
        SystemStatusTextBlock.Text = "녹화 도구를 내려받는 중 · 0%";
        try
        {
            var progress = new Progress<double>(value =>
                SystemStatusTextBlock.Text = $"녹화 도구를 내려받는 중 · {value:P0}");
            await _toolService.InstallLatestAsync(progress);
            SystemStatusTextBlock.Text = "녹화 도구 준비됨";
            return true;
        }
        catch (Exception ex)
        {
            SystemStatusTextBlock.Text = "녹화 도구 설치 실패";
            MessageBox.Show(
                this,
                $"녹화 도구를 설치하지 못했습니다.\n\n{ex.Message}",
                "설치 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _isInstalling = false;
            AddRecordingButton.IsEnabled = true;
            RefreshChrome();
        }
    }

    private void OpenSelectedFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(_outputFolder);
    }

    private void RemoveRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecordingSessionViewModel recording)
        {
            RemoveRecording(recording);
        }
    }

    private void RemoveRecording(RecordingSessionViewModel recording)
    {
        if (recording.IsActive)
        {
            MessageBox.Show(this, "진행 중인 녹화를 먼저 중지해 주세요.", "방송 제거");
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"'{recording.DisplayName}' 방송을 목록에서 제거할까요?\n저장된 영상 파일은 삭제되지 않습니다.",
            "방송 제거",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        recording.PropertyChanged -= Recording_PropertyChanged;
        _recordings.Remove(recording);
        recording.Dispose();
        RecordingListBox.SelectedItem = _recordings.FirstOrDefault();
        SaveCatalog();
        RefreshChrome();
    }

    private void ChangeOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recordings.Any(recording => recording.IsActive))
        {
            MessageBox.Show(
                this,
                "녹화 중에는 저장 위치를 변경할 수 없습니다.\n진행 중인 녹화를 먼저 중지해 주세요.",
                "저장 위치 변경",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "모든 방송의 녹화 파일을 저장할 폴더 선택",
            InitialDirectory = Directory.Exists(_outputFolder)
                ? _outputFolder
                : GetDefaultOutputFolder()
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _outputFolder = dialog.FolderName;
        foreach (var recording in _recordings)
        {
            recording.UpdateOutputFolder(_outputFolder);
        }

        SaveCatalog();
        RefreshDiskSpace();
    }

    private async void RecordingToolSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _toolService.AreRequiredToolsAvailable() ? "업데이트" : "설치";
        var answer = MessageBox.Show(
            this,
            $"녹화 도구를 최신 버전으로 {action}할까요?",
            "녹화 도구 설정",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            await InstallRecordingToolsAsync();
        }
    }

    private void RecordingSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHintTextBlock.Visibility = string.IsNullOrEmpty(RecordingSearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _recordingsView.Refresh();
    }

    private bool FilterRecording(object item)
    {
        if (item is not RecordingSessionViewModel recording)
        {
            return false;
        }

        var query = RecordingSearchTextBox.Text.Trim();
        return query.Length == 0 ||
               recording.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               recording.DetailTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               recording.StreamUrl.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RecordingListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSelectedState();
    }

    private void Recording_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordingSessionViewModel.State) or
            nameof(RecordingSessionViewModel.DisplayName) or
            nameof(RecordingSessionViewModel.DetailTitle) or
            nameof(RecordingSessionViewModel.ThumbnailSource))
        {
            _recordingsView.Refresh();
            SaveCatalog();
            RefreshChrome();
        }
    }

    private void RefreshLiveState()
    {
        var now = DateTimeOffset.Now;
        foreach (var recording in _recordings)
        {
            recording.Tick(now);
        }

        if (++_diskRefreshTick >= 10)
        {
            _diskRefreshTick = 0;
            RefreshDiskSpace();
        }
    }

    private void RefreshChrome()
    {
        RecordingCountTextBlock.Text = _recordings.Count.ToString();
        EmptyQueuePanel.Visibility = _recordings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSelectedState();

        if (_isInstalling || _isLoadingMetadata)
        {
            return;
        }

        var activeCount = _recordings.Count(recording => recording.IsActive);
        SystemStatusTextBlock.Text = activeCount > 0
            ? $"모든 시스템 정상 · {activeCount}개 녹화 중"
            : _toolService.AreRequiredToolsAvailable()
                ? "모든 시스템 정상 · 녹화 도구 준비됨"
                : "녹화 도구 설치가 필요합니다";
    }

    private void RefreshSelectedState()
    {
        var hasSelection = RecordingListBox.SelectedItem is RecordingSessionViewModel;
        SelectedDetailHost.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        EmptyDetailPanel.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshDiskSpace()
    {
        StorageStatusTextBlock.Text = _outputFolder;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_outputFolder));
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException();
            }

            var drive = new DriveInfo(root);
            var used = drive.TotalSize - drive.AvailableFreeSpace;
            DiskSpaceProgressBar.Value = drive.TotalSize == 0
                ? 0
                : used * 100d / drive.TotalSize;
            DiskSpaceTextBlock.Text = $"여유 공간 {FormatBytes(drive.AvailableFreeSpace)}";
        }
        catch
        {
            DiskSpaceProgressBar.Value = 0;
            DiskSpaceTextBlock.Text = "공간 정보 없음";
        }
    }

    private void LoadCatalog()
    {
        var state = _catalogStorage.Load(GetDefaultOutputFolder());
        _outputFolder = state.OutputFolder;
        foreach (var item in state.Items)
        {
            var recording = RecordingSessionViewModel.FromCatalogItem(item, _outputFolder);
            recording.PropertyChanged += Recording_PropertyChanged;
            _recordings.Add(recording);
        }

        RecordingListBox.SelectedItem = _recordings.FirstOrDefault();
    }

    private void SaveCatalog()
    {
        _catalogStorage.Save(new RecordingCatalogState(
            _outputFolder,
            _recordings.Select(recording => recording.ToCatalogItem()).ToArray()));
    }

    private void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(folder);
        Process.Start(startInfo);
    }

    private void RecordingWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _recordings.All(recording => !recording.IsActive))
        {
            return;
        }

        e.Cancel = true;
        if (_closingAfterStop)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "진행 중인 녹화를 모두 안전하게 중지하고 창을 닫을까요?",
            "녹화 중",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _closingAfterStop = true;
        _ = StopAllThenCloseAsync();
    }

    private async Task StopAllThenCloseAsync()
    {
        foreach (var recording in _recordings.Where(recording => recording.IsActive))
        {
            recording.RequestStop();
        }

        try
        {
            await Task.WhenAll(_recordingTasks.Values.ToArray());
        }
        catch
        {
            // 각 작업의 실패 상태는 해당 세션에 표시하고 창 닫기 흐름은 계속한다.
        }

        _allowClose = true;
        Close();
    }

    private void RecordingWindow_Closed(object? sender, EventArgs e)
    {
        _uiTimer.Stop();
        SaveCatalog();
        foreach (var recording in _recordings)
        {
            recording.PropertyChanged -= Recording_PropertyChanged;
            recording.Dispose();
        }
    }

    private void EnableImmersiveDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // 구형 Windows에서는 기본 제목 표시줄을 사용한다.
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string GetDefaultOutputFolder()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
        {
            videos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        return Path.Combine(videos, "Stream Orchestra");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
