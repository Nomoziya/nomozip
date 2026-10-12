using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

internal static class DesktopAccessibilityValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, MainViewModel model, ThemeResources theme, string root)
    {
        var checks = new Dictionary<string, bool>();
        var failures = new List<string>();
        var directory = Path.Combine(root, "artifacts/accessibility-preview");
        Directory.CreateDirectory(directory);
        void Check(string name, bool passed) { checks[name] = passed; if (!passed) failures.Add(name); }
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        try
        {
            model.Tasks.Clear(); model.CompressionSources.Clear(); model.EngineVersions.Clear();
            model.LoadPreview(); model.SelectedPassword = model.PasswordRows.First(); model.SelectedTask = model.Tasks.First();
            window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
            foreach (var width in new[] { 1120, 800 })
            {
                window.Width = width; window.Height = width == 800 ? 600 : 760;
                if (width == 800)
                {
                    WindowPlacement.FitWorkArea(window, new Rect(0, 0, 800, 500));
                    Check("startup fits simulated small work area", window.Height <= 500 && window.MinHeight <= 500 && window.Width <= 800);
                }
                foreach (var page in new[] { 0, 3, 1, 2 })
                {
                    model.Page = page;
                    await Layout();
                    if (page == 0 && width == 1120)
                    {
                        var selectedRow = Descendants(window).OfType<DataGridRow>().First(row => row.IsSelected && ReferenceEquals(row.Item, model.SelectedTask));
                        var size = Descendants(selectedRow).OfType<TextBlock>().First(block => block.Text == model.SelectedTask.SizeText);
                        Check("selected archive metadata uses row foreground", ColorOf(size.Foreground) == ColorOf(selectedRow.Foreground));
                    }
                    if (page == 0)
                    {
                        var action = (Button)window.FindName("ExtractAction");
                        var settings = (ScrollViewer)window.FindName("SettingsScroll");
                        Check($"extraction width {width} fixed primary action", action.IsVisible && action.IsEnabled
                            && !Ancestors(action).Any(parent => parent is ScrollViewer));
                        Check($"extraction width {width} one form scroll region", width == 1120
                            ? ((ScrollViewer)window.FindName("ExtractionViewport")).VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
                              && settings.ComputedVerticalScrollBarVisibility == Visibility.Collapsed
                            : settings.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled);
                    }
                    var inputs = Descendants(window).OfType<Control>().Where(control => control.TemplatedParent is null && control.IsVisible && control.IsEnabled && control.Focusable
                        && control is Button or TextBox or ComboBox or CheckBox).ToArray();
                    var labels = new List<string>();
                    var named = inputs.Length >= 3;
                    var reachable = true;
                    foreach (var control in inputs)
                    {
                        var peer = UIElementAutomationPeer.CreatePeerForElement(control);
                        var name = peer?.GetName() ?? "";
                        named &= !string.IsNullOrWhiteSpace(name) && peer!.IsKeyboardFocusable() && KeyboardNavigation.GetIsTabStop(control);
                        labels.Add(name);
                        control.BringIntoView(); await Layout();
                        var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
                        reachable &= bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
                            && bounds.Right <= window.ActualWidth + 1 && bounds.Bottom <= window.ActualHeight + 1;
                    }
                    Check($"page {page} width {width} named keyboard controls", named);
                    Check($"page {page} width {width} scroll reachable controls", reachable);
                    // Record only UI labels, never user-entered values or passwords.
                    if (!named) failures.Add("controls: " + string.Join(", ", labels));
                    foreach (var scroll in Descendants(window).OfType<ScrollViewer>()) scroll.ScrollToTop();
                    await Layout();
                    if (page == 0 && width == 800)
                    {
                        var viewport = (ScrollViewer)window.FindName("ExtractionViewport");
                        var bar = Descendants(viewport).OfType<System.Windows.Controls.Primitives.ScrollBar>()
                            .First(value => value.Orientation == Orientation.Vertical && ReferenceEquals(value.TemplatedParent, viewport));
                        var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
                        var before = viewport.VerticalOffset;
                        track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
                        track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, 12) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
                        track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 12, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
                        await Layout();
                        Check("custom scrollbar thumb retains scrolling", viewport.VerticalOffset > before);
                        viewport.ScrollToTop(); await Layout();
                    }
                    foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                        Render(window, Path.Combine(directory, $"page-{page}-{width}-{scale * 100:0}.png"), scale);
                }
            }
            theme.Apply(true); model.Page = 3; model.Banner = "高对比度资源预览：选中导航与提示须清晰可见。";
            await Layout();
            var navigation = (ListBox)window.FindName("Navigation");
            var selected = (ListBoxItem)navigation.ItemContainerGenerator.ContainerFromIndex(1);
            var resources = Application.Current.Resources;
            Check("high contrast selected navigation uses system pair", ColorOf(selected.Foreground) == SystemColors.HighlightTextColor
                && ColorOf(selected.Background) == SystemColors.HighlightColor);
            Check("high contrast content uses system pair", ColorOf((Brush)resources["SurfaceBrush"]) == SystemColors.WindowColor
                && ColorOf((Brush)resources["TextBrush"]) == SystemColors.WindowTextColor);
            Check("high contrast selection and button foregrounds", ColorOf((Brush)resources["SelectionTextBrush"]) == SystemColors.HighlightTextColor
                && ColorOf((Brush)resources["OnAccentBrush"]) == SystemColors.HighlightTextColor);
            Render(window, Path.Combine(directory, "high-contrast-resources-800.png"), 1.0);
            theme.Apply(false); await Layout();
            Check("normal theme restored", ColorOf((Brush)resources["SurfaceBrush"]) == Colors.White
                && ColorOf((Brush)resources["SelectionTextBrush"]) == Color.FromRgb(0x1D, 0x4E, 0xD8));
            var dialog = new PasswordDialog("synthetic archive", false, [], true)
            {
                Owner = window, Height = 420, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false
            };
            WindowPlacement.FitWorkArea(dialog, new Rect(0, 0, 420, 400));
            Check("password dialog fits simulated small work area", dialog.Width <= 420 && dialog.Height <= 400 && dialog.MinHeight <= 400);
            try
            {
                dialog.Show(); await Layout();
                var secret = (PasswordBox)dialog.FindName("SecretBox");
                var visible = (TextBox)dialog.FindName("VisibleSecret");
                var show = (CheckBox)dialog.FindName("ShowPassword");
                var submit = (Button)dialog.FindName("ContinueButton");
                var error = (TextBlock)dialog.FindName("ErrorLabel");
                var secretPeer = UIElementAutomationPeer.CreatePeerForElement(secret)!;
                Check("masked password accessibility", secretPeer.IsPassword() && secretPeer.GetName() == "密码" && secretPeer.IsKeyboardFocusable());
                show.IsChecked = true; await Layout();
                var visiblePeer = UIElementAutomationPeer.CreatePeerForElement(visible)!;
                Check("visible password explicit label", !visiblePeer.IsPassword() && visiblePeer.GetName().Contains("明文") && visiblePeer.IsKeyboardFocusable());
                submit.Focus();
                ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(submit)!.GetPattern(PatternInterface.Invoke)).Invoke();
                await Layout();
                Check("empty visible password returns logical focus", ReferenceEquals(FocusManager.GetFocusedElement(dialog), visible)
                    && dialog.Response is null && error.Text == "请输入密码。");
                Check("password error live region", UIElementAutomationPeer.CreatePeerForElement(error)!.GetLiveSetting() == AutomationLiveSetting.Polite);
                show.IsChecked = false; await Layout();
                Check("hide password restores masked input", secret.IsVisible && !visible.IsVisible && visible.Text.Length == 0);
                ((CheckBox)dialog.FindName("SavePassword")).IsChecked = true;
                var name = (TextBox)dialog.FindName("NameBox"); name.BringIntoView(); await Layout();
                Check("password save name reachable at minimum height", name.IsVisible && name.IsEnabled
                    && UIElementAutomationPeer.CreatePeerForElement(name)!.GetName() == "密码名称");
                Render(dialog, Path.Combine(directory, "password-dialog-small-workarea.png"), 1.0);
            }
            finally { dialog.Close(); }
        }
        finally
        {
            theme.Apply(SystemParameters.HighContrast);
            var passed = checks.Count == 34 && checks.Values.All(value => value);
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts/accessibility-validation.json"), JsonSerializer.Serialize(new
            {
                passed, total = checks.Count, checks, failures, syntheticData = true,
                renderScales = new[] { 100, 125, 150, 200 }, layoutSizes = new[] { "1120x760", "800x500" }, highContrastSystemEnabled = SystemParameters.HighContrast,
                scope = "UI Automation semantics, logical focus, scroll reachability and rendering with simulated contrast resources/pixel density",
                humanKeyboardNarratorExplorerAndMonitorDpiAccepted = false
            }));
        }
        return checks.Count == 34 && checks.Values.All(value => value);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        while (VisualTreeHelper.GetParent(child) is { } parent) { yield return parent; child = parent; }
    }
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static void Render(Window window, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
