using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class ReleaseNotesFormatterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\n  ")]
    [InlineData("---\n\n***")]
    public void ToPlainText_ReturnsNullWhenThereIsNothingToShow(string? markdown)
    {
        Assert.Null(ReleaseNotesFormatter.ToPlainText(markdown));
    }

    [Fact]
    public void ToPlainText_ConvertsHeadingsAndBulletsForMessageBox()
    {
        var markdown = "## 새 기능\r\n- 슬롯을 창 밖에 놓으면 **크롬**으로 열기\r\n* 업데이트 창에 `변경 내역` 표시\r\n\r\n## 수정\r\n+ 제목 식별 수정";

        var text = ReleaseNotesFormatter.ToPlainText(markdown);

        Assert.Equal(
            "새 기능\n• 슬롯을 창 밖에 놓으면 크롬으로 열기\n• 업데이트 창에 변경 내역 표시\n\n수정\n• 제목 식별 수정",
            text);
    }

    [Fact]
    public void ToPlainText_IndentsNestedBullets()
    {
        var text = ReleaseNotesFormatter.ToPlainText("- 상위\n  - 하위\n\t- 탭 하위");

        Assert.Equal("• 상위\n  • 하위\n    • 탭 하위", text);
    }

    [Fact]
    public void ToPlainText_KeepsLinkTextAndDropsImagesAndHtml()
    {
        var text = ReleaseNotesFormatter.ToPlainText(
            "- [이슈 12](https://github.com/Dev-Mons/stream-orchestra/issues/12) 수정 ![스크린샷](shot.png)<br>\n- *기울임* 과 __굵게__");

        Assert.Equal("• 이슈 12 수정\n• 기울임 과 굵게", text);
    }

    [Fact]
    public void ToPlainText_DoesNotTreatArithmeticAsterisksAsItalic()
    {
        Assert.Equal("• 2 * 3 * 4 계산", ReleaseNotesFormatter.ToPlainText("- 2 * 3 * 4 계산"));
    }

    [Fact]
    public void ToPlainText_CollapsesBlankLinesAndDropsCodeFenceMarkers()
    {
        var text = ReleaseNotesFormatter.ToPlainText("\n\n첫 줄\n\n\n\n```\ncode line\n```\n\n\n끝\n\n");

        Assert.Equal("첫 줄\n\ncode line\n\n끝", text);
    }

    [Fact]
    public void ToPlainText_TruncatesLongNotes()
    {
        var markdown = string.Join("\n", Enumerable.Range(1, 20).Select(index => $"- 항목 {index}"));

        var text = ReleaseNotesFormatter.ToPlainText(markdown, maxLines: 3);

        Assert.Equal($"• 항목 1\n• 항목 2\n• 항목 3\n{ReleaseNotesFormatter.TruncatedSuffix}", text);
    }

    [Fact]
    public void ToPlainText_UsesFifteenLinesByDefault()
    {
        var markdown = string.Join("\n", Enumerable.Range(1, 16).Select(index => $"- 항목 {index}"));

        var lines = ReleaseNotesFormatter.ToPlainText(markdown)!.Split('\n');

        Assert.Equal(ReleaseNotesFormatter.DefaultMaxLines + 1, lines.Length);
        Assert.Equal("• 항목 15", lines[^2]);
        Assert.Equal(ReleaseNotesFormatter.TruncatedSuffix, lines[^1]);
    }
}
