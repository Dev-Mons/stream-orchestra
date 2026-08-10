using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Models;

public enum RecordingSessionState
{
    Idle,
    Connecting,
    Recording,
    Stopping,
    Completed,
    Stopped,
    Failed
}

public sealed record RecordingActivity(string TimeText, string Message);

public sealed partial class RecordingSessionViewModel : INotifyPropertyChanged, IDisposable
{
    private SoopRecordingService? _recordingService;
    private CancellationTokenSource? _cancellationTokenSource;
    private RecordingRequest _request;
    private DateTimeOffset _startedAt;
    private RecordingSessionState _state = RecordingSessionState.Idle;
    private DateTimeOffset? _finishedAt;
    private string _elapsedText = "00:00:00";
    private string _fileSizeText = "아직 녹화하지 않음";
    private string _latestTechnicalMessage = "";
    private string _displayName;
    private string _detailTitle;
    private string? _thumbnailSource;
    private bool _disposed;

    public RecordingSessionViewModel(
        string catalogId,
        RecordingRequest request,
        string? thumbnailSource = null,
        string? displayName = null,
        string? detailTitle = null,
        bool requiresCredentials = false,
        DateTimeOffset? addedAt = null)
    {
        CatalogId = catalogId;
        _request = request with { Password = null };
        _thumbnailSource = thumbnailSource;
        _startedAt = request.StartedAt;
        var fallbackNames = CreateFriendlyNames(request.StreamUrl);
        _displayName = string.IsNullOrWhiteSpace(displayName) ? fallbackNames.DisplayName : displayName.Trim();
        _detailTitle = string.IsNullOrWhiteSpace(detailTitle) ? fallbackNames.DetailTitle : detailTitle.Trim();
        RequiresCredentials = requiresCredentials;
        AddedAt = addedAt ?? DateTimeOffset.Now;
        AddActivity("녹화할 준비가 되었어요");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CatalogId { get; }

    public DateTimeOffset AddedAt { get; }

    public bool RequiresCredentials { get; }

    public RecordingRequest Request => _request;

    public string StreamUrl => _request.StreamUrl;

    public string OutputFolder => _request.OutputFolder;

    public string? ThumbnailSource
    {
        get => _thumbnailSource;
        private set => SetField(ref _thumbnailSource, value);
    }

    public string DisplayName
    {
        get => _displayName;
        private set => SetField(ref _displayName, value);
    }

    public string DetailTitle
    {
        get => _detailTitle;
        private set => SetField(ref _detailTitle, value);
    }

    public RecordingSessionState State
    {
        get => _state;
        private set
        {
            if (!SetField(ref _state, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StateLabel));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(CanUsePrimaryAction));
            OnPropertyChanged(nameof(PrimaryActionLabel));
        }
    }

    public string StateLabel => State switch
    {
        RecordingSessionState.Idle => "대기 중",
        RecordingSessionState.Connecting => "연결 중",
        RecordingSessionState.Recording => "녹화 중",
        RecordingSessionState.Stopping => "정리 중",
        RecordingSessionState.Completed => "완료",
        RecordingSessionState.Stopped => "중지됨",
        RecordingSessionState.Failed => "확인 필요",
        _ => "대기 중"
    };

