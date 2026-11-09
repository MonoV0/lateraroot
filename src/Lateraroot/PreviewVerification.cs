using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lateraroot.Core;

namespace Lateraroot;

public partial class MainWindow
{
    internal async Task VerifyPreviewAsync()
    {
        var checks = new List<string>();
        string output = Path.Combine(Host.DataDirectory, "preview-result.json");
        try
        {
            await initialRefresh;
            Check(Host.IsPreview && Host.IsSandbox && Host.Settings.LiquidGlass, "Preview binary enables its isolated, refractive preview profile automatically");
            Check(!OrganizeButton.IsEnabled && !RestoreButton.IsEnabled, "Preview does not enable storage or restore controls");
            bool called = false;
            await RunOperation(() => { called = true; return new OperationResult(0, []); }, "test");
            Check(!called && !File.Exists(Path.Combine(Host.DataDirectory, "library.json")), "The shared file-operation guard rejects preview writes before invoking the operation");
            ChangeSort(FileOrder.Name, true);
            Check(Host.Settings.EffectiveDescending && JsonStore.Read(Host.SettingsPath, () => new AppSettings()).EffectiveDescending, "Sorting remains usable and saves to the isolated preview profile");
            Check(Host.Settings.GenreIconsOnly && StackRail.Children.Count == Host.Settings.Stacks.Count, "Icon-only genre display and the shared catalog remain available");
            JsonStore.Write(output, new { Passed = true, Checks = checks }); Host.Shutdown(0);
        }
        catch (Exception ex) { JsonStore.Write(output, new { Passed = false, Checks = checks, Error = ex.ToString() }); Host.Shutdown(1); }
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    }
}
