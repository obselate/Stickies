using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia;
using SkiaSharp;
using Stickies.Platform;

namespace Stickies.Win32;

// Pattern A platform-specific: only constructed by Stickies.Platform.TapeHost.Create()
// when OperatingSystem.IsWindows().
//
// Bypasses Avalonia's render path entirely. The tape is a Win32 layered window
// (WS_EX_LAYERED) painted via UpdateLayeredWindow with a 32bpp premultiplied-ARGB
// bitmap. DWM composites it with per-pixel alpha, so we get true transparency
// without ANGLE / GPU rendering — the rest of the app stays on
// Win32RenderingMode.Software.
//
// Owner relationship via SetWindowLongPtr(GWLP_HWNDPARENT) so the tape z-orders
// with its note (minimize, alt-tab, hide, close all follow the owner).
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WinTapeHost : ITapeHost
{
    private const string ClassName = "Stickies.Tape";
    private static int s_classRegistered;

    private const double RotationDegrees = 1.5;
    private const int TapeHeight = 22;
    private const int TapeInset = 24;

    // Pad needed (in DIPs) to keep the rotated tape inside the bitmap bounds.
    // Rotation extends the vertical AABB by sin(angle) * width — scales with
    // tape width.
    private static int RequiredPad(double tapeWidthDips)
    {
        var sin = Math.Sin(RotationDegrees * Math.PI / 180);
        return (int)Math.Ceiling(tapeWidthDips * sin / 2) + 2;
    }

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    private readonly IntPtr _ownerHwnd;
    private IntPtr _hwnd;
    private int _bmpWidth;
    private int _bmpHeight;
    private bool _shown;
    private bool _disposed;

    public WinTapeHost(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        EnsureClassRegistered();

        // Pass _ownerHwnd as hWndParent on a WS_POPUP top-level window: that's
        // the Win32 idiom for an owned window. Owned windows auto-z-order ABOVE
        // their owner (clicking the owner brings the tape forward with it,
        // alt-tab and minimize follow). Setting GWLP_HWNDPARENT after the fact
        // works for ownership but doesn't fix z-order — has to happen at create.
        _hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
            ClassName, "Stickies.Tape",
            WS_POPUP,
            0, 0, 1, 1,
            _ownerHwnd, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception();
    }

    public void Show()
    {
        if (_disposed || _shown) return;
        if (_bmpWidth == 0) return; // can't show before first Update sized + painted it
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        _shown = true;
    }

    public void Hide()
    {
        if (_disposed || !_shown) return;
        ShowWindow(_hwnd, SW_HIDE);
        _shown = false;
    }

    public void Update(PixelRect noteBounds, double scale)
    {
        if (_disposed) return;
        // Tape geometry constants are DIPs; convert to physical pixels for the
        // bitmap render and Win32 SetWindowPos / UpdateLayeredWindow (which all
        // operate in device pixels). Without this the tape renders at half size
        // on a 200% / 4K monitor. Pad scales with tape width — rotation extends
        // the vertical AABB linearly with width.
        double tapeWidthDips = noteBounds.Width / scale - 2 * TapeInset;
        int pad = RequiredPad(tapeWidthDips);
        int physInset = (int)Math.Round(TapeInset * scale);
        int physPad = (int)Math.Round(pad * scale);
        int physTapeHeight = (int)Math.Round(TapeHeight * scale);
        int w = noteBounds.Width - 2 * physInset + 2 * physPad;
        int h = physTapeHeight + 2 * physPad;
        int x = noteBounds.X + physInset - physPad;
        int y = noteBounds.Y - physTapeHeight / 2 - physPad;
        if (w <= 0 || h <= 0) return;

        if (w != _bmpWidth || h != _bmpHeight)
        {
            // Size changed (or first call) — render and atomically position+paint.
            PaintLayered(w, h, x, y, physPad);
            _bmpWidth = w;
            _bmpHeight = h;
        }
        else
        {
            SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, SWP_NOACTIVATE | SWP_NOZORDER);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private void PaintLayered(int w, int h, int x, int y, int physPad)
    {
        // 1. Render the tape into a premultiplied ARGB buffer with SkiaSharp.
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            DrawTape(canvas, w, h, physPad);
        }

        // 2. Allocate a top-down 32bpp DIB section that UpdateLayeredWindow can consume.
        var bmi = new BITMAPINFO
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFO>(),
            biWidth = w,
            biHeight = -h,           // negative = top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,       // BI_RGB
        };
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr ppvBits;
        IntPtr hBmp = CreateDIBSection(screenDc, ref bmi, 0, out ppvBits, IntPtr.Zero, 0);
        IntPtr oldBmp = SelectObject(memDc, hBmp);

        // 3. Copy SkiaSharp's tightly-packed BGRA buffer into the DIB.
        IntPtr skPixels = bitmap.GetPixels();
        long byteCount = (long)w * h * 4;
        Buffer.MemoryCopy((void*)skPixels, (void*)ppvBits, byteCount, byteCount);

        // 4. UpdateLayeredWindow positions, sizes, AND paints in one atomic call.
        var ptDst = new POINT { X = x, Y = y };
        var size = new SIZE { cx = w, cy = h };
        var ptSrc = new POINT { X = 0, Y = 0 };
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA,
        };
        UpdateLayeredWindow(_hwnd, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);

        SelectObject(memDc, oldBmp);
        DeleteObject(hBmp);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    private static void DrawTape(SKCanvas c, int w, int h, int physPad)
    {
        var tapeRect = SKRect.Create(physPad, physPad, w - 2 * physPad, h - 2 * physPad);

        // Rotate around the tape's center for the slight-tilt physical-tape feel.
        float cx = w / 2f;
        float cy = h / 2f;
        c.Save();
        c.Translate(cx, cy);
        c.RotateDegrees(-(float)RotationDegrees);
        c.Translate(-cx, -cy);

        // Tape body: vertical 3-stop gradient.
        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, tapeRect.Top),
            new SKPoint(0, tapeRect.Bottom),
            new[]
            {
                new SKColor(0xFA, 0xFA, 0xFA, 0x80),
                new SKColor(0xEC, 0xEC, 0xEC, 0x80),
                new SKColor(0xFA, 0xFA, 0xFA, 0x80),
            },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp);
        using var tapePaint = new SKPaint { IsAntialias = true, Shader = gradient };
        c.DrawRect(tapeRect, tapePaint);

        c.Restore();
    }

    private static void EnsureClassRegistered()
    {
        if (Interlocked.CompareExchange(ref s_classRegistered, 1, 0) != 0) return;

        // Win32 RegisterClassEx copies the class name internally, so freeing
        // immediately after the call is safe.
        IntPtr classNamePtr = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProcThunk,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = GetModuleHandleW(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = IntPtr.Zero,
                lpszClassName = classNamePtr,
                hIconSm = IntPtr.Zero,
            };

            if (RegisterClassExW(ref wc) == 0)
            {
                int err = Marshal.GetLastPInvokeError();
                const int ERROR_CLASS_ALREADY_EXISTS = 1410;
                if (err != ERROR_CLASS_ALREADY_EXISTS)
                    throw new System.ComponentModel.Win32Exception(err);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePtr);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr WndProcThunk(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProcW(hwnd, msg, wParam, lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        // Single-color BI_RGB DIB has no color table; this layout matches BITMAPINFOHEADER.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);
}
