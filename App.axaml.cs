using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Stickies;

public partial class App : Application
{
    public static NoteStore Store { get; } = new();
    public static string StartupVerb { get; set; } = "SHOW";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            OpenActiveNotes(desktop);

            if (StartupVerb == "NEW")
                MainWindow.SpawnNew(null);

            StartIpcServer(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void OpenActiveNotes(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var notes = Store.LoadActive();
        if (notes.Count == 0)
            notes.Add(Store.Create(null, null, 280, 280));

        var openIds = CollectOpenNoteIds(desktop);
        MainWindow? first = null;
        foreach (var row in notes)
        {
            if (openIds.Contains(row.Id)) continue;
            var w = new MainWindow(row);
            if (first is null && desktop.MainWindow is null)
            {
                first = w;
                desktop.MainWindow = w;
            }
            else
            {
                w.Show();
            }
        }
    }

    private static HashSet<long> CollectOpenNoteIds(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var ids = new HashSet<long>();
        foreach (var w in desktop.Windows)
            if (w is MainWindow mw) ids.Add(mw.NoteId);
        return ids;
    }

    private static void StartIpcServer(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var t = new Thread(() => RunIpcServer(desktop))
        {
            IsBackground = true,
            Name = "Stickies.IPC"
        };
        t.Start();
    }

    private static void RunIpcServer(IClassicDesktopStyleApplicationLifetime desktop)
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    Program.PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);
                server.WaitForConnection();
                using var r = new StreamReader(server);
                var verb = r.ReadLine();
                if (string.IsNullOrEmpty(verb)) continue;
                Dispatcher.UIThread.Post(() => HandleVerb(desktop, verb));
            }
            catch
            {
                // pipe error: loop and re-listen
            }
        }
    }

    private static void HandleVerb(IClassicDesktopStyleApplicationLifetime desktop, string verb)
    {
        if (verb == "NEW")
        {
            MainWindow.SpawnNew(FrontmostNote(desktop));
            return;
        }
        if (verb == "SHOW")
        {
            ShowAll(desktop);
            return;
        }
        if (verb.StartsWith("OPEN:", StringComparison.Ordinal)
            && long.TryParse(verb.AsSpan(5), out var id))
        {
            OpenById(desktop, id);
            return;
        }
    }

    private static MainWindow? FrontmostNote(IClassicDesktopStyleApplicationLifetime desktop)
    {
        MainWindow? last = null;
        foreach (var w in desktop.Windows)
            if (w is MainWindow mw) last = mw;
        return last;
    }

    private static void ShowAll(IClassicDesktopStyleApplicationLifetime desktop)
    {
        bool any = false;
        foreach (var w in desktop.Windows)
        {
            if (w is MainWindow mw)
            {
                any = true;
                mw.Show();
                mw.Activate();
            }
        }
        if (!any) OpenActiveNotes(desktop);
    }

    private static void OpenById(IClassicDesktopStyleApplicationLifetime desktop, long id)
    {
        foreach (var w in desktop.Windows)
        {
            if (w is MainWindow mw && mw.NoteId == id)
            {
                mw.Show();
                mw.Activate();
                return;
            }
        }
        var row = Store.LoadById(id);
        if (row is null) return;
        var fresh = new MainWindow(row);
        fresh.Show();
    }
}
