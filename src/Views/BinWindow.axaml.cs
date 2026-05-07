using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Stickies.Models;
using Stickies.Services;

namespace Stickies.Views;

public partial class BinWindow : Window
{
    public BinWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
    }

    /// <summary>Opens the Recycle Bin modally. Mirrors HsvColorPicker.ShowAsync pattern.</summary>
    public static async Task ShowAsync(Window? owner)
    {
        var win = new BinWindow();
        win.PopulateRows();
        if (owner is not null)
            await win.ShowDialog(owner);
        else
            win.Show();
    }

    // ── Title bar ─────────────────────────────────────────────────────────────

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    // ── Key handling ──────────────────────────────────────────────────────────

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    // ── Row building ──────────────────────────────────────────────────────────

    private void PopulateRows()
    {
        BinPanel.Children.Clear();
        var notes = App.Store.LoadDeleted();

        if (notes.Count == 0)
        {
            EmptyLabel.IsVisible = true;
            BinScroll.IsVisible = false;
            return;
        }

        EmptyLabel.IsVisible = false;
        BinScroll.IsVisible = true;

        bool alternate = false;
        foreach (var note in notes)
        {
            BinPanel.Children.Add(BuildRow(note, alternate));
            alternate = !alternate;
        }
    }

    private Control BuildRow(Note note, bool shaded)
    {
        // Text preview: first 60 chars, ellipsis if truncated.
        var preview = note.Text.Length > 60
            ? note.Text.Substring(0, 60) + "…"
            : note.Text;
        if (string.IsNullOrWhiteSpace(preview))
            preview = "(empty note)";

        var previewBlock = new TextBlock
        {
            Text = preview,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var ageBlock = new TextBlock
        {
            Text = RelativeTime(note.DeletedAt),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var restoreBtn = new Button
        {
            Content = "Restore",
            FontSize = 11,
            Height = 24,
            Padding = new Thickness(8, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var deleteBtn = new Button
        {
            Content = "Delete forever",
            FontSize = 11,
            Height = 24,
            Padding = new Thickness(8, 2),
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33)),
        };

        // Capture note in local for closure.
        var capturedNote = note;
        restoreBtn.Click += (_, _) => OnRestoreClicked(capturedNote);
        deleteBtn.Click += (_, _) => OnHardDeleteClicked(capturedNote);

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        rightPanel.Children.Add(ageBlock);
        rightPanel.Children.Add(restoreBtn);
        rightPanel.Children.Add(deleteBtn);

        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Height = 40,
        };
        Grid.SetColumn(previewBlock, 0);
        Grid.SetColumn(rightPanel, 1);
        rowGrid.Children.Add(previewBlock);
        rowGrid.Children.Add(rightPanel);

        var rowBorder = new Border
        {
            Padding = new Thickness(10, 0),
            Background = shaded
                ? new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Child = rowGrid,
        };

        return rowBorder;
    }

    // ── Restore ───────────────────────────────────────────────────────────────

    private void OnRestoreClicked(Note note)
    {
        const int W = 280, H = 280;

        // Use original geometry; fall back to fresh-spawn coords if off-screen.
        if (note.X is int ox && note.Y is int oy
            && PlacementService.IsOnAnyScreen(Screens?.All, ox, oy, note.Width, note.Height))
        {
            // Geometry is still on-screen — restore in place.
        }
        else
        {
            // Off-screen: compute fresh spawn position near any open note.
            var origin = new PixelPoint(200, 200);
            if (Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var w in desktop.Windows)
                    if (w is MainWindow mw) { origin = mw.Position; break; }
            }

            int newW = note.Width > 0 ? note.Width : W;
            int newH = note.Height > 0 ? note.Height : H;
            var pos = PlacementService.FindAvailableSpace(null, origin, newW, newH);
            App.Store.UpdateBounds(note.Id, pos.X, pos.Y, newW, newH);
        }

        App.Store.Restore(note.Id);
        App.OpenById(note.Id);

        // Refresh the list.
        PopulateRows();
    }

    // ── Hard delete ───────────────────────────────────────────────────────────

    private void OnHardDeleteClicked(Note note)
    {
        App.Store.HardDelete(note.Id);
        PopulateRows();
    }

    // ── Relative-time helper ──────────────────────────────────────────────────

    private static string RelativeTime(DateTimeOffset? deletedAt)
    {
        if (deletedAt is null) return "";
        var ago = DateTimeOffset.UtcNow - deletedAt.Value;
        if (ago.TotalSeconds < 90)   return "just now";
        if (ago.TotalMinutes < 90)   return $"{(int)ago.TotalMinutes} min ago";
        if (ago.TotalHours < 36)     return $"{(int)ago.TotalHours} hours ago";
        if (ago.TotalDays < 60)      return $"{(int)ago.TotalDays} days ago";
        if (ago.TotalDays < 365 * 2) return $"{(int)(ago.TotalDays / 30)} months ago";
        return $"{(int)(ago.TotalDays / 365)} years ago";
    }
}
