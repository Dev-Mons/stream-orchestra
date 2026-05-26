namespace StreamOrchestra.App.Models;

public sealed record PlaybackTestPlan(
    int TargetPlaybackCount,
    IReadOnlyList<int> ActiveSlotIds,
    IReadOnlyList<int> InactiveSlotIds);