    public string QualityLabel => _request.QualityId switch
    {
        "1080" => "1080p 이하",
        "720" => "720p 이하",
        "540" => "540p 이하",
        "360" => "360p 이하",
        _ => "최고 화질"
    };

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetField(ref _elapsedText, value);
    }

    public string FileSizeText
    {
        get => _fileSizeText;
        private set => SetField(ref _fileSizeText, value);
    }

    public string LatestTechnicalMessage
    {
        get => _latestTechnicalMessage;
        private set => SetField(ref _latestTechnicalMessage, value);
    }

    public bool CanStop => State is RecordingSessionState.Connecting or RecordingSessionState.Recording;

    public bool CanStart => !IsActive;

    public bool IsActive => State is RecordingSessionState.Connecting or RecordingSessionState.Recording or RecordingSessionState.Stopping;

    public bool CanUsePrimaryAction => State != RecordingSessionState.Stopping;

    public string PrimaryActionLabel => State switch
    {
        RecordingSessionState.Stopping => "정리 중",
        _ when CanStop => "녹화 중지",
        _ => "녹화 시작",
    };

    public ObservableCollection<RecordingActivity> Activities { get; } = [];

    public async Task<RecordingResult> StartAsync(
        string executablePath,
        string ffmpegExecutablePath,
        string outputFolder,
        string? username = null,
        string? password = null)
    {
        ThrowIfDisposed();
        if (IsActive)
        {
            throw new InvalidOperationException("이미 녹화가 진행 중입니다.");
        }

        _recordingService?.Dispose();
        _cancellationTokenSource?.Dispose();
        _recordingService = new SoopRecordingService();
        _cancellationTokenSource = new CancellationTokenSource();
        _startedAt = DateTimeOffset.Now;
        _finishedAt = null;
        _request = _request with
        {
            OutputFolder = outputFolder,
            StartedAt = _startedAt,
            Username = string.IsNullOrWhiteSpace(username) ? _request.Username : username.Trim(),
            Password = password
        };
        OnPropertyChanged(nameof(Request));
        OnPropertyChanged(nameof(OutputFolder));
        ElapsedText = "00:00:00";
        FileSizeText = "준비 중";
        LatestTechnicalMessage = "";
        State = RecordingSessionState.Connecting;
        AddActivity("방송에 연결하고 있어요");

        RecordingResult result;
        try
        {
            State = RecordingSessionState.Recording;
            AddActivity($"{QualityLabel}로 저장을 시작했어요");
            var progress = new Progress<string>(HandleToolOutput);
            result = await _recordingService.RecordAsync(
                executablePath,
                ffmpegExecutablePath,
                _request,
                progress,
                _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            LatestTechnicalMessage = ex.ToString();
            result = new RecordingResult(RecordingCompletion.Failed, -1, ex.Message);
        }

        _request = _request with { Password = null };
        _finishedAt = DateTimeOffset.Now;
        State = result.Completion switch
        {
            RecordingCompletion.Completed => RecordingSessionState.Completed,
            RecordingCompletion.Stopped => RecordingSessionState.Stopped,
            _ => RecordingSessionState.Failed
        };
        AddActivity(result.Completion switch
        {
            RecordingCompletion.Completed => "방송이 끝나 영상을 안전하게 저장했어요",
            RecordingCompletion.Stopped => "요청한 위치까지 영상을 저장했어요",
            _ => "녹화를 마치지 못했어요. 상세 정보를 확인해 주세요"
        });
        Tick(_finishedAt.Value);
        return result;
    }

    public void RequestStop()
    {
        if (!CanStop)
        {
            return;
        }

        State = RecordingSessionState.Stopping;
        AddActivity("녹화를 안전하게 마무리하고 있어요");
        _cancellationTokenSource?.Cancel();
        _recordingService?.Stop();
    }

    public void Tick(DateTimeOffset now)
    {
        if (State == RecordingSessionState.Idle)
        {
            return;
        }

        var end = _finishedAt ?? now;
        var elapsed = end - _startedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        ElapsedText = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    public void UpdateMetadata(SoopResolvedStreamMetadata metadata)
    {
        DisplayName = metadata.DisplayName;
        DetailTitle = metadata.Title;
        if (!string.IsNullOrWhiteSpace(metadata.ThumbnailPath))
        {
            ThumbnailSource = metadata.ThumbnailPath;
        }
    }

    public void UpdateOutputFolder(string outputFolder)
    {
        _request = _request with { OutputFolder = outputFolder };
        OnPropertyChanged(nameof(Request));
        OnPropertyChanged(nameof(OutputFolder));
    }

    public RecordingCatalogItem ToCatalogItem() => new(
        CatalogId,
        StreamUrl,
        DisplayName,
        DetailTitle,
        _request.QualityId,
        ThumbnailSource,
        RequiresCredentials,
        _request.Username,
        AddedAt);

    public static RecordingSessionViewModel FromCatalogItem(
        RecordingCatalogItem item,
        string outputFolder)
    {
        return new RecordingSessionViewModel(
            item.Id,
            new RecordingRequest(
                item.StreamUrl,
                outputFolder,
                item.QualityId,
                DateTimeOffset.Now,
                item.Username),
            File.Exists(item.ThumbnailPath) ? item.ThumbnailPath : null,
            item.DisplayName,
            item.DetailTitle,
            item.RequiresCredentials,
            item.AddedAt);
    }

    public static (string DisplayName, string DetailTitle) CreateFriendlyNames(string streamUrl)
    {
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri))
        {
            return ("SOOP 방송", "새 라이브 방송");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        var channel = segments.FirstOrDefault() ?? "SOOP";
        var broadcast = segments.Skip(1).FirstOrDefault();

        return (
            channel,
            broadcast is null ? $"{channel} 라이브 방송" : $"{channel} 방송 · {broadcast}");
    }

    public static bool TryExtractTransferredSize(string line, out string size)
    {
        var matches = TransferSizePattern().Matches(line);
        if (matches.Count == 0)
        {
            size = "";
            return false;
        }

        var raw = matches[0].Groups["size"].Value;
        size = raw
            .Replace("KiB", " KB", StringComparison.OrdinalIgnoreCase)
            .Replace("MiB", " MB", StringComparison.OrdinalIgnoreCase)
            .Replace("GiB", " GB", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void HandleToolOutput(string line)
    {
        LatestTechnicalMessage = line;
        if (TryExtractTransferredSize(line, out var size))
        {
            FileSizeText = size;
        }

        const string destinationMarker = "Destination:";
        var destinationIndex = line.IndexOf(destinationMarker, StringComparison.OrdinalIgnoreCase);
        if (destinationIndex < 0)
        {
            return;
        }

        var destination = line[(destinationIndex + destinationMarker.Length)..].Trim();
        var fileName = Path.GetFileNameWithoutExtension(destination);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            DetailTitle = fileName;
        }
    }

    private void AddActivity(string message)
    {
        Activities.Insert(0, new RecordingActivity(
            DateTimeOffset.Now.ToString("tt h:mm", CultureInfo.GetCultureInfo("ko-KR")),
            message));
        while (Activities.Count > 4)
        {
            Activities.RemoveAt(Activities.Count - 1);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellationTokenSource?.Cancel();
        _recordingService?.Dispose();
        _cancellationTokenSource?.Dispose();
    }

    [GeneratedRegex(@"(?<size>\d+(?:\.\d+)?\s*(?:KiB|MiB|GiB))", RegexOptions.IgnoreCase)]
    private static partial Regex TransferSizePattern();
}
