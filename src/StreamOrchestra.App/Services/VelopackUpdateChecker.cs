using Velopack;
using Velopack.Sources;

namespace StreamOrchestra.App.Services;

public sealed class VelopackUpdateChecker : IUpdateChecker
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _cachedUpdateInfo;

    public VelopackUpdateChecker(string repositoryUrl)
    {
        _manager = new UpdateManager(new GithubSource(repositoryUrl, null, false));
    }

    public async Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        _cachedUpdateInfo = info;

        if (info is null)
        {
            return null;
        }

        return new AvailableUpdate(info.TargetFullRelease.Version.ToString());
    }

    public async Task DownloadAndApplyAsync(CancellationToken cancellationToken = default)
    {
        var info = _cachedUpdateInfo ?? await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        _manager.ApplyUpdatesAndRestart(info);
    }
}
