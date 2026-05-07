using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Stickies.Views;

namespace Stickies.Services;

// App-wide hide/show toggle. Hidden notes don't fire PositionChanged or
// SizeChanged so persistence is undisturbed. Hotkey binding lives in
// App.axaml.cs; this service holds only the toggle logic and the
// _allHidden flag used to disambiguate the next press.
internal static class Visibility
{
    private static bool _allHidden;

    public static void Toggle()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        if (_allHidden)
        {
            foreach (var w in desktop.Windows)
                if (w is MainWindow mw) mw.Show();
            _allHidden = false;
        }
        else
        {
            foreach (var w in desktop.Windows)
                if (w is MainWindow mw && mw.IsVisible) mw.Hide();
            _allHidden = true;
        }
    }

    // Called by NoteSpawner after a fresh visible window appears, so a
    // subsequent Toggle() correctly hides instead of trying to "restore".
    public static void NoteSurfaced() => _allHidden = false;
}
