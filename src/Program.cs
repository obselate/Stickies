using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Avalonia;

namespace Stickies;

internal static class Program
{
    public const string MutexName = "Stickies.SingleInstance";
    public const string PipeName = "Stickies.IPC";

    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        var verb = ParseVerb(args);

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            SendVerb(verb);
            return;
        }

        try
        {
            App.StartupVerb = verb;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }
    }

    private static string ParseVerb(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--new") return "NEW";
            if (args[i] == "--show") return "SHOW";
        }
        return "SHOW";
    }

    private static void SendVerb(string verb)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var w = new StreamWriter(client) { AutoFlush = true };
            w.WriteLine(verb);
        }
        catch
        {
            // running instance is unreachable; nothing useful to do without a UI
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // Drop the GPU/ANGLE renderer — sticky notes are static text, software path is plenty.
                // Lets us exclude av_libglesv2.dll from the publish output (-5.4MB).
                //
                // Note: this means TransparencyLevelHint=Transparent windows can't show desktop through
                // uncovered pixels — Avalonia's Win32 software path composes via RedirectionSurface,
                // which has no per-pixel alpha. The lock tape works around this with a Win32
                // layered-window TapeHost (see ITapeHost / WinTapeHost) rather than an in-window
                // transparent buffer.
                RenderingMode = new[] { Win32RenderingMode.Software },
            })
            .LogToTrace();
}
