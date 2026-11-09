using System;
using Velopack;

namespace Lateraroot;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Handle update/install hooks before WPF, the instance mutex or the library is opened.
#if !GLASS_PREVIEW
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
#endif
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
