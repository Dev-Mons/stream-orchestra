using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class WebViewProfileServiceTests : IDisposable
{
    private readonly string _profileRoot;

    public WebViewProfileServiceTests()
    {
        _profileRoot = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(3, "A")]
    [InlineData(4, "B")]
    [InlineData(6, "B")]
    [InlineData(7, "C")]
    [InlineData(9, "C")]
    [InlineData(10, "D")]
    [InlineData(12, "D")]
    [InlineData(13, "E")]
    [InlineData(16, "E")]
    public void GetGroupForSlot_MapsSlotsToExpectedProfileGroups(int slotId, string expectedGroupId)
    {
        var service = new WebViewProfileService(_profileRoot);

        var group = service.GetGroupForSlot(slotId);

        Assert.Equal(expectedGroupId, group.Id);
        Assert.EndsWith($"Group{expectedGroupId}", group.UserDataFolder);
        Assert.True(Directory.Exists(group.UserDataFolder));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void GetGroupForSlot_RejectsUnsupportedSlots(int slotId)
    {
        var service = new WebViewProfileService(_profileRoot);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.GetGroupForSlot(slotId));
    }

    [Fact]
    public void Groups_ExposeOnlySlotProfileGroupsWithDistinctPersistentFolders()
    {
        var service = new WebViewProfileService(_profileRoot);

        var groups = service.Groups.OrderBy(group => group.Id).ToArray();

        Assert.Equal(["A", "B", "C", "D", "E"], groups.Select(group => group.Id));
        Assert.DoesNotContain(groups, group => group.Id == service.ExplorerGroup.Id);
        Assert.Equal(
            groups.Length,
            groups
                .Select(group => Path.GetFullPath(group.UserDataFolder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(
            groups,
            group =>
            {
                Assert.StartsWith(
                    Path.GetFullPath(_profileRoot),
                    Path.GetFullPath(group.UserDataFolder),
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(Directory.Exists(group.UserDataFolder));
            });
    }

    [Fact]
    public void ExplorerGroup_UsesPersistentProfileFolder()
    {
        var service = new WebViewProfileService(_profileRoot);

        Assert.Equal("Explorer", service.ExplorerGroup.Id);
        Assert.EndsWith("GroupExplorer", service.ExplorerGroup.UserDataFolder);
        Assert.True(Directory.Exists(service.ExplorerGroup.UserDataFolder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_profileRoot))
        {
            Directory.Delete(_profileRoot, recursive: true);
        }
    }
}
