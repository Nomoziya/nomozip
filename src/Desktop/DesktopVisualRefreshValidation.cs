using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
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
        void Capture(string name, double scale = 1)
        {
            window.Opacity = 1;
            DesktopAccessibilityValidation.Render(window, Path.Combine(samples, name + ".png"), scale, clientOnly: true);
            window.Opacity = 0;
        }
        try
        {
            model.Tasks.Clear(); model.EngineVersions.Clear(); model.CompressionSources.Clear(); model.LoadPreview();
            window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
            Check("window and sidebar load the product icon", window.Icon is not null && window.BrandMark.Source is not null);
            var icon = new IconBitmapDecoder(new Uri("pack://application:,,,/ArchivePassword;component/Assets/nomozip.ico"), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check("embedded icon contains nine distinct sizes", icon.Frames.Select(frame => frame.PixelWidth).Order().SequenceEqual(new[] {16,20,24,32,40,48,64,128,256}));
            foreach (var mode in new[] { "light", "dark", "contrast" })
            {
                theme.SetMode(mode == "contrast" ? "light" : mode); theme.Apply(mode == "contrast");
                foreach (var width in new[] { 800, 1120, 1600 })
                {
                    window.Width = width; window.Height = 760; model.Page = 2; await Layout();
                    ((Border)((FrameworkElement)window.VaultIdleChoice.Parent).Parent).BringIntoView(); await Layout();
                    Check($"{mode}/{width} idle field has readable bounded width", window.VaultIdleChoice.ActualWidth >= 220 && window.VaultIdleChoice.ActualWidth <= 280 && Bounds(window.VaultIdleChoice).Right < window.ActualWidth);
                    Check($"{mode}/{width} settings card measure stays bounded", window.PreferencesCards.ActualWidth <= 920 && window.PreferencesScroll.ExtentWidth <= window.PreferencesScroll.ViewportWidth + 1);
                    Capture($"{mode}-{width}-vault-settings", width == 800 ? 2 : 1);
                    ((Border)((FrameworkElement)((FrameworkElement)window.RegisterAssociationsAction.Parent).Parent).Parent).BringIntoView(); await Layout();
                    var buttons = new[] { window.RegisterAssociationsAction, window.RemoveAssociationsAction, window.DefaultAppsAction };
                    var rects = buttons.Select(Bounds).ToArray();
                    Check($"{mode}/{width} association buttons wrap with both gaps", rects.Zip(rects.Skip(1)).All(pair => pair.Second.Top >= pair.First.Bottom + 7 || pair.Second.Left >= pair.First.Right + 11));
                    Check($"{mode}/{width} association actions remain within content", rects.All(rect => rect.Left >= Bounds(window.PreferencesCards).Left && rect.Right <= window.ActualWidth));
                    Capture($"{mode}-{width}-association-settings");
                }
                window.Width = 1120; model.Page = 0; model.SelectedTask = model.Tasks[1]; await Layout();
                var menu = window.QueueActionsMenu; menu.Opacity = 0;
                window.QueueActionsAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(); menu.UpdateLayout();
                var header = Descendants(window.QueueMoveUpAction).OfType<ContentPresenter>().First();
                var inset = header.TranslatePoint(new Point(), menu).X;
                Check(mode + " menu has no reserved icon gutter", inset >= 12 && inset <= 24);
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
                    if (page == 3)
                    {
                        var label = Descendants(window.CompressionPage.SfxBox).OfType<AccessText>().Single();
                        var level = Descendants(window.CompressionPage.LevelLabel).OfType<AccessText>().Single();
                        Check(mode + " compression option and level label remain complete", UiText.LanguageCode != "en" || (label.ActualHeight >= 28 && level.ActualHeight >= 28 && Math.Abs(window.CompressionPage.FormatBox.TranslatePoint(new Point(), window).Y - window.CompressionPage.LevelBox.TranslatePoint(new Point(), window).Y) < 1));
                    }
                }
            }
        }
        catch (Exception error) { checks["exception " + error] = false; }
        finally { theme.Apply(SystemParameters.HighContrast); }
        var passed = checks.Count == 53 && checks.Values.All(value => value);
        await File.WriteAllTextAsync(Path.Combine(samples, "report.json"), JsonSerializer.Serialize(new { passed, checks, limitations = "Hidden WPF rendering and UIA do not replace audible Narrator or physical display acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
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
