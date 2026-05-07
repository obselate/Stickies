using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Stickies.Services;

namespace Stickies.Views;

public partial class SettingsWindow : Window
{
    private bool _applied;

    public SettingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => PurgeDaysBox.Focus();
        KeyDown += OnWindowKeyDown;
    }

    /// <summary>
    /// Opens the settings dialog modally and persists on Apply.
    /// Returns true if the user applied changes, false if they cancelled.
    /// </summary>
    public static async Task<bool> ShowAsync(Window owner)
    {
        var win = new SettingsWindow();
        win.PurgeDaysBox.Text = Settings.Current.PurgeAfterDays.ToString();
        await win.ShowDialog(owner);
        return win._applied;
    }

    // ── Key handling ──────────────────────────────────────────────────────────

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnPurgeDaysKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryApply();
            e.Handled = true;
        }
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnApplyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => TryApply();

    // ── Validation & persistence ──────────────────────────────────────────────

    private void TryApply()
    {
        var text = PurgeDaysBox.Text?.Trim() ?? "";
        if (!int.TryParse(text, out var days) || days < 0)
        {
            FlashError();
            return;
        }

        Settings.Update(Settings.Current with { PurgeAfterDays = days });
        _applied = true;
        Close();
    }

    private void FlashError()
    {
        var original = PurgeDaysBox.BorderBrush;
        PurgeDaysBox.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xCC, 0x33, 0x33));
        DispatcherTimer.RunOnce(
            () => PurgeDaysBox.BorderBrush = original,
            System.TimeSpan.FromMilliseconds(600));
    }
}
