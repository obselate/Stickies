using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Stickies;

public partial class App : Application
{
    public static NoteStore Store { get; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var notes = Store.LoadActive();
            if (notes.Count == 0)
                notes.Add(Store.Create(null, null, 280, 280));

            MainWindow? first = null;
            foreach (var row in notes)
            {
                var w = new MainWindow(row);
                if (first is null)
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
        base.OnFrameworkInitializationCompleted();
    }
}
