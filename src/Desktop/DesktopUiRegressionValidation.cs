using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

internal static class DesktopUiRegressionValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, MainViewModel model, string root)
    {
        var checks = new Dictionary<string, bool>();
        var output = Path.Combine(root, "artifacts/accessibility-preview");
        Directory.CreateDirectory(output);
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        model.Page = 0; window.Width = 1120; window.Height = 760;
        await Layout();
        var table = (DataGrid)window.FindName("ArchiveTasks");
        var tablePeer = (DataGridAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(table)!;
        var rows = tablePeer.GetChildren().OfType<DataGridItemAutomationPeer>().ToArray();
        foreach (var task in model.Tasks)
        {
            table.ScrollIntoView(task); await Layout();
            var peer = rows.Single(row => ReferenceEquals(row.Item, task));
            checks[$"UIA row announces {Path.GetExtension(task.Name)} filename size status"] = peer.GetName().Contains(task.Name)
                && peer.GetName().Contains(task.SizeText) && peer.GetName().Contains(task.Status);
            var cells = peer.GetChildren().OfType<DataGridCellItemAutomationPeer>().ToArray();
            checks[$"UIA cells announce {Path.GetExtension(task.Name)} filename and column"] = cells.Length == 2
                && cells.All(cell => cell.GetName().Contains(task.Name))
                && cells.Any(cell => cell.GetName().Contains("文件")) && cells.Any(cell => cell.GetName().Contains("状态"));
        }
        var selected = model.Tasks.First(); var previousStatus = selected.Status;
        selected.Status = "已取消"; await Layout();
        checks["UIA task status updates without replacing row"] = rows.First(row => ReferenceEquals(row.Item, selected)).GetName().Contains("已取消");
        selected.Status = previousStatus;

        // Ask WPF to create its real focus adorner without sending keys to the user's desktop.
        // This validates the visual that keyboard navigation uses, not a human Narrator session.
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var always = typeof(KeyboardNavigation).GetProperty("AlwaysShowFocusVisual", flags)!;
        var previousAlways = always.GetValue(null);
        var navigation = typeof(KeyboardNavigation).GetProperty("Current", flags)!.GetValue(null);
        var show = typeof(KeyboardNavigation).GetMethod("ShowFocusVisual", BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(DependencyObject)], null)!;
        try
        {
            always.SetValue(null, true);
            var nav = (ListBox)window.FindName("Navigation");
            var controls = new Dictionary<string, Control>
            {
                ["navigation"] = (ListBoxItem)nav.ItemContainerGenerator.ContainerFromIndex(0),
                ["extract-action"] = window.ExtractAction
            };
            foreach (var (name, control) in controls)
            {
                show.Invoke(navigation, [control]); await Layout();
                var layer = AdornerLayer.GetAdornerLayer(control)!;
                var adorner = layer.GetAdorners(control)!.Single();
                var border = Descendants(adorner).OfType<Border>().Single();
                var focusBounds = border.TransformToAncestor(window).TransformBounds(new Rect(border.RenderSize));
                var controlBounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
                checks[$"{name} focus stays inside control"] = focusBounds.Left >= controlBounds.Left && focusBounds.Top >= controlBounds.Top
                    && focusBounds.Right <= controlBounds.Right && focusBounds.Bottom <= controlBounds.Bottom;
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale),
                        (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var color = ((SolidColorBrush)border.BorderBrush).Color;
                    bool HasStroke(double x, double y)
                    {
                        var left = Math.Max(0, (int)Math.Floor(x * scale) - 2);
                        var top = Math.Max(0, (int)Math.Floor(y * scale) - 2);
                        var width = Math.Min(5, bitmap.PixelWidth - left); var height = Math.Min(5, bitmap.PixelHeight - top);
                        if (width <= 0 || height <= 0) return false;
                        var bytes = new byte[width * height * 4];
                        bitmap.CopyPixels(new Int32Rect(left, top, width, height), bytes, width * 4, 0);
                        return Enumerable.Range(0, width * height).Any(index => bytes[index * 4] == color.B
                            && bytes[index * 4 + 1] == color.G && bytes[index * 4 + 2] == color.R && bytes[index * 4 + 3] == 255);
                    }
                    checks[$"{name} complete focus stroke at {scale * 100:0}%"] = HasStroke(focusBounds.Left + 1, focusBounds.Top + focusBounds.Height / 2)
                        && HasStroke(focusBounds.Right - 1, focusBounds.Top + focusBounds.Height / 2)
                        && HasStroke(focusBounds.Left + focusBounds.Width / 2, focusBounds.Top + 1)
                        && HasStroke(focusBounds.Left + focusBounds.Width / 2, focusBounds.Bottom - 1);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"focus-{name}-{scale * 100:0}.png")); encoder.Save(stream);
                }
            }
        }
        finally { always.SetValue(null, previousAlways); }
        var passed = checks.Count == 17 && checks.Values.All(value => value);
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts/ui-regression-validation.json"), JsonSerializer.Serialize(new
        { passed, total = checks.Count, checks, syntheticData = true, scope = "UIA table providers and WPF focus adorner pixels; density simulation, not physical DPI or Narrator speech" }));
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
}
