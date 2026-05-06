using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Stickies;

public partial class MainWindow : Window
{
    private readonly long _noteId;
    private readonly DispatcherTimer _saveTextTimer;
    private readonly DispatcherTimer _saveBoundsTimer;
    private bool _ready;
    private bool _pinned;
    private string _color = "#FFF59E";

    public long NoteId => _noteId;

    public MainWindow() : this(App.Store.Create(null, null, 280, 280)) { }

    public MainWindow(NoteStore.NoteRow row)
    {
        _noteId = row.Id;
        InitializeComponent();

        Width = row.Width;
        Height = row.Height;

        if (row.X is int x && row.Y is int y && IsOnAnyScreen(x, y, row.Width, row.Height))
            Position = new PixelPoint(x, y);

        NoteText.Text = row.Text;
        ApplyPinned(row.Pinned);
        ApplyColor(row.Color);

        _saveTextTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTextTimer.Tick += OnSaveTextTick;

        _saveBoundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveBoundsTimer.Tick += OnSaveBoundsTick;

        PositionChanged += (_, _) => { if (_ready) Restart(_saveBoundsTimer); };

        Opened += (_, _) =>
        {
            _ready = true;
            OnSaveBoundsTick(null, EventArgs.Empty);
        };
        Closing += (_, _) => FlushPendingWrites();
        KeyDown += OnKeyDown;
    }

    private bool IsOnAnyScreen(int x, int y, int w, int h)
    {
        var screens = Screens?.All;
        if (screens is null || screens.Count == 0) return true;
        foreach (var s in screens)
        {
            var b = s.Bounds;
            if (x + w > b.X && x < b.X + b.Width && y + h > b.Y && y < b.Y + b.Height)
                return true;
        }
        return false;
    }

    private void OnDragBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
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
        if (_ready) Restart(_saveBoundsTimer);
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
        if (e.KeyModifiers != KeyModifiers.Control) return;
        if (e.Key == Key.N)
        {
            SpawnNew(this);
            e.Handled = true;
        }
        else if (e.Key == Key.D)
        {
            DeleteNote();
            e.Handled = true;
        }
    }

    private void OnNewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SpawnNew(this);

    private void OnDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => DeleteNote();

    private void OnPinClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ApplyPinned(!_pinned);
        App.Store.UpdatePinned(_noteId, _pinned);
    }

    private void ApplyPinned(bool pinned)
    {
        _pinned = pinned;
        Topmost = pinned;
        PinMenuItem.Header = pinned ? "Unpin from top" : "Pin on top";
    }

    private void OnSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Ellipse el || el.Tag is not string hex) return;
        ApplyColor(hex);
        App.Store.UpdateColor(_noteId, hex);
        NoteMenu.Close();
        e.Handled = true;
    }

    private void OnCustomColorPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        var handle = TryGetPlatformHandle();
        var owner = handle?.Handle ?? IntPtr.Zero;
        var current = Color.Parse(_color);
        var picked = ColorDialog.Show(owner, current);
        if (picked is null) return;
        var hex = $"#{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
        ApplyColor(hex);
        App.Store.UpdateColor(_noteId, hex);
        NoteMenu.Close();
    }

    private void ApplyColor(string hex)
    {
        Color body;
        try { body = Color.Parse(hex); }
        catch { return; }
        _color = hex;
        BodyBorder.Background = new SolidColorBrush(body);
        HeaderBar.Background = new SolidColorBrush(Darker(body, 0.92));
        UpdateSwatchSelection();
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

    private static Color Darker(Color c, double f) => Color.FromRgb(
        (byte)Math.Clamp(c.R * f, 0, 255),
        (byte)Math.Clamp(c.G * f, 0, 255),
        (byte)Math.Clamp(c.B * f, 0, 255));

    private void DeleteNote()
    {
        App.Store.SoftDelete(_noteId);
        _saveTextTimer.Stop();
        _saveBoundsTimer.Stop();
        Close();
    }

    public static void SpawnNew(MainWindow? near)
    {
        int? x = null, y = null;
        if (near is not null)
        {
            x = near.Position.X + 24;
            y = near.Position.Y + 24;
        }
        var row = App.Store.Create(x, y, 280, 280);
        var w = new MainWindow(row);
        w.Show();
    }
}
