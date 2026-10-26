namespace StreamOrchestra.App.Models;

// Win32 화면 좌표(물리 픽셀) 사각형. Right/Bottom은 포함하지 않는 경계다.
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public ScreenRect Offset(int dx, int dy)
    {
        return new ScreenRect(Left + dx, Top + dy, Right + dx, Bottom + dy);
    }
}
