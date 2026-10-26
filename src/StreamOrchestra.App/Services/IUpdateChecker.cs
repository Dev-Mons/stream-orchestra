namespace StreamOrchestra.App.Services;

public interface IUpdateChecker
{
    // Velopack으로 배포된 앱일 때만 true. 개발 빌드(bin\Debug 등)에서는 업데이트를 확인할 수 없다.
    bool IsInstalled { get; }

    Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    Task<bool> DownloadUpdateAsync(CancellationToken cancellationToken = default);

    void ApplyUpdateAndRestart();
}
