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
using Stickies.Models;

namespace Stickies;

public partial class MainWindow : Window
{
    private readonly long _noteId;
    private readonly DispatcherTimer _saveTextTimer;
    private readonly DispatcherTimer _saveBoundsTimer;
    private bool _ready;
    private bool _pinned;
    private bool _isAnimating;
    private string _color = "#FFF59E";

    public long NoteId => _noteId;

    public MainWindow() : this(App.Store.Create(null, null, 280, 280)) { }

    public MainWindow(Note row)
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
        // Null-source path: no peel. If any existing note is visible, place the
        // new note at a non-overlapping spot near it. With no reference, fall
        // through to the DB-default position (first-run; nothing to overlap).
        if (near is null)
        {
            const int W = 280, H = 280;
            var reference = FindReferenceWindow();
            int? nx = null, ny = null;
            if (reference is not null)
            {
                var pos = FindAvailableSpace(null, reference.Position, W, H);
                nx = pos.X;
                ny = pos.Y;
            }
            var rowN = App.Store.Create(nx, ny, W, H);
            var wN = new MainWindow(rowN);
            FadeIn(wN);
            wN.Show();
            return;
        }

        // Source-driven path: peel from `near`, then spawn B at near.Position
        // (no cascade offset — see spec decision #2).
        if (near._isAnimating) return;
        near._isAnimating = true;

        int srcW = (int)near.Width;
        int srcH = (int)near.Height;
        Color bodyColor;
        try { bodyColor = Color.Parse(near._color); }
        catch { bodyColor = Color.FromRgb(0xFF, 0xF5, 0x9E); }

        Bitmap snapshot;
        try
        {
            snapshot = CaptureCornerSnapshot(near);
        }
        catch
        {
            // Snapshot failed (window not yet measured, etc.) — fall back to instant
            // replacement using the same relocate-source/new-takes-original-spot model.
            near._isAnimating = false;
            SpawnReplacement(near, srcW, srcH);
            return;
        }

