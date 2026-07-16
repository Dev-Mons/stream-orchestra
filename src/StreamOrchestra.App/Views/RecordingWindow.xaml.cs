using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class RecordingWindow : Window
{
    private static readonly Brush ActiveStatusBrush = new SolidColorBrush(Color.FromRgb(255, 104, 104));
    private static readonly Brush SuccessStatusBrush = new SolidColorBrush(Color.FromRgb(100, 210, 140));

    private readonly RecordingToolService _toolService = new();
    private readonly SoopRecordingService _recordingService = new();
    private readonly StreamNavigationService _navigationService = new();
    private readonly DispatcherTimer _elapsedTimer;
    private CancellationTokenSource? _recordingCancellationTokenSource;
    private Task<RecordingResult>? _recordingTask;
    private DateTimeOffset _recordingStartedAt;
    private bool _isInstalling;
    private bool _allowClose;
    private bool _closingAfterStop;

    public RecordingWindow(string? suggestedUrl = null)
    {
        InitializeComponent();
        StreamUrlTextBox.Text = suggestedUrl ?? "";
        OutputFolderTextBox.Text = GetDefaultOutputFolder();
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => RefreshElapsedTime();
        Loaded += RecordingWindow_Loaded;
        Closing += RecordingWindow_Closing;
        Closed += (_, _) =>
        {
            _recordingCancellationTokenSource?.Dispose();
            _recordingService.Dispose();
        };
    }

    private void RecordingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshToolStatus();
        StreamUrlTextBox.Focus();
        StreamUrlTextBox.SelectAll();
    }

    private async void InstallToolButton_Click(object sender, RoutedEventArgs e)
    {
        await InstallToolAsync();
    }

    private async Task<bool> InstallToolAsync()
    {
        if (_isInstalling)
        {
            return false;
        }

        _isInstalling = true;
        InstallToolButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        ToolInstallProgressBar.Value = 0;
        ToolInstallProgressBar.Visibility = Visibility.Visible;
        ToolStatusTextBlock.Text = "공식 녹화 도구 다운로드 중...";

        try
        {
            var progress = new Progress<double>(value => ToolInstallProgressBar.Value = value);
            await _toolService.InstallLatestAsync(progress);
            ToolStatusTextBlock.Text = "녹화 도구가 준비되었습니다.";
            return true;
        }
        catch (Exception ex)
        {
            ToolStatusTextBlock.Text = "녹화 도구 설치 실패";
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
            ToolInstallProgressBar.Visibility = Visibility.Collapsed;
            InstallToolButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            RefreshToolStatus();
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingTask is not null)
        {
            return;
        }

        var url = _navigationService.NormalizeUrl(StreamUrlTextBox.Text);
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

        StreamUrlTextBox.Text = url;

        var outputFolder = OutputFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            MessageBox.Show(this, "저장 폴더를 선택해 주세요.", "폴더 확인");
            return;
        }

        var executablePath = _toolService.FindExecutable();
        if (executablePath is null)
        {
            var answer = MessageBox.Show(
                this,
                "녹화에 필요한 yt-dlp가 없습니다. 공식 릴리스에서 지금 설치할까요?",
                "녹화 도구 설치",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes || !await InstallToolAsync())
            {
                return;
            }

            executablePath = _toolService.FindExecutable();
            if (executablePath is null)
            {
                return;
            }
        }

        try
        {
            Directory.CreateDirectory(outputFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"저장 폴더를 만들 수 없습니다.\n\n{ex.Message}", "폴더 오류");
            return;
        }

        _recordingStartedAt = DateTimeOffset.Now;
        _recordingCancellationTokenSource = new CancellationTokenSource();
        var qualityId = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
        var request = new RecordingRequest(url, outputFolder, qualityId, _recordingStartedAt);
        var progress = new Progress<string>(AppendLogLine);

        SetRecordingUi(isRecording: true);
        LogTextBox.Clear();
        AppendLogLine($"녹화 시작: {url}");
        _recordingTask = _recordingService.RecordAsync(
            executablePath,
            request,
            progress,
            _recordingCancellationTokenSource.Token);

        RecordingResult result;
        try
        {
            result = await _recordingTask;
        }
        catch (Exception ex)
        {
            result = new RecordingResult(RecordingCompletion.Failed, -1, ex.Message);
            AppendLogLine(ex.ToString());
        }
        finally
        {
            _recordingTask = null;
            _recordingCancellationTokenSource?.Dispose();
            _recordingCancellationTokenSource = null;
            SetRecordingUi(isRecording: false);
        }

        RecordingStatusTextBlock.Text = result.Message;
        RecordingStatusTextBlock.Foreground = result.Completion == RecordingCompletion.Failed
            ? ActiveStatusBrush
            : SuccessStatusBrush;
        AppendLogLine(result.Message);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        RequestStop();
    }

    private void RequestStop()
    {
        RecordingStatusTextBlock.Text = "녹화를 정리하는 중...";
        StopButton.IsEnabled = false;
        _recordingCancellationTokenSource?.Cancel();
        _recordingService.Stop();
    }

    private void SetRecordingUi(bool isRecording)
    {
        StreamUrlTextBox.IsEnabled = !isRecording;
        OutputFolderTextBox.IsEnabled = !isRecording;
        BrowseFolderButton.IsEnabled = !isRecording;
        QualityComboBox.IsEnabled = !isRecording;
        InstallToolButton.IsEnabled = !isRecording;
        StartButton.IsEnabled = !isRecording;
        StopButton.IsEnabled = isRecording;

        if (isRecording)
        {
            RecordingStatusTextBlock.Text = "● 녹화 중";
            RecordingStatusTextBlock.Foreground = ActiveStatusBrush;
            _elapsedTimer.Start();
            RefreshElapsedTime();
        }
        else
        {
            _elapsedTimer.Stop();
        }
    }

    private void RefreshElapsedTime()
    {
        var elapsed = DateTimeOffset.Now - _recordingStartedAt;
        ElapsedTextBlock.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void RefreshToolStatus()
    {
        var path = _toolService.FindExecutable();
        ToolStatusTextBlock.Text = path is null
            ? "녹화 도구가 필요합니다. (최초 1회)"
            : "녹화 도구 준비됨";
        InstallToolButton.Content = path is null ? "녹화 도구 설치" : "도구 업데이트";
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "녹화 파일을 저장할 폴더 선택",
            InitialDirectory = Directory.Exists(OutputFolderTextBox.Text)
                ? OutputFolderTextBox.Text
                : GetDefaultOutputFolder()
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = OutputFolderTextBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "아직 저장 폴더가 없습니다.", "폴더 열기");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(folder);
        Process.Start(startInfo);
    }

    private void AppendLogLine(string line)
    {
        if (LogTextBox.Text.Length > 60000)
        {
            LogTextBox.Text = LogTextBox.Text[^40000..];
        }

        LogTextBox.AppendText(line + Environment.NewLine);
        LogTextBox.ScrollToEnd();
    }

    private void RecordingWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _recordingTask is null)
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
            "녹화가 진행 중입니다. 녹화를 중지하고 창을 닫을까요?",
            "녹화 중",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _closingAfterStop = true;
        _ = StopThenCloseAsync();
    }

    private async Task StopThenCloseAsync()
    {
        RequestStop();
        try
        {
            if (_recordingTask is not null)
            {
                await _recordingTask;
            }
        }
        catch
        {
            // 녹화 시작 실패와 동시에 창을 닫아도 종료 흐름은 계속한다.
        }

        _allowClose = true;
        Close();
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
}
