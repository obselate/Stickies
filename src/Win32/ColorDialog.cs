using System;
using System.Runtime.InteropServices;
using Avalonia.Media;

namespace Stickies;

internal static partial class ColorDialog
{
    private const uint CC_RGBINIT = 0x00000001;
    private const uint CC_FULLOPEN = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorStruct
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public uint rgbResult;
        public IntPtr lpCustColors;
        public uint Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "ChooseColorW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChooseColorW(ref ChooseColorStruct lpcc);

    /// <summary>Show the native Windows color picker. Returns the chosen color, or null if cancelled.</summary>
    public static Color? Show(IntPtr ownerHwnd, Color initial)
    {
        var custColors = Marshal.AllocHGlobal(sizeof(uint) * 16);
        try
        {
            for (int i = 0; i < 16; i++)
                Marshal.WriteInt32(custColors, i * 4, 0x00FFFFFF);

            var cc = new ChooseColorStruct
            {
                lStructSize = (uint)Marshal.SizeOf<ChooseColorStruct>(),
                hwndOwner = ownerHwnd,
                rgbResult = ToColorRef(initial),
                lpCustColors = custColors,
                Flags = CC_RGBINIT | CC_FULLOPEN,
            };
            return ChooseColorW(ref cc) ? FromColorRef(cc.rgbResult) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(custColors);
        }
    }

    private static uint ToColorRef(Color c) => (uint)c.R | ((uint)c.G << 8) | ((uint)c.B << 16);

    private static Color FromColorRef(uint r) =>
        Color.FromRgb((byte)(r & 0xFF), (byte)((r >> 8) & 0xFF), (byte)((r >> 16) & 0xFF));
}
