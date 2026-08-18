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
        Assert.Equal(SyncManualDelaySchema.CurrentVersion, member.DelayModelVersion);
        Assert.Equal(0, member.AlgorithmPriorMs);
        Assert.Equal(60000, member.UserResidualMs);
        Assert.Equal("https://play.sooplive.com/channel", member.CalibratedStreamUrl);
    }

    [Fact]
    public void Normalize_MigratesLegacyFinalDelayAndPreservesVersionedComponents()
    {
        var legacy = SyncPresetNormalizationService.Normalize(new SyncGroupPreset
        {
            Members = [new SyncMemberPreset { SlotId = 1, ManualDelayMs = 1200 }]
        });
        var versioned = SyncPresetNormalizationService.Normalize(new SyncGroupPreset
        {
            Members = [new SyncMemberPreset
            {
                SlotId = 1,
                ManualDelayMs = 9999,
                DelayModelVersion = SyncManualDelaySchema.CurrentVersion,
                AlgorithmPriorMs = 500,
                UserResidualMs = 200
            }]
        });

        var legacyMember = Assert.Single(legacy.Members);
        Assert.Equal(0, legacyMember.AlgorithmPriorMs);
        Assert.Equal(1200, legacyMember.UserResidualMs);
        Assert.Equal(1200, legacyMember.ManualDelayMs);
        var versionedMember = Assert.Single(versioned.Members);
        Assert.Equal(500, versionedMember.AlgorithmPriorMs);
        Assert.Equal(200, versionedMember.UserResidualMs);
        Assert.Equal(700, versionedMember.ManualDelayMs);
    }

    [Fact]
    public void Normalize_MissingPresetUsesBackwardCompatibleDefaults()
    {
        var normalized = SyncPresetNormalizationService.Normalize(null);

        Assert.Equal(3000, normalized.MinimumSafetyDelayMs);
        Assert.Empty(normalized.Members);
    }
}
