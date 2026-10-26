namespace StreamOrchestra.Tests;

public sealed class ReleaseNotesPublishingTests
{
    [Fact]
    public void UpdateChecker_ReadsReleaseNotesFromVelopackFeed()
    {
        var checker = File.ReadAllText(GetSourcePath("StreamOrchestra.App", "Services", "VelopackUpdateChecker.cs"));

        Assert.Contains("info.TargetFullRelease.NotesMarkdown", checker);
    }

    [Fact]
    public void ReleaseScript_PacksReleaseNotesFileIntoVelopackFeed()
    {
        var script = File.ReadAllText(GetReleaseScriptPath());

        Assert.Contains("[string]$ReleaseNotesPath", script);
        Assert.Contains("Join-Path $repoRoot \"release-notes/$Version.md\"", script);
        Assert.Contains("$packArgs += @(\"--releaseNotes\", (Resolve-Path $ReleaseNotesPath).Path)", script);
        Assert.Contains("vpk @packArgs", script);
        // 빈 파일은 넘기지 않는다.
        Assert.Contains("Packing without release notes.", script);
    }

    [Fact]
    public void ReleaseScript_GeneratesNotesFromCommitsWhenFileIsMissing()
    {
        var script = File.ReadAllText(GetReleaseScriptPath());

        Assert.Contains("function New-ReleaseNotesFromCommits", script);
        Assert.Contains("git tag --list \"v*\" --sort=-v:refname --merged HEAD", script);
        Assert.Contains("git log \"$previousTag..HEAD\" --no-merges --format=\"%s\"", script);
        Assert.Contains("New-ReleaseNotesFromCommits -Version $Version -Path $ReleaseNotesPath", script);
        Assert.Contains("-Encoding utf8NoBOM", script);
    }

    [Fact]
    public void ReleaseScript_WritesSameNotesToGitHubReleaseBodyAfterUpload()
    {
        var script = File.ReadAllText(GetReleaseScriptPath());
        var uploadIndex = script.IndexOf("vpk upload github", StringComparison.Ordinal);
        var bodyIndex = script.IndexOf("Set-GitHubReleaseBody -RepositoryUrl $RepositoryUrl -Tag \"v$Version\" -Body $releaseNotes -Token $token", StringComparison.Ordinal);

        Assert.True(uploadIndex >= 0);
        Assert.True(bodyIndex > uploadIndex);
        Assert.Contains("function Set-GitHubReleaseBody", script);
        Assert.Contains("https://api.github.com/repos/$repoPath/releases/tags/$Tag", script);
        Assert.Contains("Invoke-RestMethod -Method Patch", script);
        Assert.Contains("@{ body = $Body } | ConvertTo-Json", script);
        // 본문 갱신이 실패해도 이미 게시된 릴리스를 실패로 만들지 않는다.
        Assert.Contains("Could not update the GitHub release body", script);
    }

    private static string GetReleaseScriptPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "scripts",
            "release.ps1"));
    }

    private static string GetSourcePath(params string[] parts)
    {
        return Path.GetFullPath(Path.Combine(
            [AppContext.BaseDirectory, "..", "..", "..", "..", .. parts]));
    }
}
