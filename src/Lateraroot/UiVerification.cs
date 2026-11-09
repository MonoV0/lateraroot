using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lateraroot.Core;

namespace Lateraroot;

public partial class MainWindow
{
    internal async Task VerifyUiAsync()
    {
        var checks = new List<string>();
        string output = Path.Combine(Host.DataDirectory, "verification");
        Directory.CreateDirectory(output);
        try
        {
            await initialRefresh;
            WidgetMotion.TestOverride = null;
            Check(!Host.Settings.ReduceMotion && !Host.Settings.FollowSystemAnimations && WidgetMotion.Allowed, "Production default enables motion independently of Windows preference");
            Host.Settings.FollowSystemAnimations = true;
            Check(WidgetMotion.Allowed == SystemParameters.ClientAreaAnimation, "Optional system policy follows the real Windows animation flag");
            Host.Settings.FollowSystemAnimations = false;
            Host.Settings.ReduceMotion = true; Check(!WidgetMotion.Allowed, "App reduced motion always takes priority"); Host.Settings.ReduceMotion = false;
            foreach (string icon in new[] { "ArrowOut", "ArrowIn", "CloseIcon", "MinusIcon", "GripIcon", "SettingsIcon", "HideIcon", "SearchIcon", "ReloadIcon", "LockIcon", "AddIcon", "UpIcon", "DownIcon", "ImageGenre", "DocumentGenre", "MediaGenre", "ArchiveGenre", "GameGenre", "ShortcutGenre", "FolderGenre", "OtherGenre" })
                Check(FindResource(icon) is Geometry geometry && !geometry.Bounds.IsEmpty && geometry.Bounds.Width <= 24.01 && geometry.Bounds.Height <= 24.01, "Compiled Flaticon geometry: " + icon);
            WidgetMotion.TestOverride = false;
            await Settled();
            var face = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            Check(face.TryGetGlyphTypeface(out var glyph) && glyph.FontUri.ToString().Contains("ZenKakuGothicNew-Medium.ttf", StringComparison.OrdinalIgnoreCase), "Bundled Medium font resolves");
            Check(Host.Settings.Stacks.All(s => s.Color is "#FFFFFF" or "#000000"), "New installations start with white and black genres");
            var clearGlass = GlassProbe("#FFFFFF", false);
            Check(SystemParameters.HighContrast || Pixel(clearGlass, 44, 44)[3] is > 0 and < 255, "Glass surface really transmits the backdrop");
            Check(Pixel(clearGlass, 0, 0)[3] == 0 && Pixel(GlassProbe("#FFFFFF", true), 44, 44)[3] == 255, "Rounded glass leaves empty pixels transparent and reduced transparency is opaque");
            if (!SystemParameters.HighContrast)
            foreach (string tint in new[] { "#FFFFFF", "#000000", "#123456", "#777777", "#707070", "#A8BEDF", "#B4C6B2", "#D4BEDD", "#E0B9B8", "#FFFF00" })
            {
                var probe = GlassProbe(tint, false);
                bool blackInk = WidgetVisuals.Ink(DesktopEdgeWindow.ColorBrush(tint)) == Brushes.Black;
                foreach (int y in new[] { 16, 44, 70 })
                foreach (int backdrop in new[] { 0, 255 })
                {
                    byte[] p = Pixel(probe, 44, y);
                    byte Composite(int c) => (byte)Math.Clamp(c + backdrop * (255 - p[3]) / 255d, 0, 255);
                    double luminance = WidgetVisuals.Luminance(Color.FromRgb(Composite(p[2]), Composite(p[1]), Composite(p[0])));
                    double contrast = blackInk ? (luminance + .05) / .05 : 1.05 / (luminance + .05);
                    Check(contrast >= 4.5, $"Glass text contrast {tint}, y={y}, backdrop={backdrop}");
                }
            }
            // A real pointer can hover the rail while this isolated fixture starts.
            expanded = pinned = false; ApplyPresentation(); UpdateLayout();
            Check(!expanded && Math.Abs(ActualWidth - 68) < 1, "Compact access remains available");
            foreach (double scale in new[] { 1d, 1.5, 2 })
            {
                var icon = new UiIcon { Kind = "MinusIcon", OpticalWeight = ((UiIcon)RailMinimize.Content).OpticalWeight, Foreground = Brushes.Black, Width = 18, Height = 18 };
                icon.Measure(new Size(18, 18)); icon.Arrange(new Rect(0, 0, 18, 18)); icon.UpdateLayout();
                int side = (int)(18 * scale); var bitmap = new RenderTargetBitmap(side, side, 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(icon);
                var pixels = new byte[side * side * 4]; bitmap.CopyPixels(pixels, side * 4, 0);
                int visible = Enumerable.Range(0, side * side).Count(i => pixels[i * 4 + 3] >= 128);
                Check(visible >= 12 * scale, $"Minimize mark retains visible pixels at {scale * 100}% DPI");
            }
            Check(DragHandle.ActualHeight == 66 && RailHeader.Children.Count == 1 && DragHandle.Template.FindName("Glyph", DragHandle) is UiIcon { Kind: "GripIcon" }, "Compact header is one glass drag control with an actual Flaticon grip");
            int docs = Host.Settings.Stacks.FindIndex(x => x.Id == "documents");
            ((Button)StackRail.Children[docs]).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            TogglePin(this, new RoutedEventArgs()); await Settled();
            Check(expanded && pinned && visibleTiles.Count == 3, "Hover and click share the file library");
            Capture(this, "compact.png");
            SearchBox.Text = "アイデア"; Check(visibleTiles.Count == 1, "Filename search");
            SearchBox.Text = "no-such-file"; Check(visibleTiles.Count == 0 && EmptyState.IsVisible, "Empty search state"); SearchBox.Text = "";
            int readsBeforeSort = catalog.ReadCount;
            OrderBox.SelectedIndex = (int)FileOrder.Size; Check(Host.Settings.Order == FileOrder.Size, "Sort persistence"); OrderBox.SelectedIndex = 0;
            Check(catalog.ReadCount == readsBeforeSort, "Sorting uses acquired data without reading the disk again");
            var sortMenu = ((Button)StackRail.Children[docs]).ContextMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "ファイルの並び順");
            ((MenuItem)sortMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            ((MenuItem)sortMenu.Items[6]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(Host.Settings.Order == FileOrder.Name && Host.Settings.EffectiveDescending && DirectionBox.SelectedIndex == 1 && allItems[0].Name == allItems.MaxBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)!.Name, "Shared right-click menu changes the actual file order and inspector direction");
            Check(catalog.ReadCount == readsBeforeSort, "Changing sort direction also reuses the shared catalog");
            ChangeSort(FileOrder.Name, false);
            string gameShortcut = Path.Combine(Host.DesktopDirectory, "検証ゲーム.url"), webShortcut = Path.Combine(Host.DesktopDirectory, "検証Web.url");
            File.WriteAllText(gameShortcut, "[InternetShortcut]\nURL=steam://rungameid/12345\n");
            File.WriteAllText(webShortcut, "[InternetShortcut]\nURL=https://example.com/\n");
            await RefreshFilesAsync(force: true);
            Check(allItems.Single(i => i.Path == gameShortcut).StackId == "games" && allItems.Single(i => i.Path == webShortcut).StackId == "shortcuts", "Real file acquisition separates game launch links from normal web shortcuts");
            SelectStack("games"); pinned = true; await Settled();
            Check(visibleTiles.Count == 1 && visibleTiles[0].Item.Path == gameShortcut, "Game genre displays the detected shortcut through the shared file view");
            Capture(this, "games.png");
            File.Delete(gameShortcut); File.Delete(webShortcut); await RefreshFilesAsync(force: true); SelectStack("documents"); pinned = true;
            Host.Settings.GenreIconsOnly = true; var originalGenreIcon = Host.Settings.Stacks[docs].Icon; Host.Settings.Stacks[docs].Icon = GenreIcon.Game; BuildRail(); await Settled();
            Check(StackRail.Children.OfType<Button>().All(b => Descendants<UiIcon>(b).Count() == 1 && !Descendants<TextBlock>(b).Any(t => t.IsVisible)), "Icon-only mode removes small genre names and counts from compact cells");
            var docsButton = (Button)StackRail.Children[docs];
            Check(Descendants<UiIcon>(docsButton).Single().Kind == "GameGenre" && System.Windows.Automation.AutomationProperties.GetName(docsButton).Contains(Host.Settings.Stacks[docs].Name) && ((string)docsButton.ToolTip).Contains(Host.Settings.Stacks[docs].Name), "Chosen genre icon retains a full tooltip and accessible name");
            Host.Settings.Stacks[docs].Icon = originalGenreIcon; BuildRail(); await Settled();
            Capture(this, "compact-icons.png"); UpdateDesktopContext(true); await Settled();
            Check(desktopEdge.SquareCells.Take(Host.Settings.Stacks.Count).Cast<Button>().All(b => Descendants<UiIcon>(b).Count() == 1 && !Descendants<TextBlock>(b).Any(t => t.IsVisible)), "Desktop and compact presentations share the icon-only setting");
            CaptureDesktop("desktop-icons.png");
            Host.Settings.GenreIconsOnly = false; Host.Settings.Stacks[docs].Icon = originalGenreIcon; BuildRail(); UpdateDesktopContext(false); SelectStack("documents"); pinned = true;
            var categoryBefore = desktopEdge.SquareCells[0]; var compactBefore = StackRail.Children[0]; var filesBefore = FileItems.ItemsSource;
            await RefreshFilesAsync(force: true);
            Check(ReferenceEquals(categoryBefore, desktopEdge.SquareCells[0]) && ReferenceEquals(compactBefore, StackRail.Children[0]) && ReferenceEquals(filesBefore, FileItems.ItemsSource), "Unchanged acquisition preserves category controls and the open file view");
            string otherChange = Path.Combine(Host.DesktopDirectory, "監視確認.zzz");
            File.WriteAllText(otherChange, "Watcher notification");
            await WaitForFiles(() => allItems.Any(i => i.Path == otherChange));
            Check(ReferenceEquals(filesBefore, FileItems.ItemsSource) && ReferenceEquals(categoryBefore, desktopEdge.SquareCells[0]), "A different genre updates counts without replacing the selected genre view");
            File.Delete(otherChange); await RefreshFilesAsync(force: true);
            foreach (var visibility in new[] { DockVisibility.Hidden, DockVisibility.Minimized })
            {
                await Task.Delay(750); await RefreshFilesAsync();
                SetDockVisibility(visibility); int before = catalog.ReadCount;
                string waiting = Path.Combine(Host.DesktopDirectory, $"待機確認-{visibility}.txt"); File.WriteAllText(waiting, "Deferred read");
                await WaitForFiles(() => catalog.IsDirty || refreshQueued != 0); await Task.Delay(750);
                Check(!catalog.IsDirty && !refreshTimer.IsEnabled, $"{visibility} notifications cannot restart an already-active acquisition");
                Check(catalog.ReadCount == before && !allItems.Any(i => i.Path == waiting), $"{visibility} coalesces changes without background file reads");
                SetDockVisibility(DockVisibility.Visible); await RefreshFilesAsync();
                Check(allItems.Any(i => i.Path == waiting), $"{visibility} return acquires the pending changes");
                File.Delete(waiting); await RefreshFilesAsync(force: true);
            }
            SelectStack("documents"); pinned = true; await Settled();
            var lockAction = ((Button)StackRail.Children[0]).ContextMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "配置をロック");
            lockAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(Host.Settings.LockPosition && !DragHandle.IsEnabled && !MiniDragHandle.IsEnabled && !desktopEdge.CanDrag, "The shared lock command disables all three drag handles");
            var placementBefore = (Host.Settings.Edge, Host.Settings.DockOffset, Host.Settings.Monitor);
            DockDragStarted(DragHandle, new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
            DockDragDelta(DragHandle, new System.Windows.Controls.Primitives.DragDeltaEventArgs(100, 100));
            DockDragCompleted(DragHandle, new System.Windows.Controls.Primitives.DragCompletedEventArgs(100, 100, false));
            Check(!dragging && placementBefore == (Host.Settings.Edge, Host.Settings.DockOffset, Host.Settings.Monitor) && JsonStore.Read(Host.SettingsPath, () => new AppSettings()).LockPosition, "Locked drag handlers cannot move or overwrite the saved placement");
            lockAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!Host.Settings.LockPosition && DragHandle.IsEnabled && MiniDragHandle.IsEnabled && desktopEdge.CanDrag, "Unlock restores dragging without disabling file interaction");
            int manualBefore = catalog.ReadCount; await ReloadFilesAsync();
            Check(catalog.ReadCount > manualBefore && StatusText.Text.Contains("再読み込み"), "Manual reload reacquires data and reports completion");
            for (int i = 1; i <= 16; i++) File.WriteAllText(Path.Combine(Host.DesktopDirectory, $"資料 {i:00}.txt"), "Widget demo");
            await RefreshFilesAsync(force: true); UpdateDesktopContext(true); await Settled();
            Check(desktopEdge.IsVisible && !IsVisible && !desktopEdge.Topmost, "Desktop widgets live on the desktop rather than the topmost app layer");
            Check((WindowPlacement.GetWindowLongPtr(new WindowInteropHelper(desktopEdge).Handle, -20).ToInt64() & 8) == 0, "Native desktop window is non-topmost");
            Check(desktopEdge.SquareCells.All(c => Math.Abs(c.ActualWidth - c.ActualHeight) < .01 && c.ActualWidth >= 72), "All category and utility cells are actual squares");
            string originalColor = Host.Settings.Stacks[0].Color;
            Host.Settings.Stacks[0].Color = "#000000"; BuildRail();
            Check(desktopEdge.SquareCells[0] is Button { Foreground: SolidColorBrush ink } && ink.Color == Colors.White, "Dark custom colors choose readable white ink");
            Host.Settings.Stacks[0].Color = originalColor; BuildRail();
            Check(WidgetVisuals.Balance("ドキュメント", 68).Split('\n').All(line => line.Length >= 2), "Short Japanese labels have balanced lines");
            SelectStack("documents"); pinned = true; await Settled();
            Check(desktopMode && !Topmost && visibleTiles.Count == DesktopColumns && ExpandAllButton.IsVisible, "One-row square widget preview");
            Check(FileScroll is { } preview && preview.ExtentHeight <= preview.ViewportHeight + 1 && preview.ViewportWidth >= DesktopColumns * TileSide - 1, "The preview row fits without clipping");
            Check(Descendants<Button>(FileItems).All(c => Math.Abs(c.ActualWidth - c.ActualHeight) < .01), "Realized file cells are square too");
            CaptureDesktop("desktop-preview.png");
            ToggleAllFiles(this, new RoutedEventArgs()); await Settled();
            Check(showAll && visibleTiles.Count == 19 && !SearchTools.IsVisible, "OPEN reveals the square grid without an inspector backdrop");
            var pixel = new byte[4]; double pixelScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Render(this).CopyPixels(new Int32Rect((int)(TileSide * .5 * pixelScale), (int)(TileSide * 1.5 * pixelScale), 1, 1), pixel, 4, 0);
            Check(pixel[3] == 0, "Unused widget space is transparent rather than a rectangular pasted backdrop");
            CaptureDesktop("desktop-expanded.png");
            ShowInspector(); await Settled();
            Check(widgetInspector && SearchTools.IsVisible && OrganizeButton.IsVisible, "Search and file operations use the shared inspector");
            Capture(this, "inspector.png"); SelectStack("documents");
            Check(!widgetInspector && !SearchTools.IsVisible, "Selecting a widget returns from the inspector to the grid");
            foreach (int size in new[] { 72, 88, 104 })
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                Host.Settings.TileSize = size; Host.Settings.Edge = edge; BuildRail(); ApplyPresentation(); await Settled();
                CheckBounds(this, edge, false); CheckBounds(desktopEdge, edge, true);
                Check(desktopEdge.SquareCells.All(c => Math.Abs(c.ActualWidth - c.ActualHeight) < .01), $"Square proportions at {size} DIP on {edge}");
            }
            Host.Settings.TileSize = 88; Host.Settings.Edge = DockEdge.Right; BuildRail(); ApplyPresentation();
            var baseline = allItems;
            var sample = baseline.First(x => x.StackId == "documents");
            allItems = Enumerable.Range(0, 1500).Select(i => sample with { Name = $"Virtual file {i:0000}.txt" }).ToList();
            showAll = true; ApplyPresentation(); await Settled();
            Check(visibleTiles.Count == 1500 && FileItems.Items.Count > 100, "All files remain accessible in a large widget");
            int realized = Enumerable.Range(0, FileItems.Items.Count).Count(i => FileItems.ItemContainerGenerator.ContainerFromIndex(i) is not null);
            Check(realized < 25 && FileItems.ItemContainerGenerator.ContainerFromIndex(FileItems.Items.Count - 1) is null, "Native recycling bounds the realized rows");
            FileScroll!.ScrollToEnd(); await Settled();
            Check(FileItems.ItemContainerGenerator.ContainerFromIndex(FileItems.Items.Count - 1) is not null, "The final files can be scrolled into view");
            allItems = baseline; showAll = false; await RefreshFilesAsync(force: true); FileScroll?.ScrollToTop();
            SetDockVisibility(DockVisibility.Minimized); await Settled();
            Check(IsVisible && !desktopEdge.IsVisible && MinimizedTab.IsVisible, "Minimized widget tab");
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                Host.Settings.Edge = edge; ApplyPresentation(); await Settled();
                Check(MiniRestore.ActualWidth >= 24 && MiniRestore.ActualHeight >= 24, edge + " minimized control");
            }
            Host.Settings.Edge = DockEdge.Right; SetDockVisibility(DockVisibility.Hidden);
            Check(!IsVisible && !desktopEdge.IsVisible, "Hidden widgets leave no surface");
            bool handled = false; WindowMessage(new WindowInteropHelper(this).Handle, 0x0312, new IntPtr(1), IntPtr.Zero, ref handled);
            Check(handled && desktopEdge.IsVisible, "Shortcut restores hidden widgets");
            modalDepth++; UpdateDesktopContext(false); Check(!desktopEdge.IsVisible, "Foreground change hides the desktop layer even during a dialog"); EndModal();
            Check(!desktopMode && IsVisible && Topmost, "Compact access replaces desktop widgets in application context");
            foreach (var edge in Enum.GetValues<DockEdge>())
            foreach (double offset in new[] { 0d, 1d })
            {
                Host.Settings.Edge = edge; Host.Settings.DockOffset = offset; ApplyPresentation(); await Settled(); CheckBounds(this, edge, true);
            }
            Host.Settings.Edge = DockEdge.Right; Host.Settings.DockOffset = .5; BuildRail(); ApplyPresentation();
            var priorSettings = JsonStore.Clone(Host.Settings);
            var settings = new SettingsWindow(); settings.Show(); await Settled();
            Check(settings.IsVisible && settings.FontFamily.Source.Contains("Zen Kaku Gothic New"), "The widget editor uses the same type system");
            Check(Descendants<TabItem>(settings).All(t => t.ActualHeight >= 38), "Settings tab labels fit without clipping");
            Check(settings.ResetPaletteButton.TransformToAncestor(settings.GenreScroll).Transform(new Point(settings.ResetPaletteButton.ActualWidth, 0)).X <= settings.GenreScroll.ViewportWidth + 1, "Monochrome reset stays inside the editor viewport");
            Check(Descendants<TextBlock>(settings).First(t => t.Text == "ジャンル名").Foreground is SolidColorBrush labelInk && labelInk.Color.R < 100, "Selected tab does not turn editor labels white");
            settings.NameBox.Text = "制作資料"; settings.FilterBox.Text = ".blend, .psd";
            ((Button)settings.PalettePanel.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settled();
            Check(settings.SelectedRule?.Color == "#A8BEDF" && Descendants<Border>((Button)settings.PalettePanel.Children[2]).Any(b => b.Background is SolidColorBrush c && c.Color == (Color)ColorConverter.ConvertFromString("#A8BEDF")), "Palette buttons visibly show their color and update the preview");
            settings.ColorBox.Text = "#12";
            Check(!settings.SaveButton.IsEnabled && settings.ColorError.Text.Length > 0 && settings.SelectedRule?.Color == "#A8BEDF", "Invalid color input has inline feedback and cannot overwrite the selected color");
            settings.ColorBox.Text = "#123456"; await Settled();
            Check(settings.SaveButton.IsEnabled && settings.SquarePreview.Background is SolidColorBrush custom && custom.Color == (Color)ColorConverter.ConvertFromString("#123456"), "Arbitrary HEX colors update the real glass preview");
            Check(Host.Settings.Stacks[0].Name == priorSettings.Stacks[0].Name && Host.Settings.Stacks[0].Color == priorSettings.Stacks[0].Color, "Editing a genre is isolated until saved");
            Capture(settings, "settings-custom.png");
            settings.AddGenre(); settings.NameBox.Text = "素材フォルダー"; settings.KindBox.SelectedIndex = 1; await Settled();
            Check(settings.SelectedRule?.Name == "素材フォルダー" && settings.SelectedRule.Filter == "<folders>", "Adding a genre and choosing folders uses plain controls");
            settings.KindBox.SelectedIndex = 3; Check(settings.SelectedRule?.Filter == "<games>" && settings.FilterHint.Text.Contains("ゲーム"), "Game auto-classification is editable without typing special tokens"); settings.KindBox.SelectedIndex = 1;
            string addedId = settings.SelectedRule!.Id; int originalIndex = settings.RulesList.SelectedIndex; settings.Move(-1);
            Check(settings.SelectedRule?.Id == addedId && settings.RulesList.SelectedIndex == originalIndex - 1, "Genre priority moves with the selected entry");
            Check(settings.RemoveSelectedGenre(), "Custom genres can be removed before saving");
            settings.RulesList.SelectedItem = settings.RulesList.Items.Cast<StackRule>().First(r => r.Tokens.Contains("*"));
            Check(!settings.RemoveSelectedGenre() && !settings.RemoveButton.IsEnabled && !settings.KindBox.IsEnabled, "The required fallback genre is protected while its name and color remain editable");
            settings.RulesList.SelectedIndex = 0; settings.TransparencyBox.IsChecked = true; settings.LockPositionBox.IsChecked = true; settings.FollowSystemBox.IsChecked = true;
            settings.IconOnlyBox.IsChecked = true; settings.GenreIconBox.SelectedValue = GenreIcon.Game;
            settings.SortBox.SelectedIndex = (int)FileOrder.Type; settings.SortDirectionBox.SelectedIndex = 1;
            Check(settings.PreviewGenreIcon.IsVisible && settings.PreviewGenreIcon.Kind == "GameGenre" && !settings.PreviewName.IsVisible, "Icon-only selection updates the real settings preview");
            Check(GlassSurface.GetReduced(settings), "Reduced transparency updates the preview through the shared material policy");
            settings.SaveDraft();
            var savedGenres = JsonStore.Read(Host.SettingsPath, () => new AppSettings());
            Check(savedGenres.Stacks[0].Name == "制作資料" && savedGenres.Stacks[0].Color == "#123456" && savedGenres.Stacks[0].Filter == ".blend, .psd" && savedGenres.ReduceTransparency, "Genre, arbitrary color, filter and transparency are saved together");
            Check(savedGenres.LockPosition, "Position lock is saved through the same settings draft");
            Check(savedGenres.FollowSystemAnimations, "Optional Windows animation policy is persisted through the settings draft");
            Check(savedGenres.GenreIconsOnly && savedGenres.Stacks[0].Icon == GenreIcon.Game && savedGenres.Order == FileOrder.Type && savedGenres.SortDescending == true, "Genre icon choice, icon-only mode and both sort settings persist together");
            settings.ResetColors();
            Check(settings.RulesList.Items.Cast<StackRule>().All(r => r.Color is "#FFFFFF" or "#000000") && settings.SelectedRule?.Name == "制作資料" && settings.SelectedRule.Filter == ".blend, .psd", "Monochrome reset preserves user genre names and classification rules");
            Check(Host.Settings.Stacks[0].Color == "#123456", "Subsequent draft edits do not mutate already-saved application settings");
            settings.Close(); Host.Settings = priorSettings; JsonStore.Write(Host.SettingsPath, priorSettings);
            var cleanSettings = new SettingsWindow(); cleanSettings.Show(); await Settled(); Capture(cleanSettings, "settings.png");
            Check((string)cleanSettings.IconCredits.Content == "Uicons by Flaticon" && cleanSettings.IconCredits.IsVisible && cleanSettings.FollowSystemBox.IsChecked == false, "Settings visibly credits Flaticon and preserves the app-first motion default");
            Descendants<TabControl>(cleanSettings).Single().SelectedIndex = 1; await Settled();
            Check(cleanSettings.LockPositionBox.IsVisible && cleanSettings.LockPositionBox.IsEnabled, "Placement lock remains directly accessible in layout settings");
            Capture(cleanSettings, "settings-layout.png"); cleanSettings.Close();
            var updateProvider = new UiUpdateProvider(); var updateSession = new UpdateSession(updateProvider);
            var updateDialog = new UpdateWindow(updateSession, null); updateDialog.Show(); await Settled();
            Check(updateSession.Snapshot.Stage == UpdateStage.Available && updateDialog.UpdateAction.IsEnabled && (string)updateDialog.UpdateAction.Content == "ダウンロード", "Update dialog shows available release and explicit download");
            var downloadingUpdate = updateDialog.PerformActionAsync(); await Settled();
            Check(updateSession.Snapshot.Stage == UpdateStage.Downloading && updateDialog.DownloadProgress.IsVisible && !updateDialog.UpdateAction.IsEnabled && (string)updateDialog.DismissButton.Content == "中止", "Update dialog exposes progress and cancel without duplicate downloads");
            updateProvider.Finish.SetResult(); await downloadingUpdate; await Settled();
            Check(updateSession.Snapshot.Stage == UpdateStage.Ready && updateDialog.UpdateAction.IsEnabled && (string)updateDialog.UpdateAction.Content == "更新して再起動", "Update requires explicit restart after verification");
            Capture(updateDialog, "update.png"); updateDialog.Close();
            SelectStack("documents"); pinned = true;
            int total = allItems.Count;
            var stored = Host.Library.Organize(allItems.Where(x => x.CanOrganize));
            Check(stored.Succeeded == total && stored.Errors.Count == 0, "Explicit storage preserves the file workflow");
            await RefreshFilesAsync(force: true); Check(allItems.All(x => x.IsStored) && RestoreButton.IsEnabled, "Shared inspector tracks stored files");
            SelectStack("media"); SearchBox.Text = "no-match";
            Check(RestoreButton.IsEnabled, "Global restore remains available from an empty genre and empty search");
            await RestoreAllAsync();
            Check(!Host.Library.HasRecoveryEntries && StatusText.Text.StartsWith(total + " 個を復元"), "The same action as the restore button returns every stored genre"); await RefreshFilesAsync(force: true);
            SearchBox.Text = ""; SelectStack("documents");
            Check(allItems.All(x => !x.IsStored), "Restored files have no duplicate library entries");
            WidgetMotion.TestOverride = null; Host.Settings.ReduceMotion = false;
            var delayed = new Border(); WidgetMotion.Reveal(delayed, DockEdge.Right, 7);
            Check(delayed.Opacity == 0 && delayed.RenderTransform is TranslateTransform { X: 12 }, "Delayed entry establishes its invisible initial frame synchronously");
            await Task.Delay(75); Check(delayed.Opacity == 0, "Staggered cells remain hidden while waiting, without a flash");
            await Task.Delay(500); Check(delayed.Opacity == 1 && delayed.RenderTransform is TranslateTransform { X: 0 }, "Delayed entry settles without a retained offset");
            UpdateDesktopContext(true); SelectStack("documents"); closeTimer.Stop(); pinned = true;
            await Task.Delay(90); Check(Panel.Opacity > 0 && Panel.Opacity < 1, "Entry motion has an observable intermediate state");
            CaptureDesktop("motion-intermediate.png");
            Check(Descendants<Button>(FileItems).Any(b => b.Opacity is > 0 and < 1), "Realized file cells animate through their own stagger under normal production policy");
            await Task.Delay(650); Check(Math.Abs(Panel.Opacity - 1) < .001 && Panel.RenderTransform is TranslateTransform { X: 0, Y: 0 }, "Entry reaches its stable final state");
            Collapse(); await Task.Delay(45); SelectStack("documents"); closeTimer.Stop(); await Task.Delay(400);
            Check(expanded && IsVisible && !closingPanel && Math.Abs(Panel.Opacity - 1) < .001, "Rapid re-entry cancels a pending exit");
            Collapse(); await Task.Delay(220); Check(!expanded && !IsVisible, "Exit completes and releases the file surface");
            SelectStack("documents"); SetDockVisibility(DockVisibility.Hidden); await Task.Delay(500);
            Check(!IsVisible && !desktopEdge.IsVisible, "Stale entry clocks cannot reopen hidden widgets");
            Host.Settings.ReduceMotion = true; RestoreDock(); SelectStack("documents"); await Settled();
            Check(!WidgetMotion.GetEnabled(this) && Panel.Opacity == 1 && Panel.RenderTransform is TranslateTransform { X: 0, Y: 0 }, "Reduced motion uses the same immediate final state");
            Collapse(); Check(!expanded, "Reduced-motion exit is immediate");
            Host.Settings.ReduceMotion = false; WidgetMotion.TestOverride = null;
            UpdateDesktopContext(false); Collapse(); await Task.Delay(220);
            var hoverButton = (Button)StackRail.Children[0]; hoverButton.ApplyTemplate();
            hoverButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            closeTimer.Stop(); await Task.Delay(60);
            var hoverInk = (Border)hoverButton.Template.FindName("HoverInk", hoverButton);
            Check(hoverInk.Opacity is > 0 and < .08, "Real button hover has an observable intermediate state with no policy override");
            await Task.Delay(250); Check(Math.Abs(hoverInk.Opacity - .08) < .001, "Hover reaches visible feedback");
            hoverButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            await Task.Delay(180); Check(hoverInk.Opacity == 0, "Hover exit releases its state layer");
            SetDockVisibility(DockVisibility.Minimized);
            // A busy runner may resume a 60ms delay after the entire 140ms fade.
            // Check scheduling synchronously, then verify its eventual final state.
            Check(hidingSurface && Frame.HasAnimatedProperties && Rail.IsVisible && !MinimizedTab.IsVisible, "Minimize schedules its fade before switching the current surface");
            await Task.Delay(500); Check(MinimizedTab.IsVisible && MinimizedTab.Opacity == 1, "Minimize completes with a stable access tab");
            RestoreDock(); SetDockVisibility(DockVisibility.Hidden); await Task.Delay(35); RestoreDock(); await Task.Delay(500);
            Check(IsVisible && Host.Settings.Visibility == DockVisibility.Visible && Frame.Opacity == 1, "Rapid restore invalidates a pending hide callback");
            SetDockVisibility(DockVisibility.Hidden); await Task.Delay(35); Host.Settings.ReduceMotion = true; RefreshMotionPolicy();
            Check(!IsVisible && !desktopEdge.IsVisible && !hidingSurface, "Disabling motion during an active hide immediately finishes the requested state");
            Host.Settings.ReduceMotion = false; RestoreDock();
            await RecordMotion(); WidgetMotion.TestOverride = false;
            Check(File.Exists(Path.Combine(output, "motion.gif")), "Rendered animation preview is available");
            Check(Host.Windows.Count == 2, "Only two reusable presentation windows");
            await VerifyThemeAsync(Check, Capture);
            SaveSettings(); JsonStore.Write(Path.Combine(output, "result.json"), new { Passed = true, Checks = checks });
            Host.Trace("UI verification passed: " + checks.Count); Host.Shutdown(0);
        }
        catch (Exception ex)
        {
            JsonStore.Write(Path.Combine(output, "result.json"), new { Passed = false, Checks = checks, Error = ex.ToString() });
            Host.Trace("UI verification failed: " + ex.Message); Host.Shutdown(1);
        }
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        void CheckBounds(Window window, DockEdge edge, bool onEdge)
        {
            GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
            var area = WindowPlacement.Area(WindowPlacement.Screen);
            bool inside = r.Left >= area.X - 1 && r.Right <= area.Right + 1 && r.Top >= area.Y - 1 && r.Bottom <= area.Bottom + 1;
            bool aligned = edge switch { DockEdge.Right => Math.Abs(r.Right - area.Right) < 2, DockEdge.Left => Math.Abs(r.Left - area.X) < 2, DockEdge.Top => Math.Abs(r.Top - area.Y) < 2, _ => Math.Abs(r.Bottom - area.Bottom) < 2 };
            Check(inside && (!onEdge || aligned), $"{(desktopMode ? "Desktop" : "Compact")} {window.Title} {edge} bounds and alignment");
        }
        static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found;
                foreach (var descendant in Descendants<T>(child)) yield return descendant;
            }
        }
        RenderTargetBitmap Render(Window window)
        {
            window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window); return bitmap;
        }
        void Write(BitmapSource bitmap, string name)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        }
        void Capture(Window window, string name) => Write(Render(window), name);
        static byte[] Pixel(BitmapSource bitmap, int x, int y) { var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel; }
        static BitmapSource GlassProbe(string color, bool reduced)
        {
            var surface = new GlassSurface { Width = 88, Height = 88, Background = DesktopEdgeWindow.ColorBrush(color) };
            GlassSurface.SetReduced(surface, reduced); surface.Measure(new Size(88, 88)); surface.Arrange(new Rect(0, 0, 88, 88)); surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(88, 88, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface); return bitmap;
        }
        static void DrawBackdrop(DrawingContext dc, Rect area)
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(211, 215, 220), Color.FromRgb(242, 241, 238), 35), null, area);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(180, 255, 255, 255), Colors.Transparent), null, new Point(area.Width * .85, area.Height * .25), area.Width * .48, area.Height * .8);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(65, 106, 113, 124), Colors.Transparent), null, new Point(area.Width * .88, area.Height * .9), area.Width * .5, area.Height * .7);
        }
        void CaptureDesktop(string name)
        {
            var area = WindowPlacement.Area(WindowPlacement.Screen);
            var canvas = new DrawingVisual();
            using (var dc = canvas.RenderOpen())
            {
                DrawBackdrop(dc, new Rect(0, 0, area.Width, area.Height));
                foreach (var window in new Window[] { desktopEdge, this })
                {
                    if (!window.IsVisible) continue;
                    GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
                    dc.DrawImage(Render(window), new Rect(r.Left - area.X, r.Top - area.Y, r.Right - r.Left, r.Bottom - r.Top));
                }
            }
            var bitmap = new RenderTargetBitmap((int)area.Width, (int)area.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas); Write(bitmap, name);
        }
        async Task RecordMotion()
        {
            UpdateDesktopContext(true);
            Host.Settings.Edge = DockEdge.Right; Host.Settings.TileSize = 88; BuildRail();
            expanded = false; ApplyPresentation();
            var encoder = new GifBitmapEncoder();
            var area = WindowPlacement.Area(WindowPlacement.Screen);
            var dpi = VisualTreeHelper.GetDpi(this);
            double side = TileSide * dpi.DpiScaleX;
            double width = Math.Min(area.Width, side * 10 + 32), height = Math.Min(area.Height, side * 8 + 32);
            GetWindowRect(new WindowInteropHelper(desktopEdge).Handle, out var anchor);
            double originX = area.Right - width, originY = Math.Clamp(anchor.Top - 16, area.Y, area.Bottom - height);
            var frameClock = System.Diagnostics.Stopwatch.StartNew(); double lastFrame = 0;
            for (int i = 0; i < 30; i++)
            {
                if (i == 1) { SelectStack("documents"); pinned = true; }
                if (i == 20) Collapse();
                closeTimer.Stop(); await Task.Delay(40); UpdateLayout();
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // Keep the animated documentation small; still previews use the generated neutral backdrop.
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    foreach (var window in new Window[] { desktopEdge, this })
                    {
                        if (!window.IsVisible) continue;
                        GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
                        dc.DrawImage(Render(window), new Rect(r.Left - originX, r.Top - originY, r.Right - r.Left, r.Bottom - r.Top));
                    }
                }
                var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                double now = frameClock.Elapsed.TotalMilliseconds;
                var metadata = new BitmapMetadata("gif"); metadata.SetQuery("/grctlext/Delay", (ushort)Math.Clamp(Math.Round((now - lastFrame) / 10), 1, 100)); lastFrame = now; metadata.SetQuery("/grctlext/Disposal", (byte)2);
                encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
            }
            using var buffer = new MemoryStream(); encoder.Save(buffer);
            var bytes = buffer.ToArray();
            int offset = 13 + ((bytes[10] & 128) != 0 ? 3 * (1 << ((bytes[10] & 7) + 1)) : 0);
            using var stream = File.Create(Path.Combine(output, "motion.gif"));
            stream.Write(bytes.AsSpan(0, offset)); stream.Write(new byte[] { 0x21, 0xff, 11 }); stream.Write("NETSCAPE2.0"u8); stream.Write(new byte[] { 3, 1, 0, 0, 0 }); stream.Write(bytes.AsSpan(offset));
        }
        async Task Settled() { closeTimer.Stop(); await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(80); }
        async Task WaitForFiles(Func<bool> condition)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!condition()) { if (timeout.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException("File notification did not reach the view"); closeTimer.Stop(); await Task.Delay(50); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
}
