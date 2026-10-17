using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nomozip.DesignPreview;

internal static class Capture
{
    internal static async Task<bool> RunAsync(MainWindow window, string outputDirectory)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Isolated CI required.");
        var output = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(output);
        var checks = new Dictionary<string, bool>(); var pictures = new List<object>(); var frames = new List<BitmapFrame>(); var motionValues = new List<double>(); string? error = null;
        void Check(string name, bool passed) => checks.Add(name, passed);
        async Task Layout(int delay = 80) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(delay); }
        BitmapSource Render(FrameworkElement visual)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); return bitmap;
        }
        void Screenshot(string file)
        {
            var bitmap = Render(window.Client); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, file))) encoder.Save(stream);
            pictures.Add(new { file, width = bitmap.PixelWidth, height = bitmap.PixelHeight, clientDip = new { width = window.Client.ActualWidth, height = window.Client.ActualHeight }, dark = ((App)Application.Current).Dark, progress = window.Progress.Value, displayedProgress = window.Progress.DisplayedValue,
                motionEnabled = window.MotionEnabled, sha256 = Hash(Path.Combine(output, file)) });
        }
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window.Client).TransformBounds(new Rect(element.RenderSize));
        try
        {
            window.IsHitTestVisible = false; window.MinWidth = 1120; window.Width = 1120; window.Height = 760;
            window.ApplyMotion(true, true); window.SetPose(62, "正在解压"); await Layout(320);
            Check("exact core viewport", window.Client.ActualWidth == 1120 && window.Client.ActualHeight == 760);
            Check("captured client has an opaque themed canvas", window.Client.Background is SolidColorBrush { Color.A: 255 });
            Check("brand asset retains original size", window.Brand.Source is BitmapSource brand && brand.PixelWidth == 256 && brand.PixelHeight == 256);
            Check("same eight-entry ZIP fixture is used", window.Model.Entries.Count == 8 && window.TotalBytes == 799);
            Check("task panel and action stay fully visible", Bounds(window.TaskCard).Bottom <= window.Client.ActualHeight && Bounds(window.ExtractAll).Right <= window.Client.ActualWidth);
            Screenshot("01-option2-light-1120.png");
            ((App)Application.Current).ApplyTheme(true); await Layout(); Screenshot("02-option2-dark-1120.png");
            ((App)Application.Current).ApplyTheme(false); window.MinWidth = 800; window.Width = 800; window.Height = 600; await Layout();
            Check("narrow navigation remains reachable through menu", window.CompactNavigation.IsVisible && !window.NavigationRail.IsVisible);
            Check("narrow task actions stay inside client", Bounds(window.MotionToggle).Right <= window.Client.ActualWidth && Bounds(window.PauseButton).Bottom <= window.Client.ActualHeight);
            Screenshot("03-option2-light-800.png");
            window.Model.Search = "不存在"; await Layout();
            var textBounds = window.Search.GetRectFromCharacterIndex(0);
            Check("search empty result is real and text remains inside input", window.Model.VisibleEntries.IsEmpty && !textBounds.IsEmpty && textBounds.Bottom <= window.Search.ActualHeight);
            Screenshot("04-option2-empty-800.png"); window.Model.Search = "";
            window.MinWidth = 1120; window.Width = 1120; window.Height = 760; await Layout();
            window.ApplyMotion(false, true); window.SetPose(0, "准备解压"); window.ApplyMotion(true, true); window.SetPose(62, "正在解压");
            Check("progress starts a real WPF animation clock", window.Progress.HasAnimatedProperties);
            await Layout(320); Check("progress settles to the reported value", Math.Abs(window.Progress.DisplayedValue - 62) < .01);
            window.ApplyMotion(false, true); window.SetPose(30, "正在解压");
            Check("reduced motion removes animation while retaining progress", !window.Progress.HasAnimatedProperties && window.Progress.DisplayedValue == 30 && window.PercentLabel.Text == "30%");
            window.ApplyMotion(true, true); window.SetPose(50, "正在解压"); window.ApplyMotion(false, true);
            Check("disabling motion interrupts an active animation immediately", !window.Progress.HasAnimatedProperties && window.Progress.DisplayedValue == 50);
            window.SetPose(0, "准备解压"); window.ApplyMotion(true, true);
            var demo = window.RunDemoAsync();
            while (!demo.IsCompleted)
            {
                await Layout(100); motionValues.Add(window.Progress.DisplayedValue);
                var metadata = new BitmapMetadata("gif"); metadata.SetQuery("/grctlext/Delay", (ushort)10); metadata.SetQuery("/grctlext/Disposal", (byte)2);
                frames.Add(BitmapFrame.Create(Render(window.TaskCard), null, metadata, null));
            }
            await demo; await Layout(320); motionValues.Add(window.Progress.DisplayedValue);
            frames.Add(BitmapFrame.Create(Render(window.TaskCard), null, new BitmapMetadata("gif"), null));
            Check("sample extraction finishes only after matching actual bytes", window.ProcessedBytes == window.TotalBytes && window.TaskTitle.Text == "演示完成");
            Check("recorded progress advances monotonically", motionValues.Zip(motionValues.Skip(1), (a, b) => b >= a - .01).All(value => value));
            Check("actual animation includes multiple intermediate visual positions", motionValues.Select(value => Math.Round(value)).Distinct().Count() >= 8 && frames.Count >= 10);
            var gif = new GifBitmapEncoder(); foreach (var frame in frames) gif.Frames.Add(frame);
            using (var stream = File.Create(Path.Combine(output, "05-progress-motion.gif"))) gif.Save(stream);
            Screenshot("06-option2-completed.png");
            window.ApplyMotion(false, true); window.SetPose(62, "正在解压"); ((App)Application.Current).ApplyTheme(false, true); await Layout();
            Check("contrast resources preserve system selection pair", ((SolidColorBrush)Application.Current.Resources["Selected"]).Color == SystemColors.HighlightColor && ((SolidColorBrush)Application.Current.Resources["SelectedText"]).Color == SystemColors.HighlightTextColor);
            Screenshot("07-option2-contrast-resources.png");
        }
        catch (Exception exception) { error = exception.ToString(); }
        var passed = error is null && checks.Values.All(value => value);
        var gifPath = Path.Combine(output, "05-progress-motion.gif");
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
        {
            passed, checks, error, sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"), pictures,
            animation = new { file = Path.GetFileName(gifPath), frames = frames.Count, frameDelayMs = 100, sha256 = File.Exists(gifPath) ? Hash(gifPath) : null, motionValues, recordedFrom = "Actual running WPF task panel, no image composition", preferenceSimulatedOnInIsolatedCi = true, systemClientAreaAnimation = SystemParameters.ClientAreaAnimation },
            font = ((FontFamily)Application.Current.Resources["UiFont"]).Source,
            scope = "x64 option two standalone prototype; real generated ZIP read to memory with intentional pacing; no production extraction/vault or disk writes", userApproved = false, physicalDpiAccepted = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
}
