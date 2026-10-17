using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        var checks = new Dictionary<string, bool>(); var pictures = new List<object>(); var frames = new List<BitmapFrame>(); var motionValues = new List<double>(); string? error = null; object? sorting = null;
        void Check(string name, bool passed) => checks.Add(name, passed);
        async Task Layout(int delay = 80) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(delay); }
        BitmapSource Render(FrameworkElement visual, double scale = 1)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Round(visual.ActualWidth * scale), (int)Math.Round(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
        }
        void Screenshot(string file, double scale = 1)
        {
            var bitmap = Render(window.Client, scale); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, file))) encoder.Save(stream);
            pictures.Add(new { file, width = bitmap.PixelWidth, height = bitmap.PixelHeight, clientDip = new { width = window.Client.ActualWidth, height = window.Client.ActualHeight }, offscreenPixelScale = scale, dark = ((App)Application.Current).Dark, sha256 = Hash(Path.Combine(output, file)) });
        }
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window.Client).TransformBounds(new Rect(element.RenderSize));
        BitmapSource TaskFrame()
        {
            var bounds = Bounds(window.TaskCard);
            return new CroppedBitmap(Render(window.Client), new Int32Rect((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height)));
        }
        bool HasVisibleSurface(BitmapSource bitmap)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var pixel = new byte[4]; converted.CopyPixels(new Int32Rect(12, 12, 1, 1), pixel, 4, 0); return pixel[0] + pixel[1] + pixel[2] > 300;
        }
        try
        {
            window.IsHitTestVisible = false; window.MinWidth = 1152; window.Width = 1152; window.Height = 768; await Layout();
            Check("exact core client viewport", window.Client.ActualWidth == 1152 && window.Client.ActualHeight == 768);
            Check("opaque themed client surface", window.Client.Background is SolidColorBrush { Color.A: 255 });
            Check("original brand asset is loaded", window.Brand.Source is BitmapSource { PixelWidth: 256, PixelHeight: 256 });
            Check("reference layout proportions", Math.Abs(Bounds(window.PreviewPane).X - 801) < 3 && Math.Abs(Bounds(window.FileHeader).Y - 183) < 3 && Math.Abs(window.RailColumn.Width.Value - 186) < .1);
            Check("archive title aligns with reference heading", Math.Abs(Bounds(window.ArchiveTitle).X - 285) < 2);
            Check("twelve real ZIP entries shown in album", window.Model.VisibleEntries.Cast<Entry>().Count() == 12 && window.Model.Entries.Count == 14);
            Check("photo preview comes from selected ZIP payload", window.PreviewImage.Source == window.Model.CurrentEntry?.Thumbnail && window.PreviewImage.Source is BitmapSource { PixelWidth: 1280, PixelHeight: 848 });
            Check("preview and toolbar fit client", Bounds(window.MoreButton).Right <= Bounds(window.PreviewPane).Left && Bounds(window.PreviewPane).Bottom <= window.Client.ActualHeight);
            Check("extract actions use document extraction semantics", window.ExtractAllIcon.Kind == "DocumentArrowDown");
            Check("unsorted headers have no dropdown-like arrows", window.NameSortArrow.Visibility == Visibility.Collapsed && window.SizeSortArrow.Visibility == Visibility.Collapsed);
            Screenshot("01-reference-light-1152.png"); Screenshot("02-reference-aligned-1536.png", 4d / 3);
            ((App)Application.Current).ApplyTheme(true); await Layout(); Screenshot("03-reference-dark-1152.png");
            ((App)Application.Current).ApplyTheme(false); window.MinWidth = 800; window.Width = 800; window.Height = 600; await Layout();
            Check("narrow navigation remains reachable", window.CompactNavigation.IsVisible && !window.NavigationRail.IsVisible);
            Check("narrow preview actions stay fixed inside client", Bounds(window.PreviewActions).Bottom < window.Client.ActualHeight - 49 && window.PreviewActions.IsVisible);
            Check("narrow preview and extraction stay within client", Bounds(window.PreviewPane).Right <= window.Client.ActualWidth && Bounds(window.ExtractAll).Right < Bounds(window.PreviewPane).Left);
            Screenshot("04-reference-narrow-800.png");
            window.ClosePreview(); window.Model.Search = "不存在"; await Layout();
            Check("real empty search result preserves checked file", window.Model.VisibleEntries.IsEmpty && window.Model.Entries.Count(row => row.Checked) == 1);
            Screenshot("05-reference-empty-800.png"); window.Model.Search = "";
            var folder = window.Model.Entries.First(row => row.Name == "旅行照片"); window.Model.Navigate(folder); await Layout();
            Check("directory navigation filters actual nested entry", window.Model.VisibleEntries.Cast<Entry>().Single().Name == "山间湖泊.jpg"); window.Model.Navigate();
            window.SortBy(nameof(Entry.Name));
            Check("active name sort has ascending vector indicator", window.NameSortArrow.IsVisible && !window.SizeSortArrow.IsVisible && window.NameSortArrow.Kind == "ChevronUp");
            sorting = new { collectionCulture = window.Model.VisibleEntries.Culture.Name, threadCulture = System.Globalization.CultureInfo.CurrentCulture.Name, displayedNames = window.Model.VisibleEntries.Cast<Entry>().Select(row => row.Name).ToArray() };
            Check("name sorting follows current UI collection culture", window.Model.VisibleEntries.Cast<Entry>().Select(row => row.Name).SequenceEqual(window.Model.VisibleEntries.Cast<Entry>().OrderBy(row => row.Name, StringComparer.Create(window.Model.VisibleEntries.Culture, false)).Select(row => row.Name)));
            window.SortBy(nameof(Entry.Name)); await Layout();
            Check("second name sort reverses vector indicator", window.NameSortArrow.Kind == "ChevronDown");
            window.SortBy("Bytes.Length"); await Layout();
            Check("size sort moves the only indicator to size column", !window.NameSortArrow.IsVisible && window.SizeSortArrow.IsVisible && window.SizeSortArrow.Kind == "ChevronUp" && window.Model.VisibleEntries.Cast<Entry>().Select(row => row.Bytes.Length).Zip(window.Model.VisibleEntries.Cast<Entry>().Skip(1).Select(row => row.Bytes.Length), (a,b) => a <= b).All(value => value));
            Screenshot("10-size-sorted.png");
            window.Model.VisibleEntries.SortDescriptions.Clear(); window.NameSortArrow.Visibility = window.SizeSortArrow.Visibility = Visibility.Collapsed; window.Model.CurrentEntry = window.Model.Entries.Single(row => row.Name == "封面.jpg");
            window.MinWidth = 1152; window.Width = 1152; window.Height = 768; window.ShowPreview(); await Layout();
            window.IsHitTestVisible = true; IsolatedInput.Activate(window); IsolatedInput.MoveTo(window.NameHeaderButton); await Layout(160);
            Check("real name header hover uses compact target", window.NameHeaderButton.IsMouseOver && window.NameHeaderButton.ActualWidth < 100);
            Screenshot("11-name-header-hover.png");
            try { IsolatedInput.LeftDown(); await Layout(100); Check("real pointer press reaches the header", window.NameHeaderButton.IsPressed); Screenshot("12-name-header-pressed.png"); }
            finally { IsolatedInput.LeftUp(); }
            await Layout(); IsolatedInput.MoveTo(window.ArchiveTitle); window.NameHeaderButton.Focus(); IsolatedInput.Tab(); await Layout(140);
            var focus = (Border)window.SizeHeaderButton.Template.FindName("HeaderFocus", window.SizeHeaderButton);
            var focusBounds = Bounds(focus); var buttonBounds = Bounds(window.SizeHeaderButton);
            Check("tab reaches size header with an inset visible focus", window.SizeHeaderButton.IsKeyboardFocused && focus.IsVisible && focusBounds.Left >= buttonBounds.Left && focusBounds.Top >= buttonBounds.Top && focusBounds.Right <= buttonBounds.Right && focusBounds.Bottom <= buttonBounds.Bottom);
            Check("focus never covers header text padding", window.SizeHeaderButton.Padding.Left >= 8 && focus.Margin.Left == 2);
            Screenshot("13-size-header-keyboard-focus.png"); Keyboard.ClearFocus(); window.IsHitTestVisible = false;
            window.ApplyMotion(false, true); window.SetPose(0, "准备解压"); window.ApplyMotion(true, true); window.SetPose(62, "正在解压");
            Check("progress has real WPF animation clock", window.Progress.HasAnimatedProperties); await Layout(320);
            Check("progress settles at reported value", Math.Abs(window.Progress.DisplayedValue - 62) < .01);
            window.ApplyMotion(false, true); window.SetPose(30, "正在解压");
            await Layout();
            Check("reduced motion immediately shows value", !window.Progress.HasAnimatedProperties && window.Progress.DisplayedValue == 30 && window.PercentLabel.Text == "30%");
            window.SetPose(0, "准备解压"); window.ApplyMotion(true, true); var demo = window.RunDemoAsync();
            while (!demo.IsCompleted)
            {
                await Layout(100); motionValues.Add(window.Progress.DisplayedValue);
                var metadata = new BitmapMetadata("gif"); metadata.SetQuery("/grctlext/Delay", (ushort)10); metadata.SetQuery("/grctlext/Disposal", (byte)2); frames.Add(BitmapFrame.Create(TaskFrame(), null, metadata, null));
                if (frames.Count == 12)
                {
                    Check("extraction occupies task workspace instead of file overlay", window.TaskWorkspace.IsVisible && !window.ArchiveWorkspace.IsVisible && window.TaskCard.Opacity == 1);
                    Check("ring and percentage follow displayed bar position", Math.Abs(window.TaskRing.Value - window.Progress.DisplayedValue) < .01 && window.PercentLabel.Text == $"{window.Progress.DisplayedValue:0}%");
                    Screenshot("06-reference-extracting.png");
                    window.TogglePause(); await Layout(100); var pausedBytes = window.ProcessedBytes; var pausedValue = window.Progress.DisplayedValue; await Layout(220);
                    Check("pause freezes actual bytes and all progress displays", window.IsPaused && window.ProcessedBytes == pausedBytes && window.Progress.DisplayedValue == pausedValue && window.TaskRing.Value == pausedValue);
                    Screenshot("14-task-paused.png"); window.TogglePause();
                }
            }
            await demo; await Layout(320); motionValues.Add(window.Progress.DisplayedValue); frames.Add(BitmapFrame.Create(TaskFrame(), null, new BitmapMetadata("gif"), null));
            Check("extraction completes after comparing actual payload bytes", window.ProcessedBytes == window.TotalBytes && window.TaskTitle.Text == "已完成" && !window.PauseButton.IsEnabled && !window.CancelButton.IsEnabled);
            Check("animation progresses monotonically with multiple positions", motionValues.Zip(motionValues.Skip(1), (a, b) => b >= a - .01).All(value => value) && motionValues.Select(value => Math.Round(value)).Distinct().Count() >= 8);
            Check("animation contains visible task pixels", frames.Count >= 12 && frames.All(HasVisibleSurface));
            var gif = new GifBitmapEncoder(); foreach (var frame in frames) gif.Frames.Add(frame); using (var stream = File.Create(Path.Combine(output, "07-extraction-motion.gif"))) gif.Save(stream);
            Check("completion percentage and bar agree without settling delay", window.PercentLabel.Text == "100%" && window.Progress.DisplayedValue == 100 && window.TaskRing.Value == 100);
            Screenshot("08-reference-completed.png");
            ((App)Application.Current).ApplyTheme(true); await Layout(); Screenshot("15-task-dark.png");
            ((App)Application.Current).ApplyTheme(false); window.MinWidth = 800; window.Width = 800; window.Height = 600; await Layout();
            Check("narrow task actions remain fully in view", Bounds(window.CancelButton).Right <= window.Client.ActualWidth && Bounds(window.PauseButton).Left >= 0);
            Screenshot("16-task-narrow-800.png"); window.MinWidth = 1152; window.Width = 1152; window.Height = 768; window.ShowTasks(false); await Layout();
            ((App)Application.Current).ApplyTheme(false, true); await Layout();
            Check("system contrast preserves selection color pair", ((SolidColorBrush)Application.Current.Resources["Selected"]).Color == SystemColors.HighlightColor && ((SolidColorBrush)Application.Current.Resources["SelectedText"]).Color == SystemColors.HighlightTextColor);
            Check("contrast secondary action text stays visible on selection", ((SolidColorBrush)window.ExtractChecked.Foreground).Color == SystemColors.HighlightTextColor && ((SolidColorBrush)window.ExtractChecked.Background).Color == SystemColors.HighlightColor);
            Screenshot("09-reference-contrast-resources.png"); window.IsHitTestVisible = true; window.SizeHeaderButton.Focus(); await Layout(); Screenshot("17-header-contrast-focus.png"); window.IsHitTestVisible = false;
        }
        catch (Exception exception) { error = exception.ToString(); }
        var passed = error is null && checks.Values.All(value => value); var gifPath = Path.Combine(output, "07-extraction-motion.gif");
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { passed, checks, error, sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"), pictures, sorting,
            animation = new { frames = frames.Count, frameDelayMs = 100, sha256 = File.Exists(gifPath) ? Hash(gifPath) : null, motionValues, systemClientAreaAnimation = SystemParameters.ClientAreaAnimation, preferenceSimulatedOnInIsolatedCi = true, location = "Dedicated task workspace; opaque task row; no fade overlay" },
            font = ((FontFamily)Application.Current.Resources["UiFont"]).Source, reference = "concepts-2026-10-16/02-archive-browser-preview.png", scope = "x64 standalone WPF reference reproduction; generated ZIP, memory-only paced extraction; no production services", userApproved = false, physicalDpiAccepted = false }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
}
