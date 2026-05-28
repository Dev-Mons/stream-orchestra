using System;
using Velopack;

namespace StreamOrchestra.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            VelopackApp.Build().Run();
        }
        catch
        {
            // Velopack 부트스트랩 실패는 무시하고 일반 시작 진행
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
