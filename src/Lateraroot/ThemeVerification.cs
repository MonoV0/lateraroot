using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lateraroot.Core;

namespace Lateraroot;

public partial class MainWindow
{
    private async Task VerifyThemeAsync(Action<bool, string> check, Action<Window, string> capture)
    {
        bool wasDesktop = desktopMode;
        UpdateDesktopContext(false);
        var original = JsonStore.Clone(Host.Settings);
        string[] colors = original.Stacks.Select(s => s.Color).ToArray();
        var preview = new SettingsWindow(); preview.Show(); await ThemeSettled();
        preview.ThemeBox.SelectedIndex = 1; await ThemeSettled();
        check(Host.Settings.Theme == original.Theme && Dark(preview.Background), "Dark preview changes settings without applying or saving the application theme");
        capture(preview, "settings-dark.png");
        ((Grid)preview.Content).Children.OfType<TabControl>().Single().SelectedIndex = 1;
        await ThemeSettled(); preview.ThemeBox.IsDropDownOpen = true; await ThemeSettled();
        check(preview.ThemeBox.IsDropDownOpen && Dark(preview.ThemeBox.Background), "Theme selector opens a themed drop-down using the real keyboard-accessible control");
        preview.ThemeBox.IsDropDownOpen = false;
        capture(preview, "settings-dark-layout.png"); preview.Close();
        check(Host.Settings.Theme == original.Theme, "Closing an unsaved theme preview keeps the application preference");

        var saved = new SettingsWindow(); saved.Show(); saved.ThemeBox.SelectedIndex = 1;
        saved.SaveDraft(); await ThemeSettled(); ApplyPresentation();
        check(Host.Settings.Theme == ColorTheme.Dark && JsonStore.Read(Host.SettingsPath, () => new AppSettings()).Theme == ColorTheme.Dark, "Saving dark mode persists the theme used on next startup");
        check(Dark(Frame.Background) && !Dark(Foreground), "Saved dark mode immediately updates compact/inspector surfaces and foreground");
        capture(this, "compact-dark.png");
        check(colors.SequenceEqual(Host.Settings.Stacks.Select(s => s.Color)), "Changing theme preserves every user genre color");
        foreach (string background in new[] { "Surface", "Canvas", "Field" })
        {
            double bg = WidgetVisuals.Luminance(((SolidColorBrush)FindResource(background)).Color);
            foreach (string foreground in new[] { "Ink", "Muted" })
            {
                double fg = WidgetVisuals.Luminance(((SolidColorBrush)FindResource(foreground)).Color);
                check((Math.Max(bg, fg) + .05) / (Math.Min(bg, fg) + .05) >= 4.5, "Dark text contrast: " + foreground + " on " + background);
            }
        }
        var menu = actions.CreateMenu(); menu.PlacementTarget = RailSettings; menu.IsOpen = true; await ThemeSettled();
        check(Dark(menu.Background) && !Dark(menu.Foreground), "Shared widget context menu uses the dark palette");
        menu.IsOpen = false;
        var update = new UpdateWindow(new UpdateSession(new UiUpdateProvider()), null); update.Show(); await ThemeSettled();
        check(Dark(update.Background) && !Dark(update.Foreground), "Update dialog shares the saved dark palette");
        capture(update, "update-dark.png"); update.Close();
        saved.ThemeBox.SelectedIndex = 0; saved.SaveDraft(); await ThemeSettled(); ApplyPresentation();
        check(!Dark(Frame.Background) && Dark(Foreground) && Host.Settings.Theme == ColorTheme.Light, "Switching back to light mode updates existing controls without restarting");
        saved.Close(); Host.Settings = original; JsonStore.Write(Host.SettingsPath, original); ThemePalette.Apply(original.Theme); UpdateDesktopContext(wasDesktop); ApplyPresentation();

        static bool Dark(Brush brush) => brush is SolidColorBrush solid && WidgetVisuals.Luminance(solid.Color) < .2;
        static async Task ThemeSettled() => await Task.Delay(100);
    }
}
