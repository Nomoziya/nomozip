using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

internal static class DesktopVisualRefreshValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, MainViewModel model, ThemeResources theme, string root)
    {
        var checks = new Dictionary<string, bool>();
        var samples = Path.Combine(root, "artifacts/visual-refresh", UiText.LanguageCode);
        Directory.CreateDirectory(samples);
        void Check(string name, bool value) => checks.Add(name, value);
        async Task Layout() => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        // Right-click menus open from a real placement target and are tidied exactly as they are for a pointer user.
        async Task Preview(ContextMenu menu, FrameworkElement target, string name)
        {
            menu.Opacity = 0; menu.PlacementTarget = target; menu.IsOpen = true; await Layout(); RowActions.Tidy(menu);
            menu.Opacity = 1; Render(menu, Path.Combine(samples, name + ".png")); menu.Opacity = 0; menu.IsOpen = false;
        }
        void Capture(string name, double scale = 1)
        {
            window.Opacity = 1;
            DesktopAccessibilityValidation.Render(window, Path.Combine(samples, name + ".png"), scale, clientOnly: true);
            window.Opacity = 0;
        }
        try
        {
            model.Tasks.Clear(); model.EngineVersions.Clear(); model.CompressionSources.Clear(); model.LoadPreview();
            model.PasswordRows[0] = model.PasswordRows[0] with { Group = "Example group", Source = "example.test" };
            window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
            Check("window and sidebar load the product icon", window.Icon is not null && window.BrandMark.Source is not null);
            var icon = new IconBitmapDecoder(new Uri("pack://application:,,,/ArchivePassword;component/Assets/nomozip.ico"), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check("embedded icon contains nine distinct sizes", icon.Frames.Select(frame => frame.PixelWidth).Order().SequenceEqual(new[] {16,20,24,32,40,48,64,128,256}));
            foreach (var mode in new[] { "light", "dark", "contrast" })
            {
                theme.SetMode(mode == "contrast" ? "light" : mode); theme.Apply(mode == "contrast");
                foreach (var width in new[] { 800, 1120, 1600 })
                {
                    window.MinWidth = width; window.Width = width; window.Height = 760; model.Page = 2; model.SettingsSection = 2; await Layout();
                    ((Border)((FrameworkElement)window.VaultIdleChoice.Parent).Parent).BringIntoView(); await Layout();
                    Check($"{mode}/{width} idle field has readable bounded width", window.VaultIdleChoice.ActualWidth >= 80 && window.VaultIdleChoice.ActualWidth <= 280 && Bounds(window.VaultIdleChoice).Right < window.ActualWidth);
                    Check($"{mode}/{width} settings card measure stays bounded", window.PreferencesCards.ActualWidth <= 920 && window.PreferencesScroll.ExtentWidth <= window.PreferencesScroll.ViewportWidth + 1);
                    var note = Descendants(window.VaultProtectionCard).OfType<TextBlock>().Single(block=>block.Text==model.VaultProtectionMessage);
                    var noteBounds=Bounds(note);var cardBounds=Bounds(window.VaultProtectionCard);
                    var formatted=new FormattedText(note.Text,System.Globalization.CultureInfo.CurrentUICulture,note.FlowDirection,
                        new Typeface(note.FontFamily,note.FontStyle,note.FontWeight,note.FontStretch),note.FontSize,note.Foreground,VisualTreeHelper.GetDpi(note).PixelsPerDip){MaxTextWidth=Math.Max(1,note.ActualWidth)};
                    Check($"{mode}/{width} privacy information fits actual vault settings",note.IsVisible && note.ActualWidth>0 && noteBounds.Right<=cardBounds.Right+1 && noteBounds.Bottom<=cardBounds.Bottom+1 && note.ActualHeight>=formatted.Height-1);
                    Capture($"{mode}-{width}-vault-settings", width == 800 ? 2 : 1);
                    model.SettingsSection = 3; await Layout();
                    ((Border)((FrameworkElement)((FrameworkElement)window.RegisterAssociationsAction.Parent).Parent).Parent).BringIntoView(); await Layout();
                    var buttons = new[] { window.RegisterAssociationsAction, window.RemoveAssociationsAction, window.DefaultAppsAction };
                    var rects = buttons.Select(Bounds).ToArray();
                    Check($"{mode}/{width} association buttons wrap with both gaps", rects.Zip(rects.Skip(1)).All(pair => pair.Second.Top >= pair.First.Bottom + 7 || pair.Second.Left >= pair.First.Right + 11));
                    Check($"{mode}/{width} association actions remain within content", rects.All(rect => rect.Left >= Bounds(window.PreferencesCards).Left && rect.Right <= window.ActualWidth));
                    Capture($"{mode}-{width}-association-settings");
                }
                window.MinWidth = 1120; window.Width = 1120; model.Page = 0; model.SelectedTask = model.Tasks[1]; await Layout();
                Capture($"{mode}-1120-page-0-selected");
                var row = (DataGridRow)window.ArchiveTasks.ItemContainerGenerator.ContainerFromItem(model.SelectedTask);
                var cell = Descendants(row).OfType<DataGridCell>().First();
                cell.Focus(); await Layout();
                var focus = (Border)row.Template.FindName("RowFocus", row);
                Check(mode + " keyboard focus spans the entire row", cell.IsKeyboardFocusWithin && focus.Visibility == Visibility.Visible
                    && focus.ActualWidth >= row.ActualWidth - 5 && focus.Margin == new Thickness(2) && focus.BorderThickness == new Thickness(2));
                var inside = focus.TransformToAncestor(row).TransformBounds(new Rect(focus.RenderSize)); inside.Inflate(-1, -1);
                Check(mode + " row focus leaves all visible text clear", Descendants(row).OfType<TextBlock>().Where(text => text.IsVisible && text.Text.Length > 0)
                    .All(text => inside.Contains(text.TransformToAncestor(row).TransformBounds(new Rect(text.RenderSize)))));
                Check(mode + " selected cell retains readable row foreground", ColorOf(cell.Foreground) == ColorOf(row.Foreground));
                Capture($"{mode}-cell-focus");
                Descendants(row).OfType<DataGridCell>().Last().Focus(); await Layout();
                Check(mode + " moving between columns keeps one complete row focus", row.IsKeyboardFocusWithin && focus.Visibility == Visibility.Visible && focus.ActualWidth >= row.ActualWidth - 5);
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    window.Opacity = 1; bitmap.Render(window); window.Opacity = 0;
                    var bounds = Bounds(focus); var color = ColorOf(focus.BorderBrush);
                    bool Stroke(double x, double y)
                    {
                        var left = Math.Max(0, (int)Math.Floor(x * scale) - 2); var top = Math.Max(0, (int)Math.Floor(y * scale) - 2);
                        var width = Math.Min(5, bitmap.PixelWidth - left); var height = Math.Min(5, bitmap.PixelHeight - top);
                        if (width <= 0 || height <= 0) return false;
                        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(new Int32Rect(left, top, width, height), pixels, width * 4, 0);
                        return Enumerable.Range(0, width * height).Any(i => pixels[i * 4] == color.B && pixels[i * 4 + 1] == color.G && pixels[i * 4 + 2] == color.R && pixels[i * 4 + 3] == 255);
                    }
                    Check($"{mode} complete row focus strokes at {scale * 100:0}%", Stroke(bounds.Left + 0.5, bounds.Top + bounds.Height / 2)
                        && Stroke(bounds.Right - 0.5, bounds.Top + bounds.Height / 2) && Stroke(bounds.Left + bounds.Width / 2, bounds.Top + 0.5) && Stroke(bounds.Left + bounds.Width / 2, bounds.Bottom - 0.5));
                    Capture($"{mode}-row-focus-{scale * 100:0}", scale);
                }
                window.PrepareExtractionOptionsForValidation = dialog =>
                {
                    dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.ShowActivated=false;dialog.Opacity=0;
                    dialog.Loaded += (_,_) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(() =>
                    {
                        var rules=window.ExtractionRulesAction;
                        for(DependencyObject? parent=rules;parent is not null;parent=LogicalTreeHelper.GetParent(parent))
                            if(parent is Expander expander)expander.IsExpanded=true;
                        dialog.UpdateLayout();
                        Check(mode + " extraction rules is reachable in its actual confirmation dialog",rules.IsVisible && rules.ActualHeight >= 36
                            && rules.FocusVisualStyle is not null && new ButtonAutomationPeer(rules).IsKeyboardFocusable() && new ButtonAutomationPeer(rules).GetName()==UiText.Get("T_571579b0c6a1"));
                        dialog.Close();
                    }));
                };
                window.ConfirmExtractionOptions();window.PrepareExtractionOptionsForValidation=null;
                await Preview(window.TaskContextMenu, window.ArchiveTasks, mode + "-row-menu");
                Check(mode + " task context menu filters by the current command state", window.TaskContextMenu.Items.OfType<MenuItem>().All(item =>
                    (item.Visibility == Visibility.Visible) == (item.Command?.CanExecute(item.CommandParameter) ?? item.IsEnabled)));
                Check(mode + " task context menu has no orphan separators", CleanSeparators(window.TaskContextMenu));
                foreach (var pair in new[] { ("TextBrush", "SurfaceBrush", 4.5), ("SecondaryBrush", "SurfaceBrush", 4.5), ("ErrorBrush", "SurfaceBrush", 4.5), ("BorderBrush", "SurfaceBrush", 3.0) })
                    Check(mode + " readable palette " + pair.Item1, Contrast(ColorOf((Brush)Application.Current.Resources[pair.Item1]), ColorOf((Brush)Application.Current.Resources[pair.Item2])) >= pair.Item3);
                var menu = window.QueueActionsMenu; menu.Opacity = 0;
                window.QueueActionsAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(); menu.UpdateLayout();
                var header = Descendants(window.QueueMoveUpAction).OfType<ContentPresenter>().First();
                var inset = header.TranslatePoint(new Point(), menu).X;
                Check(mode + " menu uses bounded unified icon space", inset >= 12 && inset <= 64 && (window.QueueMoveUpAction.Icon is not FluentIcon menuIcon || menuIcon.Width == 20));
                Check(mode + " enabled menu retains accessible command name", new MenuItemAutomationPeer(window.QueueMoveUpAction).GetName() == (string)window.QueueMoveUpAction.Header && window.QueueMoveUpAction.IsEnabled);
                menu.Opacity = 1; Render(menu, Path.Combine(samples, mode + "-task-menu.png")); menu.Opacity = 0;
                var selected = model.SelectedTask;
                ((IInvokeProvider)new MenuItemAutomationPeer(window.QueueMoveUpAction).GetPattern(PatternInterface.Invoke)).Invoke(); await Layout();
                Check(mode + " actual menu invoke moves the selected task", ReferenceEquals(model.Tasks[0], selected));
                menu.IsOpen = false; model.SelectedTask = null; await Layout();
                window.QueueActionsAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout();
                Check(mode + " unavailable menu items stay disabled and readable", !window.QueueMoveUpAction.IsEnabled && window.QueueMoveUpAction.Opacity == 1 && ((SolidColorBrush)window.QueueMoveUpAction.Foreground).Color == ((SolidColorBrush)Application.Current.Resources["SecondaryBrush"]).Color);
                menu.Opacity = 1; Render(menu, Path.Combine(samples, mode + "-disabled-task-menu.png")); menu.Opacity = 0; menu.IsOpen = false;
                foreach (var page in new[] { 0, 3, 1 })
                {
                    model.Page = page; await Layout(); Capture($"{mode}-1120-page-{page}");
                    if (page == 1)
                    {
                        model.SelectedPassword = model.PasswordRows.FirstOrDefault(); await Layout();
                        var passwordRow = (DataGridRow)window.PasswordTable.ItemContainerGenerator.ContainerFromItem(model.SelectedPassword);
                        var metadata = Descendants(passwordRow).OfType<TextBlock>().Single(block => block.Text.Contains("example.test",StringComparison.Ordinal));
                        Check(mode + " selected vault metadata follows the readable row color", metadata.IsVisible && metadata.FontSize==13 && ColorOf(metadata.Foreground) == ColorOf(passwordRow.Foreground));
                        Capture($"{mode}-1120-page-1-selected");
                        await Preview(window.PasswordContextMenu, window.PasswordTable, mode + "-vault-row-menu");
                        Check(mode + " vault actions bind to the selected password", ReferenceEquals(window.PasswordEditAction.Command, model.EditPasswordCommand)
                            && window.PasswordEditAction.Visibility == Visibility.Visible && CleanSeparators(window.PasswordContextMenu));
                    }
                    if (page == 3)
                    {
                        var label = Descendants(window.CompressionPage.SfxBox).OfType<TextBlock>().Single(block => block.Text == UiText.Get("T_b0b03f8b05a5"));
                        var level = Descendants(window.CompressionPage.LevelLabel).OfType<FrameworkElement>().First(value=>value is AccessText or TextBlock);
                        var text = new FormattedText(label.Text, System.Globalization.CultureInfo.CurrentUICulture, label.FlowDirection,
                            new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,label.Foreground,VisualTreeHelper.GetDpi(label).PixelsPerDip) { MaxTextWidth=Math.Max(1,label.ActualWidth) };
                        Check(mode + " compression option and level label remain complete", label.ActualWidth > 0 && label.ActualHeight + 1 >= text.Height
                            && level.ActualWidth + 1 >= level.DesiredSize.Width && level.ActualHeight + 1 >= level.DesiredSize.Height
                            && Math.Abs(window.CompressionPage.LevelBox.TranslatePoint(new Point(),window).Y-window.CompressionPage.FormatBox.TranslatePoint(new Point(),window).Y)<1);
                    }
                }
            }
            // Match the usable client height of an 800x600 decorated window, including its status banner.
            window.MinWidth = 800; window.Width = 800; window.MinHeight = 560; window.Height = 560;
            model.Page = 1; model.Banner = "Layout validation"; await Layout();
            Check("compact vault with status banner retains a complete first row", window.PasswordTable.ActualHeight >= 96);
            Capture("compact-vault-banner");
            model.Banner = ""; window.Height = 760; window.MinHeight = 600; window.MinWidth = 1120; window.Width = 1120; await Layout();
            await CheckRowInput(window, model, Check, Layout);
        }
        catch (Exception error) { checks["exception " + error] = false; }
        finally { window.PrepareExtractionOptionsForValidation=null; theme.Apply(SystemParameters.HighContrast); }
        var passed = checks.Count == 124 && checks.Values.All(value => value);
        await File.WriteAllTextAsync(Path.Combine(samples, "report.json"), JsonSerializer.Serialize(new { passed, checks, limitations = "Hidden WPF rendering and UIA do not replace audible Narrator or physical display acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }

    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static double Contrast(Color first, Color second)
    {
        static double L(Color c)
        {
            static double Linear(byte value) { var v = value / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        var a = L(first); var b = L(second); return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static bool CleanSeparators(ContextMenu menu)
    {
        var visible = menu.Items.OfType<UIElement>().Where(item => item.Visibility == Visibility.Visible).ToArray();
        return visible.Length > 0 && visible[0] is MenuItem && visible[^1] is MenuItem
            && !visible.Zip(visible.Skip(1)).Any(pair => pair.First is Separator && pair.Second is Separator);
    }

    private static async Task CheckRowInput(MainWindow window, MainViewModel model, Action<string, bool> check, Func<Task> layout)
    {
        model.Page = 0; model.SelectedTask = model.Tasks[0]; await layout();
        var grid = window.ArchiveTasks;
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(model.Tasks[1]);
        var openBinding = BindingOperations.GetBindingBase(grid, RowActions.OpenCommandProperty)!;
        var deleteBinding = BindingOperations.GetBindingBase(grid, RowActions.DeleteCommandProperty)!;
        var opened = 0; var deleted = 0; var enabled = true;
        grid.SetValue(RowActions.OpenCommandProperty, new InputCommand(() => opened++, () => enabled));
        grid.SetValue(RowActions.DeleteCommandProperty, new InputCommand(() => deleted++, () => enabled));
        void Key(Key key) => grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        void Right(DependencyObject source) => grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = source });
        try
        {
            Right(row); await layout(); check("right-click selects the pointed row before commands", ReferenceEquals(model.SelectedTask, row.Item));
            Key(System.Windows.Input.Key.Enter); check("Enter invokes the row open command once", opened == 1);
            Key(System.Windows.Input.Key.Delete); check("Delete invokes the row remove command once", deleted == 1);
            grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = row });
            check("double-click invokes the row open command once", opened == 2);
            enabled = false; Key(System.Windows.Input.Key.Enter); Key(System.Windows.Input.Key.Delete);
            check("unavailable row commands cannot run from keyboard", opened == 2 && deleted == 1);
            Right(grid); await layout(); check("right-click blank list clears stale selection", model.SelectedTask is null);
            enabled = true; Key(System.Windows.Input.Key.Enter); Key(System.Windows.Input.Key.Delete);
            check("blank list keyboard cannot act on the previous row", opened == 2 && deleted == 1);
            var unavailable = new MenuItem { Command = new InputCommand(() => { }, () => false) };
            var available = new MenuItem { Command = new InputCommand(() => { }, () => true) };
            var popup = new ContextMenu(); popup.Items.Add(new Separator()); popup.Items.Add(unavailable); popup.Items.Add(new Separator()); popup.Items.Add(available); popup.Items.Add(new Separator());
            RowActions.Tidy(popup);
            check("menu filtering removes leading trailing and unavailable groups", unavailable.Visibility == Visibility.Collapsed && available.Visibility == Visibility.Visible && CleanSeparators(popup));
            unavailable.Command = new InputCommand(() => { }, () => true); RowActions.Tidy(popup);
            check("menu filtering restores commands when availability changes", unavailable.Visibility == Visibility.Visible && CleanSeparators(popup));
            model.Page = 3; await layout();
            var sources = Descendants(window.CompressionPage).OfType<DataGrid>().Single();
            sources.ContextMenu.PlacementTarget = sources; await layout();
            check("compression list retains remove keyboard and context entry", RowActions.GetDeleteCommand(sources) == model.RemoveCompressionSourceCommand
                && sources.ContextMenu.Items.OfType<MenuItem>().Any(item => item.Command == model.RemoveCompressionSourceCommand));
        }
        finally
        {
            BindingOperations.SetBinding(grid, RowActions.OpenCommandProperty, openBinding);
            BindingOperations.SetBinding(grid, RowActions.DeleteCommandProperty, deleteBinding);
        }
    }

    private sealed class InputCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public bool CanExecute(object? parameter) => canExecute();
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Render(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
