using System.Collections.Concurrent;
using System.IO;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class WebViewProfileService
{
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

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync(ProfileGroup group)
    {
        var options = new CoreWebView2EnvironmentOptions();
        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: group.UserDataFolder,
            options);
    }
}
