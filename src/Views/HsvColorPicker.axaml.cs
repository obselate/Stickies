using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Stickies.Services;

namespace Stickies.Views;

public partial class HsvColorPicker : Window
{
    private const int Size = 160;

    private double _h, _s, _v;
    private Color _original;
    private Color _current;
    private Action<Color>? _live;
    private bool _applied;
    private WriteableBitmap? _svBitmap;
    private bool _suppressHexUpdate;

    public HsvColorPicker()
    {
        InitializeComponent();
        PaintHueStrip();
        Opened += (_, _) => HexBox.Focus();
        KeyDown += OnWindowKeyDown;
    }

    public static async Task<Color?> ShowAsync(Window owner, Color initial, Action<Color> live)
    {
        var picker = new HsvColorPicker();
        picker._original = initial;
        picker._live = live;
        picker.SetCurrent(initial);
        await picker.ShowDialog(owner);
        return picker._applied ? picker._current : null;
    }

    private void SetCurrent(Color c)
    {
        _current = c;
        var hsv = ColorOps.RgbToHsv(c);
        _h = hsv.H;
        _s = hsv.S;
        _v = hsv.V;
        PaintSvSquare();
        UpdateCursors();
        UpdatePreview();
        UpdateHexBox();
    }

    private void PaintHueStrip()
    {
        var wb = new WriteableBitmap(new PixelSize(20, Size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = wb.Lock())
        {
            unsafe
            {
                byte* p = (byte*)fb.Address;
                for (int y = 0; y < Size; y++)
                {
                    double h = (double)y / (Size - 1) * 360.0;
                    var c = ColorOps.HsvToRgb(h, 1.0, 1.0);
                    byte* row = p + y * fb.RowBytes;
                    for (int x = 0; x < 20; x++)
                    {
                        row[x * 4 + 0] = c.B;
                        row[x * 4 + 1] = c.G;
                        row[x * 4 + 2] = c.R;
                        row[x * 4 + 3] = 0xFF;
                    }
                }
            }
        }
        HueImage.Source = wb;
    }

    private void PaintSvSquare()
    {
        _svBitmap ??= new WriteableBitmap(new PixelSize(Size, Size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = _svBitmap.Lock())
        {
            unsafe
            {
                byte* p = (byte*)fb.Address;
                for (int y = 0; y < Size; y++)
                {
                    double v = 1.0 - (double)y / (Size - 1);
                    byte* row = p + y * fb.RowBytes;
                    for (int x = 0; x < Size; x++)
                    {
                        double s = (double)x / (Size - 1);
                        var c = ColorOps.HsvToRgb(_h, s, v);
                        row[x * 4 + 0] = c.B;
                        row[x * 4 + 1] = c.G;
                        row[x * 4 + 2] = c.R;
                        row[x * 4 + 3] = 0xFF;
                    }
                }
            }
        }
        SvImage.Source = _svBitmap;
        SvImage.InvalidateVisual();
    }

    private void UpdateCursors()
    {
        Canvas.SetLeft(SvCursor, _s * (Size - 1) - 6);
        Canvas.SetTop(SvCursor, (1 - _v) * (Size - 1) - 6);
        Canvas.SetTop(HueCursor, _h / 360.0 * (Size - 1) - 1);
    }

    private void UpdatePreview()
    {
        _current = ColorOps.HsvToRgb(_h, _s, _v);
        PreviewBorder.Background = new SolidColorBrush(_current);
        _live?.Invoke(_current);
    }

    private void UpdateHexBox()
    {
        _suppressHexUpdate = true;
        HexBox.Text = $"#{_current.R:X2}{_current.G:X2}{_current.B:X2}";
        _suppressHexUpdate = false;
    }

    private void OnSvPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(SvImage);
        UpdateSvFromPointer(e.GetPosition(SvImage));
    }

    private void OnSvMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured == SvImage)
            UpdateSvFromPointer(e.GetPosition(SvImage));
    }

    private void OnSvReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
    }

    private void UpdateSvFromPointer(Point pt)
    {
        _s = Math.Clamp(pt.X / (Size - 1), 0, 1);
        _v = Math.Clamp(1 - pt.Y / (Size - 1), 0, 1);
        UpdateCursors();
        UpdatePreview();
        UpdateHexBox();
    }

    private void OnHuePressed(object? sender, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(HueImage);
        UpdateHueFromPointer(e.GetPosition(HueImage));
    }

    private void OnHueMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured == HueImage)
            UpdateHueFromPointer(e.GetPosition(HueImage));
    }

    private void OnHueReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
    }

    private void UpdateHueFromPointer(Point pt)
    {
        _h = Math.Clamp(pt.Y / (Size - 1), 0, 1) * 360.0;
        PaintSvSquare();
        UpdateCursors();
        UpdatePreview();
        UpdateHexBox();
    }

    private void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
    }

    private void OnHexLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        if (_suppressHexUpdate) return;
        var text = HexBox.Text?.Trim() ?? "";
        if (TryParseHex(text, out var parsed))
        {
            SetCurrent(parsed);
        }
        else
        {
            FlashHexError();
        }
    }

    private static bool TryParseHex(string s, out Color c)
    {
        c = default;
        if (string.IsNullOrEmpty(s)) return false;
        if (s[0] == '#') s = s.Substring(1);
        if (s.Length == 3)
        {
            s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        }
        if (s.Length != 6) return false;
        try
        {
            byte r = Convert.ToByte(s.Substring(0, 2), 16);
            byte g = Convert.ToByte(s.Substring(2, 2), 16);
            byte b = Convert.ToByte(s.Substring(4, 2), 16);
            c = Color.FromRgb(r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void FlashHexError()
    {
        var original = HexBox.BorderBrush;
        HexBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33));
        DispatcherTimer.RunOnce(() => HexBox.BorderBrush = original, TimeSpan.FromMilliseconds(600));
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RevertAndClose();
            e.Handled = true;
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RevertAndClose();

    private void OnApplyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _applied = true;
        Close();
    }

    private void RevertAndClose()
    {
        _live?.Invoke(_original);
        Close();
    }
}
