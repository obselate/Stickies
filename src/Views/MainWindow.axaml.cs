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

    private static readonly Geometry DeleteIconNormalGeo = Geometry.Parse(
        "M14 2H10C10 0.897 9.103 0 8 0C6.897 0 6 0.897 6 2H2C1.724 2 1.5 2.224 1.5 2.5C1.5 2.776 1.724 3 2 3H2.54L3.349 12.708C3.456 13.994 4.55 15 5.84 15H10.159C11.449 15 12.543 13.993 12.65 12.708L13.459 3H13.999C14.275 3 14.499 2.776 14.499 2.5C14.499 2.224 14.275 2 13.999 2H14ZM8 1C8.551 1 9 1.449 9 2H7C7 1.449 7.449 1 8 1ZM11.655 12.625C11.591 13.396 10.934 14 10.16 14H5.841C5.067 14 4.41 13.396 4.346 12.625L3.544 3H12.458L11.656 12.625H11.655ZM7 5.5V11.5C7 11.776 6.776 12 6.5 12C6.224 12 6 11.776 6 11.5V5.5C6 5.224 6.224 5 6.5 5C6.776 5 7 5.224 7 5.5ZM10 5.5V11.5C10 11.776 9.776 12 9.5 12C9.224 12 9 11.776 9 11.5V5.5C9 5.224 9.224 5 9.5 5C9.776 5 10 5.224 10 5.5Z");
    private static readonly Geometry DeleteIconArmedGeo = Geometry.Parse("M2,2 L14,14 M14,2 L2,14");
    private static readonly IBrush IconBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly IBrush DeleteArmedBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x32, 0x2D));

    private static readonly Geometry PinUnpinnedGeo = Geometry.Parse(
        "M4.146.146A.5.5 0 0 1 4.5 0h7a.5.5 0 0 1 .5.5c0 .68-.342 1.174-.646 1.479-.126.125-.25.224-.354.298v4.431l.078.048c.203.127.476.314.751.555C12.36 7.775 13 8.527 13 9.5a.5.5 0 0 1-.5.5h-4v4.5c0 .276-.224 1.5-.5 1.5s-.5-1.224-.5-1.5V10h-4a.5.5 0 0 1-.5-.5c0-.973.64-1.725 1.17-2.189A6 6 0 0 1 5 6.708V2.277a3 3 0 0 1-.354-.298C4.342 1.674 4 1.179 4 .5a.5.5 0 0 1 .146-.354m1.58 1.408-.002-.001zm-.002-.001.002.001A.5.5 0 0 1 6 2v5a.5.5 0 0 1-.276.447h-.002l-.012.007-.054.03a5 5 0 0 0-.827.58c-.318.278-.585.596-.725.936h7.792c-.14-.34-.407-.658-.725-.936a5 5 0 0 0-.881-.61l-.012-.006h-.002A.5.5 0 0 1 10 7V2a.5.5 0 0 1 .295-.458 1.8 1.8 0 0 0 .351-.271c.08-.08.155-.17.214-.271H5.14q.091.15.214.271a1.8 1.8 0 0 0 .37.282");
    private static readonly Geometry PinPinnedGeo = Geometry.Parse(
        "M16.2425 2.93189L21.0682 7.75765C22.3955 9.08491 22.0324 11.3224 20.3535 12.1619L15.4826 14.5973C15.3073 14.685 15.1732 14.8379 15.1092 15.0232L13.6699 19.1895C13.3684 20.0622 12.2574 20.3181 11.6045 19.6653L8.50002 16.5607L4.06074 21.0001H3L3.00008 19.9394L7.43936 15.5001L4.33487 12.3956C3.682 11.7427 3.93791 10.6317 4.81061 10.3302L8.97688 8.89096C9.16223 8.82694 9.31512 8.69287 9.40281 8.51748L11.8382 3.6466C12.6777 1.96772 14.9152 1.60462 16.2425 2.93189ZM20.0076 8.81831L15.1818 3.99255C14.5785 3.38924 13.5614 3.55429 13.1799 4.31742L10.7445 9.18829C10.4814 9.71446 10.0227 10.1167 9.46666 10.3087L5.67812 11.6175L12.3826 18.322L13.6914 14.5335C13.8835 13.9774 14.2857 13.5188 14.8118 13.2557L19.6827 10.8202C20.4458 10.4387 20.6109 9.42161 20.0076 8.81831Z");

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
        DeleteIcon.Fill = Brushes.Transparent;
        DeleteIcon.Stroke = DeleteArmedBrush;
        DeleteIcon.StrokeThickness = 2;
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
        DeleteIcon.Fill = IconBrush;
        DeleteIcon.Stroke = null;
        DeleteIcon.StrokeThickness = 0;
        ToolTip.SetTip(DeleteIconBorder, "Delete");
    }

    private void ApplyPinned(bool pinned)
    {
        _pinned = pinned;
        Topmost = pinned;
        PinIcon.Data = pinned ? PinPinnedGeo : PinUnpinnedGeo;
        ToolTip.SetTip(PinIconBorder, pinned ? "Unpin from top" : "Pin on top");
    }

    private void ApplyLocked(bool locked)
    {
        _locked = locked;
        LockIcon.Opacity = locked ? 1.0 : 0.35;
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
