using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncPresetNormalizationServiceTests
{
    [Fact]
    public void Normalize_ClampsValuesRemovesInvalidMembersAndKeepsLastDuplicate()
    {
        var preset = new SyncGroupPreset
        {
            MinimumSafetyDelayMs = 99999,
            Members =
            [
                new SyncMemberPreset { SlotId = 0, ManualDelayMs = 500 },
                new SyncMemberPreset { SlotId = 2, ManualDelayMs = 300 },
                new SyncMemberPreset
                {
                    SlotId = 2,
                    ManualDelayMs = 70000,
                    CalibratedStreamUrl = "HTTPS://PLAY.SOOPLIVE.COM/channel/?token=secret#fragment"
                },
                new SyncMemberPreset { SlotId = 17, ManualDelayMs = -500 }
            ]
        };

        var normalized = SyncPresetNormalizationService.Normalize(preset);

        Assert.Equal(15000, normalized.MinimumSafetyDelayMs);
        var member = Assert.Single(normalized.Members);
        Assert.Equal(2, member.SlotId);
        Assert.Equal(60000, member.ManualDelayMs);
        Assert.Equal("https://play.sooplive.com/channel", member.CalibratedStreamUrl);
    }

    [Fact]
    public void Normalize_PreservesManualDelay()
    {
        var normalized = SyncPresetNormalizationService.Normalize(new SyncGroupPreset
        {
            Members = [new SyncMemberPreset { SlotId = 1, ManualDelayMs = 1200 }]
        });

        Assert.Equal(1200, Assert.Single(normalized.Members).ManualDelayMs);
    }

    [Fact]
    public void Normalize_MissingPresetUsesBackwardCompatibleDefaults()
    {
        var normalized = SyncPresetNormalizationService.Normalize(null);

        Assert.Equal(3000, normalized.MinimumSafetyDelayMs);
        Assert.Empty(normalized.Members);
    }
}
