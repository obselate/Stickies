using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Stickies.Animation;
using Stickies.Markdown;
using Stickies.Models;
using Stickies.Platform;
using Stickies.Services;

namespace Stickies.Views;

public partial class MainWindow : Window
{
    private readonly long _noteId;
    private readonly DispatcherTimer _saveTextTimer;
    private readonly DispatcherTimer _saveBoundsTimer;
    private bool _ready;
    private bool _pinned;
    private bool _locked;
    internal bool _isAnimating;
    internal string _color = "#FFF59E";

    // Created lazily on Opened (needs the platform window handle on Win32).
    // Disposed on Closing. Locked state drives Show/Hide/Update calls.
    private ITapeHost? _tapeHost;

    // Two-click delete: first click arms (red X, 3s window); second confirms.
    private DispatcherTimer? _deleteRevertTimer;
    private bool _deleteArmed;

    private static readonly Geometry DeleteIconNormalGeo = Geometry.Parse("M4,5 H12 V12 H4 Z M3,5 H13 M6,4 H10 V5 M6.5,7 V11 M9.5,7 V11");
    private static readonly Geometry DeleteIconArmedGeo = Geometry.Parse("M4,4 L12,12 M12,4 L4,12");
    private static readonly Geometry LockClosedGeo = Geometry.Parse("M3,7 L13,7 L13,13 L3,13 Z M5,7 V5 A3,3 0 0,1 11,5 V7");
    private static readonly Geometry LockOpenGeo = Geometry.Parse("M3,7 L13,7 L13,13 L3,13 Z M5,7 V5 A3,3 0 0,1 11,5 V6");
    private static readonly IBrush DeleteNormalBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly IBrush DeleteArmedBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x32, 0x2D));
    private static readonly IBrush PinFillBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

    public long NoteId => _noteId;

    public MainWindow() : this(App.Store.Create(null, null, 280, 280)) { }

    public MainWindow(Note row)
    {
        _noteId = row.Id;
        InitializeComponent();

        Width = row.Width;
        Height = row.Height;

        if (row.X is int x && row.Y is int y && PlacementService.IsOnAnyScreen(Screens?.All, x, y, row.Width, row.Height))
            Position = new PixelPoint(x, y);

        NoteText.Text = row.Text;
        ApplyPinned(row.Pinned);
        ApplyColor(row.Color);
        ApplyLocked(row.Locked);

        _saveTextTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTextTimer.Tick += OnSaveTextTick;

        _saveBoundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveBoundsTimer.Tick += OnSaveBoundsTick;

        PositionChanged += (_, _) =>
        {
            if (!_ready) return;
            Restart(_saveBoundsTimer);
            if (_locked) UpdateTape();
        };

        Opened += OnWindowOpened;
        Closing += (_, _) =>
        {
            FlushPendingWrites();
            _deleteRevertTimer?.Stop();
            _tapeHost?.Dispose();
            _tapeHost = null;
        };
        KeyDown += OnKeyDown;

        NoteText.GotFocus += OnNoteFocusGot;
        NoteText.LostFocus += OnNoteFocusLost;

        // Initial render: if the loaded note has text, show the rendered view;
        // otherwise leave the TextBox visible so a freshly-created empty note
        // is immediately typable without an extra click.
        if (!string.IsNullOrEmpty(NoteText.Text))
        {
            RenderText();
            NoteText.IsVisible = false;
            RenderedScroll.IsVisible = true;
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _ready = true;
        OnSaveBoundsTick(null, EventArgs.Empty);

        _tapeHost = TapeHost.Create(this);
        if (_locked)
        {
            UpdateTape();
            _tapeHost.Show();
        }

        Stickies.Services.Visibility.NoteSurfaced();

        // Only auto-focus if the textbox is the visible surface (i.e. the note is
        // empty). For existing notes loaded at startup, the rendered view is the
        // visible surface — preserve "open the app, see your notes, click to edit".
        if (!NoteText.IsVisible) return;

        Dispatcher.UIThread.Post(
            () => NoteText.Focus(),
            DispatcherPriority.Background);
    }

    private void OnNoteFocusGot(object? sender, GotFocusEventArgs e)
    {
        RenderedScroll.IsVisible = false;
        NoteText.IsVisible = true;
    }

    private void OnNoteFocusLost(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Keep the TextBox visible while the note is empty — there's nothing
        // to render and an empty rendered view is just an awkward blank that
        // requires an extra click to start typing again.
        if (string.IsNullOrEmpty(NoteText.Text)) return;
        RenderText();
        NoteText.IsVisible = false;
        RenderedScroll.IsVisible = true;
    }

    private void OnRenderedTextPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return; // an interactive inline (link, checkbox) already handled it
        NoteText.IsVisible = true;
        RenderedScroll.IsVisible = false;
        NoteText.Focus();
        NoteText.CaretIndex = NoteText.Text?.Length ?? 0;
    }

    private void RenderText()
    {
        Color body;
        try { body = Color.Parse(_color); }
        catch { body = Color.FromRgb(0xFF, 0xF5, 0x9E); }

        RenderedText.Inlines?.Clear();
        foreach (var inline in MarkdownRenderer.Render(
            NoteText.Text ?? string.Empty,
            body,
            OnCheckboxToggle,
            OnLinkClicked))
        {
            RenderedText.Inlines?.Add(inline);
        }
    }

    private void OnCheckboxToggle(int lineIndex, bool isChecked)
    {
        var src = NoteText.Text ?? string.Empty;
        var lines = src.Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length) return;

        // Strip trailing \r so checks against fixed offsets work on either line ending.
        var line = lines[lineIndex].TrimEnd('\r');
        // The block prefix is "- [x] " or "- [ ] " — toggle the char at offset 3.
        if (line.Length < 5 || line[0] != '-' || line[1] != ' ' || line[2] != '[' || line[4] != ']')
            return;
        char target = isChecked ? 'x' : ' ';
        if (line[3] == target) return; // no-op (e.g., IsCheckedChanged firing during render)

        var chars = line.ToCharArray();
        chars[3] = target;
        lines[lineIndex] = new string(chars);

        NoteText.Text = string.Join('\n', lines);
        // OnTextChanged kicks the debounced save timer automatically.
    }

    private void OnLinkClicked(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Silently ignore launch failures — invalid URL or no default browser.
        }
    }

    private void OnDragBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_locked) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_locked) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginResizeDrag(WindowEdge.SouthEast, e);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        Restart(_saveTextTimer);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_ready) return;
        Restart(_saveBoundsTimer);
        if (_locked) UpdateTape();
    }

    private void UpdateTape()
    {
        if (_tapeHost == null) return;
        var scale = DesktopScaling;
        var size = PixelSize.FromSize(new Size(Width, Height), scale);
        _tapeHost.Update(new PixelRect(Position, size), scale);
    }

    private static void Restart(DispatcherTimer t)
    {
        t.Stop();
        t.Start();
    }

    private void OnSaveTextTick(object? sender, EventArgs e)
    {
        _saveTextTimer.Stop();
        App.Store.UpdateText(_noteId, NoteText.Text ?? string.Empty);
    }

    private void OnSaveBoundsTick(object? sender, EventArgs e)
    {
        _saveBoundsTimer.Stop();
        App.Store.UpdateBounds(_noteId, Position.X, Position.Y, (int)Width, (int)Height);
    }

    private void FlushPendingWrites()
    {
        if (_saveTextTimer.IsEnabled) OnSaveTextTick(null, EventArgs.Empty);
        if (_saveBoundsTimer.IsEnabled) OnSaveBoundsTick(null, EventArgs.Empty);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && NoteText.IsFocused)
        {
            BodyBorder.Focus();
            e.Handled = true;
            return;
        }

        var cmd = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers != cmd) return;
        if (e.Key == Key.N)
        {
            NoteSpawner.SpawnNew(this);
            e.Handled = true;
        }
        else if (e.Key == Key.D)
        {
            DeleteNote();
            e.Handled = true;
        }
    }

    private void OnNewIconPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        BodyBorder.ContextFlyout?.Hide();
        NoteSpawner.SpawnNew(this);
    }

    private void OnPinIconPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        ApplyPinned(!_pinned);
        App.Store.UpdatePinned(_noteId, _pinned);
    }

    private void OnLockIconPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        ApplyLocked(!_locked);
        App.Store.UpdateLocked(_noteId, _locked);
    }

    private void OnDeleteIconPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (_deleteArmed)
        {
            DisarmDelete();
            BodyBorder.ContextFlyout?.Hide();
            DeleteNote();
            return;
        }
        ArmDelete();
    }

    private async void OnBinIconPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        BodyBorder.ContextFlyout?.Hide();
        await BinWindow.ShowAsync(this);
    }

    private async void OnSettingsIconPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        BodyBorder.ContextFlyout?.Hide();
        await SettingsWindow.ShowAsync(this);
    }

    private void ArmDelete()
    {
        _deleteArmed = true;
        DeleteIcon.Data = DeleteIconArmedGeo;
        DeleteIcon.Stroke = DeleteArmedBrush;
        DeleteIcon.StrokeThickness = 1.8;
        ToolTip.SetTip(DeleteIconBorder, "Click again to delete");

        _deleteRevertTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _deleteRevertTimer.Tick -= OnDeleteRevertTick;
        _deleteRevertTimer.Tick += OnDeleteRevertTick;
        _deleteRevertTimer.Stop();
        _deleteRevertTimer.Start();
    }

    private void OnDeleteRevertTick(object? sender, EventArgs e) => DisarmDelete();

    private void DisarmDelete()
    {
        _deleteRevertTimer?.Stop();
        _deleteArmed = false;
        DeleteIcon.Data = DeleteIconNormalGeo;
        DeleteIcon.Stroke = DeleteNormalBrush;
        DeleteIcon.StrokeThickness = 1.25;
        ToolTip.SetTip(DeleteIconBorder, "Delete");
    }

    private void ApplyPinned(bool pinned)
    {
        _pinned = pinned;
        Topmost = pinned;
        PinIcon.Fill = pinned ? PinFillBrush : Brushes.Transparent;
        ToolTip.SetTip(PinIconBorder, pinned ? "Unpin from top" : "Pin on top");
    }

    private void ApplyLocked(bool locked)
    {
        _locked = locked;
        LockIcon.Data = locked ? LockClosedGeo : LockOpenGeo;
        ToolTip.SetTip(LockIconBorder, locked ? "Unlock" : "Lock in place");
        if (_tapeHost == null) return; // ctor path; OnWindowOpened will Show if locked
        if (locked)
        {
            UpdateTape();
            _tapeHost.Show();
        }
        else
        {
            _tapeHost.Hide();
        }
    }

    private void OnSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_locked) return;
        if (sender is not Ellipse el || el.Tag is not string hex) return;
        ApplyColor(hex);
        App.Store.UpdateColor(_noteId, hex);
        BodyBorder.ContextFlyout?.Hide();
        e.Handled = true;
    }

    private async void OnCustomColorPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_locked) return;
        e.Handled = true;
        BodyBorder.ContextFlyout?.Hide();
        var initial = Color.Parse(_color);
        var picked = await HsvColorPicker.ShowAsync(this, initial,
            live => ApplyColor($"#{live.R:X2}{live.G:X2}{live.B:X2}"));
        if (picked is null) return;
        var hex = $"#{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
        ApplyColor(hex);
        App.Store.UpdateColor(_noteId, hex);
    }

    private void ApplyColor(string hex)
    {
        Color body;
        try { body = Color.Parse(hex); }
        catch { return; }
        _color = hex;
        BodyBorder.Background = new SolidColorBrush(body);
        HeaderBar.Background = new SolidColorBrush(ColorOps.Darken(body, 0.92));
        UpdateSwatchSelection();

        // If we're currently showing the rendered view, re-render so code-span
        // backgrounds pick up the new body colour immediately (rather than
        // waiting for the next blur).
        if (RenderedScroll != null && RenderedScroll.IsVisible)
            RenderText();
    }

    private void UpdateSwatchSelection()
    {
        var selected = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2));
        var normal = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0));
        foreach (var sw in new[] { Sw1, Sw2, Sw3, Sw4, Sw5 })
        {
            bool current = sw.Tag is string h && string.Equals(h, _color, StringComparison.OrdinalIgnoreCase);
            sw.Stroke = current ? selected : normal;
            sw.StrokeThickness = current ? 2.5 : 1;
        }
    }

    private void DeleteNote()
    {
        App.Store.SoftDelete(_noteId);
        _saveTextTimer.Stop();
        _saveBoundsTimer.Stop();
        Close();
    }

}
