using System.Collections.Concurrent;
using System.IO;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class WebViewProfileService
{
    // 생방송 영상 조각이 HTTP 캐시에 쌓여 그룹마다 수백 MB까지 커지므로 그룹별 상한을 둔다.
    // 다시 재생할 일이 없는 데이터라 상한을 낮게 잡아도 재생에는 영향이 없다.
    public const long DiskCacheSizeBytes = 64L * 1024 * 1024;

    private readonly ConcurrentDictionary<string, Lazy<Task<CoreWebView2Environment>>> _environments = new();
    private readonly IReadOnlyDictionary<string, ProfileGroup> _groups;

    public WebViewProfileService(string? baseProfileFolder = null)
    {
        BaseProfileFolder = baseProfileFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "Profiles");

        Directory.CreateDirectory(BaseProfileFolder);
        ExplorerGroup = CreateGroup("Explorer");

        _groups = SlotProfileGroupMapping.GroupIds
            .Select(CreateGroup)
            .ToDictionary(group => group.Id, StringComparer.OrdinalIgnoreCase);
    }

    public string BaseProfileFolder { get; }

    public ProfileGroup ExplorerGroup { get; }

    public SoopLoginSessionCookieService SoopLoginSessionCookies { get; } = new();

    public IReadOnlyCollection<ProfileGroup> Groups => _groups.Values.ToArray();

    public ProfileGroup GetGroupForSlot(int slotId)
    {
        var groupId = SlotProfileGroupMapping.GetGroupIdForSlot(slotId);
        return _groups[groupId];
    }

    public Task<CoreWebView2Environment> GetEnvironmentAsync(ProfileGroup group)
    {
        var lazyEnvironment = _environments.GetOrAdd(
            group.Id,
            _ => new Lazy<Task<CoreWebView2Environment>>(
                () => CreateEnvironmentAsync(group),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyEnvironment.Value;
    }

    private ProfileGroup CreateGroup(string id)
    {
        var folder = Path.Combine(BaseProfileFolder, $"Group{id}");
        Directory.CreateDirectory(folder);

        return new ProfileGroup(
            id,
            $"SOOP Group {id}",
            folder);
    }

    // 같은 userDataFolder를 쓰는 환경은 옵션이 모두 같아야 하므로 환경 생성은 이 경로로만 한다.
    public static CoreWebView2EnvironmentOptions CreateEnvironmentOptions()
    {
        return new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = $"--disk-cache-size={DiskCacheSizeBytes}"
        };
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync(ProfileGroup group)
    {
        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: group.UserDataFolder,
            CreateEnvironmentOptions());
    }
}
