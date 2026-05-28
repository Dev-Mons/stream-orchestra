namespace StreamOrchestra.App.Services;

public interface IUpdateChecker
{
    Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    Task DownloadAndApplyAsync(CancellationToken cancellationToken = default);
}
