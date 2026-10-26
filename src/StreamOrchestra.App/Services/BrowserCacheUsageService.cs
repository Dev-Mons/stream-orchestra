using System.IO;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class BrowserCacheUsageService
{
    // WebView2는 userDataFolder\EBWebView\Default에 기본 프로필을 만든다.
    // 디스크 캐시 삭제(DiskCache)로 비워지는 폴더만 센다. 쿠키·로그인 데이터는 포함하지 않는다.
    private static readonly string[] CacheFolderNames = ["Cache", "Code Cache"];

    private static readonly EnumerationOptions FileEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0
    };

    public long GetCacheSizeBytes(ProfileGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var profileFolder = Path.Combine(group.UserDataFolder, "EBWebView", "Default");
        long total = 0;
        foreach (var folderName in CacheFolderNames)
        {
            total += GetFolderSizeBytes(Path.Combine(profileFolder, folderName));
        }

        return total;
    }

    public static string FormatSize(long bytes)
    {
        const double megabyte = 1024 * 1024;
        const double gigabyte = megabyte * 1024;

        return bytes >= gigabyte
            ? $"{bytes / gigabyte:0.0}GB"
            : $"{bytes / megabyte:0}MB";
    }

    private static long GetFolderSizeBytes(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        try
        {
            // 열거 시점의 크기를 쓰므로 브라우저가 사용 중인 파일도 잠금 없이 셀 수 있다.
            return new DirectoryInfo(folder)
                .EnumerateFiles("*", FileEnumerationOptions)
                .Sum(file => file.Length);
        }
        catch (DirectoryNotFoundException)
        {
            // 측정 도중 브라우저가 캐시 폴더를 지운 경우.
            return 0;
        }
    }
}
