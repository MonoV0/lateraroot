using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lateraroot.Core;

namespace Lateraroot;

/// <summary>One shared palette; settings can preview it in their own resource scope.</summary>
internal static class ThemePalette
{
    internal static void Apply(ColorTheme theme)
    {
        Fill(App.Current.Resources, theme);
        foreach (Window window in App.Current.Windows) TitleBar(window, theme);
    }

    internal static void Fill(ResourceDictionary resources, ColorTheme theme)
    {
        bool dark = theme == ColorTheme.Dark;
        void Put(string key, string light, string night) => resources[key] = Brush(dark ? night : light);
        Put("Ink", "#171717", "#F2F2F4");
        Put("Muted", "#686868", "#B0B0B8");
        Put("Accent", "#171717", "#F2F2F4");
        Put("AccentInk", "#FFFFFF", "#171717");
        Put("Surface", "#FFFFFF", "#202023");
        Put("Canvas", "#F5F5F7", "#151517");
        Put("Field", "#FFFFFF", "#29292E");
        Put("Stroke", "#C8C8CC", "#515159");
        Put("Hover", "#E9E9ED", "#34343B");
        Put("Selection", "#BFD5ED", "#435671");
        Put("Error", "#A32626", "#FF9292");
        if (SystemParameters.HighContrast)
        {
            resources["Ink"] = resources["Muted"] = SystemColors.WindowTextBrush;
            resources["Surface"] = resources["Canvas"] = resources["Field"] = SystemColors.WindowBrush;
            resources["Accent"] = resources["Hover"] = resources["Selection"] = SystemColors.HighlightBrush;
            resources["AccentInk"] = SystemColors.HighlightTextBrush;
            resources["Stroke"] = resources["Error"] = SystemColors.WindowTextBrush;
        }
        // Native WPF menus and text controls also resolve these shared colors.
        resources[SystemColors.WindowBrushKey] = resources["Field"];
        resources[SystemColors.WindowTextBrushKey] = resources["Ink"];
        resources[SystemColors.ControlBrushKey] = resources["Surface"];
        resources[SystemColors.ControlTextBrushKey] = resources["Ink"];
        resources[SystemColors.MenuBrushKey] = resources["Surface"];
        resources[SystemColors.MenuTextBrushKey] = resources["Ink"];
        resources[SystemColors.HighlightBrushKey] = resources["Accent"];
        resources[SystemColors.HighlightTextBrushKey] = resources["AccentInk"];
        resources[SystemColors.GrayTextBrushKey] = resources["Muted"];
    }

    internal static void TitleBar(Window window, ColorTheme theme)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = theme == ColorTheme.Dark && !SystemParameters.HighContrast ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze(); return brush;
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
