namespace StreamOrchestra.App.Services;

// ReleaseNotes는 배포 때 vpk pack --releaseNotes로 넣은 마크다운이다. 넣지 않은 버전은 null이다.
public sealed record AvailableUpdate(string Version, string? ReleaseNotes = null);
