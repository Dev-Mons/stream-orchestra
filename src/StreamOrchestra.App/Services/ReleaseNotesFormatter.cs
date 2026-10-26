using System.Text.RegularExpressions;

namespace StreamOrchestra.App.Services;

// 업데이트 안내 창(MessageBox)은 마크다운을 그리지 못하므로, 릴리스 노트를 읽기 쉬운 일반 텍스트로 바꾼다.
public static partial class ReleaseNotesFormatter
{
    public const int DefaultMaxLines = 15;
    public const string TruncatedSuffix = "…(이하 생략)";

    public static string? ToPlainText(string? markdown, int maxLines = DefaultMaxLines)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var lines = new List<string>();
        var isInCodeFence = false;
        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (FenceLineRegex().IsMatch(rawLine))
            {
                isInCodeFence = !isInCodeFence;
                continue;
            }

            var line = isInCodeFence ? rawLine.TrimEnd() : FormatLine(rawLine);
            if (line is null)
            {
                continue;
            }

            // 빈 줄은 문단 구분으로 하나만 남긴다.
            if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0))
            {
                continue;
            }

            lines.Add(line);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var limit = Math.Max(1, maxLines);
        if (lines.Count > limit)
        {
            lines = lines.Take(limit).ToList();
            lines.Add(TruncatedSuffix);
        }

        return string.Join("\n", lines);
    }

    // 표시하지 않을 줄(구분선 등)은 null을 돌려준다.
    private static string? FormatLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return "";
        }

        if (HorizontalRuleRegex().IsMatch(rawLine))
        {
            return null;
        }

        var headingMatch = HeadingRegex().Match(rawLine);
        if (headingMatch.Success)
        {
            return FormatInline(headingMatch.Groups["text"].Value);
        }

        var bulletMatch = BulletRegex().Match(rawLine);
        if (bulletMatch.Success)
        {
            var depth = MeasureIndent(bulletMatch.Groups["indent"].Value) / 2;
            return $"{new string(' ', depth * 2)}• {FormatInline(bulletMatch.Groups["text"].Value)}";
        }

        return FormatInline(rawLine.Trim());
    }

    private static string FormatInline(string text)
    {
        var result = ImageRegex().Replace(text, "");
        result = LinkRegex().Replace(result, "${text}");
        result = InlineCodeRegex().Replace(result, "${code}");
        result = BoldRegex().Replace(result, "${text}");
        result = ItalicRegex().Replace(result, "${text}");
        result = HtmlTagRegex().Replace(result, "");
        return result.Trim();
    }

    private static int MeasureIndent(string indent)
    {
        var width = 0;
        foreach (var character in indent)
        {
            width += character == '\t' ? 4 : 1;
        }

        return width;
    }

    [GeneratedRegex(@"^\s*(```|~~~)")]
    private static partial Regex FenceLineRegex();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(?<text>.*?)\s*#*\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?<indent>\s*)[-*+]\s+(?<text>.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"`(?<code>[^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"(\*\*|__)(?<text>.+?)\1")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<![\w*])\*(?<text>[^*\s][^*]*?)\*(?![\w*])")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex HtmlTagRegex();
}
