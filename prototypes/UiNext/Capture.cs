using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nomozip.DesignPreview;

// This entry point runs only inside an isolated Actions runner. It never injects desktop input.
internal static class Capture
{
    internal static async Task<bool> RunAsync(MainWindow window, string outputDirectory)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Isolated CI required.");
        var directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, bool>(); var captures = new List<object>(); string? failure = null;
        void Check(string name, bool value) => checks.Add(name, value);
        async Task Layout() { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(80); }
        Rect Bounds(FrameworkElement value) => value.TransformToAncestor(window.Client).TransformBounds(new Rect(value.RenderSize));
        void Screenshot(string name, double scale = 1)
        {
            var client = window.Client;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(client.ActualWidth * scale), (int)Math.Ceiling(client.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(client);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(directory, name + ".png"); using (var stream = File.Create(path)) encoder.Save(stream);
            captures.Add(new { file = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                clientDip = new { width = client.ActualWidth, height = client.ActualHeight }, pixelWidth = bitmap.PixelWidth, pixelHeight = bitmap.PixelHeight,
                actualWindowDip = new { width = window.ActualWidth, height = window.ActualHeight }, renderDensity = scale, actualVisualDpi = VisualTreeHelper.GetDpi(client).DpiScaleX,
                method = "RenderTargetBitmap of the running WPF window client, including integrated title bar", nativeDesktopCapture = false, composited = false,
                selected = window.Model.CurrentEntry?.Name, checkedFiles = window.Model.Entries.Where(item => item.Checked).Select(item => item.Name).ToArray(),
                preview = window.Model.PreviewName, search = window.Model.Search, dark = ((App)Application.Current).Dark });
        }
        try
        {
            window.Width = 1120; window.Height = 760; await Layout();
            Check("sample archive contains eight verified entries", window.Model.Entries.Count == 8 && window.Model.ArchiveBytes.Length > 0);
            Check("initial preview is actual selected UTF-8 sample", window.Model.PreviewName == "交付说明.txt" && window.Model.PreviewText.Contains("本次交付包含"));
            Check("checked range starts empty despite highlighted row", !window.Model.HasSelection && !window.ExtractSelected.IsEnabled);
            Check("main action and file list remain inside client", Bounds(window.ExtractAll).Right <= window.Client.ActualWidth && Bounds(window.Files).Bottom <= window.Client.ActualHeight);
            Check("brand image decoded at original aspect", window.Brand.Source is BitmapSource image && image.PixelWidth == 256 && image.PixelHeight == 256);
            Screenshot("01-core-light-1120");
            Screenshot("02-core-light-density-150", 1.5);
            window.Model.Entries.Single(item => item.Name == "配置.json").Checked = true;
            window.Model.Search = "交付说明"; await Layout();
            Check("filter preserves hidden checked range", window.Model.HasSelection && window.Model.Entries.Single(item => item.Name == "配置.json").Checked && window.Model.VisibleEntries.Cast<Entry>().Count() == 1);
            window.Model.Search = ""; window.Model.CurrentEntry = window.Model.Entries.Single(item => item.Name == "更新记录.md"); await Layout();
            Check("highlight does not silently decode a new preview", window.Model.PreviewName == "交付说明.txt");
            window.RequestPreview(); await Layout();
            Check("explicit preview reads requested sample", window.Model.PreviewName == "更新记录.md" && window.Model.PreviewText.Contains("# 更新记录"));
            Check("checked action is available independently of current row", window.ExtractSelected.IsEnabled);
            window.Model.CurrentEntry = window.Model.Entries.Single(item => item.Name == "交付说明.txt"); window.RequestPreview();
            window.Model.Entries.Single(item => item.Name == "配置.json").Checked = false;
            ((App)Application.Current).ApplyTheme(true); await Layout(); Screenshot("03-core-dark-1120");
            ((App)Application.Current).ApplyTheme(false); window.Width = 800; window.Height = 600; window.HidePreview(); await Layout();
            Check("minimum window preserves file list and search", window.Files.ActualWidth > 650 && Bounds(window.Search).Right <= window.Client.ActualWidth);
            Check("minimum window removes optional date column", window.Model.DateColumnWidth.Value == 0);
            Screenshot("04-core-light-800");
            window.RequestPreview(); await Layout();
            Check("minimum window preview overlay remains inside client", window.PreviewPane.ActualWidth == 292 && Bounds(window.PreviewPane).Right <= window.Client.ActualWidth);
            Screenshot("05-core-preview-overlay-800");
            window.HidePreview(); window.Model.Search = "不存在的文件"; await Layout();
            Check("search empty state matches real filtered result", window.Model.VisibleEntries.IsEmpty && window.Model.EmptyVisibility == Visibility.Visible);
            Screenshot("06-core-search-empty-800");
            window.Model.Search = ""; window.Width = 1120; window.Height = 760; window.RequestPreview();
            ((App)Application.Current).ApplyTheme(false, true); await Layout();
            Check("contrast selection pairs use system colors", ((SolidColorBrush)Application.Current.Resources["Selected"]).Color == SystemColors.HighlightColor && ((SolidColorBrush)Application.Current.Resources["SelectedText"]).Color == SystemColors.HighlightTextColor);
            Screenshot("07-core-system-contrast-resources");
            ((App)Application.Current).ApplyTheme(false);
        }
        catch (Exception error) { failure = error.ToString(); }
        var passed = failure is null && checks.Values.All(value => value);
        await File.WriteAllBytesAsync(Path.Combine(directory, "sample.zip"), window.Model.ArchiveBytes);
        await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new
        {
            passed, checks, failure, captures, sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            evidenceScope = "Standalone WPF visual prototype, generated ZIP fixture, core layout and limited prototype interactions only",
            productFeatureAcceptance = false, userDirectionApproved = false, physicalDpiAccepted = false,
            uiFont = ((FontFamily)Application.Current.Resources["UiFont"]).Source,
            previewFont = ((FontFamily)Application.Current.Resources["PreviewFont"]).Source,
            fixtureSha256 = Convert.ToHexString(SHA256.HashData(window.Model.ArchiveBytes)).ToLowerInvariant()
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }
}
