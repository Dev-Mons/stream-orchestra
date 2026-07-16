namespace StreamOrchestra.App.Services;

public interface IUpdateChecker
{
    Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    Task<bool> DownloadUpdateAsync(CancellationToken cancellationToken = default);

    void ApplyUpdateAndRestart();
}
