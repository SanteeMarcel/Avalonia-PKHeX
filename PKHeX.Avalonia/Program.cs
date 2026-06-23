using System;
using Avalonia;

namespace PKHeX.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine($"[Unhandled] {e.ExceptionObject}");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"[UnobservedTask] {e.Exception}");
            e.SetObserved();
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
