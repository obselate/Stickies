using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Stickies;

public partial class MainWindow : Window
{
    private readonly NoteStore _store = new();
    private readonly DispatcherTimer _saveTimer;
    private long _noteId;

    public MainWindow()
    {
        InitializeComponent();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += OnSaveTick;

        var (id, text) = _store.LoadOrCreateDefault();
        _noteId = id;
        NoteText.Text = text;
    }

    private void OnDragBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        _store.UpdateText(_noteId, NoteText.Text ?? string.Empty);
    }
}