        var overlay = new PeelOverlay { Snapshot = snapshot, BodyColor = bodyColor };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 2);
        overlay.HorizontalAlignment = HorizontalAlignment.Left;
        overlay.VerticalAlignment = VerticalAlignment.Bottom;
        overlay.Width = PeelOverlay.CornerSize;
        overlay.Height = PeelOverlay.CornerSize;
        overlay.IsHitTestVisible = false;

        overlay.Completed += () =>
        {
            near.BodyGrid.Children.Remove(overlay);
            near._isAnimating = false;
            SpawnReplacement(near, srcW, srcH);
        };

        near.BodyGrid.Children.Add(overlay);
        overlay.Start();
    }

    // After the peel: relocate the source ("old") sticky to nearby available
    // space and create the new sticky at the source's original position. The
    // new sticky fades in; the old slides via AnimateMove.
    private static void SpawnReplacement(MainWindow source, int width, int height)
    {
        // Normally the new note takes the source's exact position. If the source
        // is itself stacked under a twin (legacy notes from before non-overlap
        // placement existed), find a free spot for the new note rather than
        // landing it on top of the twin.
        var preferred = source.Position;
        var newNotePos = OverlapsAnyOther(source, preferred, width, height)
            ? FindAvailableSpace(source, preferred, width, height)
            : preferred;

        var oldNewPos = FindAvailableSpace(source, newNotePos, width, height);

        AnimateMove(source, oldNewPos);

        var row = App.Store.Create(newNotePos.X, newNotePos.Y, width, height);
        var fresh = new MainWindow(row);
        fresh.Opacity = 0;
        fresh.Show();
        FadeIn(fresh, durationMs: 200);
    }

    private static bool OverlapsAnyOther(MainWindow source, PixelPoint pos, int width, int height)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;
        // pos is physical (PixelPoint); convert width/height (DIPs) using source's scaling.
        double srcScale = source.RenderScaling > 0 ? source.RenderScaling : 1.0;
        var rect = new PixelRect(pos, new PixelSize((int)(width * srcScale), (int)(height * srcScale)));
        foreach (var w in desktop.Windows)
        {
            if (w is MainWindow mw && mw != source && mw.IsVisible)
            {
                if (rect.Intersects(PhysicalRect(mw))) return true;
            }
        }
        return false;
    }

    private static PixelRect PhysicalRect(MainWindow mw)
    {
        double s = mw.RenderScaling > 0 ? mw.RenderScaling : 1.0;
        return new PixelRect(mw.Position, new PixelSize((int)(mw.Width * s), (int)(mw.Height * s)));
    }

    // First visible MainWindow (any). Used as a placement anchor when a new
    // note is being spawned without a peel source.
    private static MainWindow? FindReferenceWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        foreach (var w in desktop.Windows)
            if (w is MainWindow mw && mw.IsVisible) return mw;
        return null;
    }

    // Spirals out from `origin` in cardinal-then-diagonal order at increasing
    // distances. Skips positions that overlap any open note (other than `source`,
    // which is moving away) or fall within margin of a screen edge. The origin
    // itself is treated as occupied. Best-effort fallback when crowded: small
    // offset clamped to screen.
    private static PixelPoint FindAvailableSpace(
        MainWindow? source,
        PixelPoint origin,
        int width,
        int height)
    {
        // Logical (DIP) constants converted to physical via screenAnchor.RenderScaling.
        const int marginDip = 40;
        const int gapDip = 12;

        MainWindow? screenAnchor = source;
        var others = new List<PixelRect>();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows)
            {
                if (w is MainWindow mw && mw != source && mw.IsVisible)
                {
                    others.Add(PhysicalRect(mw));
                    screenAnchor ??= mw;
                }
            }
        }

        double scale = screenAnchor?.RenderScaling > 0 ? screenAnchor.RenderScaling : 1.0;
        int wPx = (int)(width * scale);
        int hPx = (int)(height * scale);
        int marginPx = (int)(marginDip * scale);
        int gapPx = (int)(gapDip * scale);

        others.Add(new PixelRect(origin, new PixelSize(wPx, hPx)));

        var screen = screenAnchor?.Screens?.ScreenFromWindow(screenAnchor);
        var screenArea = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        var directions = new (int dx, int dy)[]
        {
            (1, 0), (0, 1), (-1, 0), (0, -1),
            (1, 1), (-1, 1), (1, -1), (-1, -1),
        };

        for (int dist = 1; dist <= 5; dist++)
        {
            foreach (var (dx, dy) in directions)
            {
                int nx = origin.X + dx * (wPx + gapPx) * dist;
                int ny = origin.Y + dy * (hPx + gapPx) * dist;

                if (nx < screenArea.X + marginPx) continue;
                if (ny < screenArea.Y + marginPx) continue;
                if (nx + wPx > screenArea.X + screenArea.Width - marginPx) continue;
                if (ny + hPx > screenArea.Y + screenArea.Height - marginPx) continue;

                var candidate = new PixelRect(nx, ny, wPx, hPx);
                bool overlap = false;
                foreach (var other in others)
                {
                    if (candidate.Intersects(other)) { overlap = true; break; }
                }
                if (overlap) continue;

                return new PixelPoint(nx, ny);
            }
        }

        int fxMin = screenArea.X + marginPx;
        int fxMax = screenArea.X + screenArea.Width - wPx - marginPx;
        int fyMin = screenArea.Y + marginPx;
        int fyMax = screenArea.Y + screenArea.Height - hPx - marginPx;
        int fx = Math.Clamp(origin.X + gapPx, Math.Min(fxMin, fxMax), Math.Max(fxMin, fxMax));
        int fy = Math.Clamp(origin.Y + gapPx, Math.Min(fyMin, fyMax), Math.Max(fyMin, fyMax));
        return new PixelPoint(fx, fy);
    }

    private static void AnimateMove(MainWindow w, PixelPoint to, int durationMs = 200)
    {
        var from = w.Position;
        if (from == to) return;
        var sw = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Clamp(sw.Elapsed.TotalMilliseconds / durationMs, 0.0, 1.0);
            double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            int x = (int)(from.X + (to.X - from.X) * eased);
            int y = (int)(from.Y + (to.Y - from.Y) * eased);
            w.Position = new PixelPoint(x, y);
            if (t >= 1.0)
            {
                w.Position = to;
                timer.Stop();
                sw.Stop();
            }
        };
        timer.Start();
    }

    private static void FadeIn(MainWindow w, int durationMs = 50)
    {
        w.Opacity = 0;
        var sw = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Clamp(sw.Elapsed.TotalMilliseconds / durationMs, 0.0, 1.0);
            w.Opacity = t;
            if (t >= 1.0)
            {
                timer.Stop();
                sw.Stop();
            }
        };
        timer.Start();
    }

    internal static Bitmap CaptureCornerSnapshot(MainWindow source)
    {
        // Capture the entire BodyBorder, then crop to BL 110x110.
        var body = source.BodyBorder;
        var bw = (int)Math.Ceiling(body.Bounds.Width);
        var bh = (int)Math.Ceiling(body.Bounds.Height);
        if (bw <= 0 || bh <= 0)
            return new RenderTargetBitmap(new PixelSize(PeelOverlay.CornerSize, PeelOverlay.CornerSize));

        var full = new RenderTargetBitmap(new PixelSize(bw, bh));
        full.Render(body);

        // Crop to the bottom-left CornerSize x CornerSize.
        int cs = PeelOverlay.CornerSize;
        int cropW = Math.Min(cs, bw);
        int cropH = Math.Min(cs, bh);
        int srcX = 0;                  // BL x = 0
        int srcY = bh - cropH;         // BL y = bottom of body

        var cropped = new RenderTargetBitmap(new PixelSize(cs, cs));
        using (var ctx = cropped.CreateDrawingContext())
        {
            // Fill with transparent (RenderTargetBitmap default is transparent).
            // Draw the cropped region of `full` into the BL of `cropped`.
            int dstY = cs - cropH;     // anchor crop at BL of overlay region
            ctx.DrawImage(
                full,
                new Rect(srcX, srcY, cropW, cropH),
                new Rect(0, dstY, cropW, cropH));
        }
        full.Dispose();
        return cropped;
    }
}
