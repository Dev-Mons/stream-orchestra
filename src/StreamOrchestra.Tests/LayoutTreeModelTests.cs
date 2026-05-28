using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.Tests;

public sealed class LayoutTreeModelTests
{
    [Fact]
    public void LayoutTreeDocument_RoundTripsSplitAndLeafNodes()
    {
        var document = new LayoutTreeDocument
        {
            Root = new SplitLayoutNode
            {
                Id = "split-root",
                Orientation = SplitOrientation.Horizontal,
                Weights = [1, 2],
                Children =
                [
                    new LeafLayoutNode
                    {
                        Id = "leaf-1",
                        SlotId = 1,
                        Items = [new LeafContentRef("stream", 1, "https://example.com/a", "A")]
                    },
                    new LeafLayoutNode
                    {
                        Id = "leaf-2",
                        SlotId = 2,
                        Items = []
                    }
                ]
            },
            ActiveLeafId = "leaf-1"
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var json = JsonSerializer.Serialize(document, options);
        var restored = JsonSerializer.Deserialize<LayoutTreeDocument>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(1, restored!.Version);
        Assert.Equal("leaf-1", restored.ActiveLeafId);
        var split = Assert.IsType<SplitLayoutNode>(restored.Root);
        Assert.Equal(SplitOrientation.Horizontal, split.Orientation);
        Assert.Equal([1, 2], split.Weights);
        Assert.Collection(
            split.Children,
            child => Assert.Equal(1, Assert.IsType<LeafLayoutNode>(child).SlotId),
            child => Assert.Equal(2, Assert.IsType<LeafLayoutNode>(child).SlotId));
    }
}
