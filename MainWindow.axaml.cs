using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Stickies;

public partial class MainWindow : Window
{
    private readonly long _noteId;
    private readonly DispatcherTimer _saveTextTimer;
    private readonly DispatcherTimer _saveBoundsTimer;
    private bool _ready;

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
            App.Store.SoftDelete(_noteId);
            _saveTextTimer.Stop();
            _saveBoundsTimer.Stop();
            Close();
            e.Handled = true;
        }
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
