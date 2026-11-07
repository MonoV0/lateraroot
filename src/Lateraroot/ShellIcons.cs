using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lateraroot;

internal static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource> cache = new(StringComparer.OrdinalIgnoreCase);
    public static ImageSource Get(string path, bool directory)
    {
        var extension = System.IO.Path.GetExtension(path);
        var key = directory ? "<folder>" : extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ? path : extension;
        if (cache.TryGetValue(key, out var result)) return result;
        var info = new SHFILEINFO();
        // Use the extension for normal files: avoids touching cloud-only contents and network providers.
        var flags = 0x100u | (key == path ? 0u : 0x10u);
        SHGetFileInfo(key == path ? path : directory ? "folder" : "file" + extension, directory ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        try
        {
            result = info.hIcon != IntPtr.Zero ? Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()) : DrawingFallback();
            result.Freeze();
            if (cache.Count > 512) cache.Clear();
            cache[key] = result;
            return result;
        }
        finally { if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon); }
    }
    private static ImageSource DrawingFallback() => new DrawingImage(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(167, 184, 255)), null, new RectangleGeometry(new Rect(4, 2, 24, 28), 3, 3)));
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
