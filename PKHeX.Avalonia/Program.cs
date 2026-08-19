using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

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
            // The default UI font lacks glyphs for the symbols we use (gender ♂/♀,
            // arrows ◀/▶, folder/sparkle emoji, etc.). Avalonia won't fall back unless
            // told to, so register symbol/emoji fonts for each platform. Families that
            // don't exist on the running OS are skipped; the first with the glyph wins.
            .With(new FontManagerOptions
            {
                FontFallbacks = new List<FontFallback>
                {
                    // macOS
                    new() { FontFamily = new FontFamily("Apple Symbols") },
                    new() { FontFamily = new FontFamily("Apple Color Emoji") },
                    new() { FontFamily = new FontFamily("Arial Unicode MS") },
                    // Linux
                    new() { FontFamily = new FontFamily("Noto Sans Symbols2") },
                    new() { FontFamily = new FontFamily("Noto Color Emoji") },
                    new() { FontFamily = new FontFamily("DejaVu Sans") },
                    // Windows
                    new() { FontFamily = new FontFamily("Segoe UI Symbol") },
                    new() { FontFamily = new FontFamily("Segoe UI Emoji") },
                },
            })
            .LogToTrace();
}
