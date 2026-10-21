using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopReferenceVisualValidation
{
    internal static async Task RunAsync(MainWindow window, ThemeResources theme, string root, string folder, Action<string, bool> check)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Isolated CI only.");
        var data = Path.Combine(folder, "synthetic", "reference-baseline"); Directory.CreateDirectory(data);
        var downloads=Path.Combine(data,UiText.LanguageCode=="en"?"Downloads":"下载");Directory.CreateDirectory(downloads);
        var archive = Path.Combine(downloads, "图片备份.zip");
        CreateFixture(archive, Path.Combine(root, "prototypes", "UiReference", "Assets", "moraine-lake.jpg"));
        var oldContext = window.DataContext;
        var vault = new DpapiPasswordVault(Path.Combine(data, "synthetic-vault.apv"));
        var model = new MainViewModel(AppContext.BaseDirectory, data, vault, new WindowDialogs(window)); await model.InitializeAsync();
        window.DataContext = model; window.MinWidth = 1152; window.Width = 1152; window.Height = 768;
        theme.SetMode("light"); theme.Apply(false);
        async Task Layout() { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(80); }
        async Task Until(Func<bool> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Reference comparison transition timed out."); await Task.Delay(30); await Layout(); }
        }
        var screenshots = new List<object>();
        void Capture(string file, string state)
        {
            window.Opacity = 1; DesktopAccessibilityValidation.Render(window, Path.Combine(folder, file), 1, clientOnly: true); window.Opacity = 0;
            screenshots.Add(new { file, state, language = UiText.LanguageCode, theme = theme.Mode, windowDip = new { width = window.ActualWidth, height = window.ActualHeight }, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, file)))).ToLowerInvariant() });
        }
        var sampleTask = new ArchiveTaskRow(archive) { Running = true, Status = UiText.Get("Redesign.SampleExtracting"), Percent = 10 };
        model.Tasks.Add(sampleTask); model.SelectedTask = sampleTask; model.Page = 0; await Layout();
        window.ArchiveTasks.ScrollIntoView(sampleTask); await Layout();
        check("actual compact task row and header match the adopted 64 and 40 DIP metrics", window.ArchiveTasks.RowHeight == 64 && window.ArchiveTasks.ColumnHeaderHeight == 40);
        model.TaskDensityIndex=1;await Layout();check("comfortable task density retains 88 DIP rows",window.ArchiveTasks.RowHeight==88);
        model.TaskDensityIndex=0;await Layout();
        var taskView = Descendants(window.ArchiveTasks).OfType<TaskProgressView>().Single(view => ReferenceEquals(view.DataContext, sampleTask));
        var track = Descendants(taskView).OfType<TaskProgressTrack>().Single(); var ring = Descendants(taskView).OfType<TaskProgressRing>().Single(); var percent = Descendants(taskView).OfType<TextBlock>().Single(text => text.Name == "Percentage");
        track.SetAnimationPreferenceForValidation(true); sampleTask.Percent = 70; await Task.Delay(70); await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.Render);
        check("actual task progress uses an intermediate animated position", track.DisplayedValue > 10 && track.DisplayedValue < 70);
        check("task circle and percentage share the animated bar value", Math.Abs(ring.Value-track.DisplayedValue)<.01 && percent.Text==$"{track.DisplayedValue:0}%");
        track.IsPaused = true; var frozen = track.DisplayedValue; sampleTask.Percent = 80; await Task.Delay(280);
        check("paused task view freezes all displayed progress", Math.Abs(track.DisplayedValue-frozen)<.01 && Math.Abs(ring.Value-frozen)<.01 && percent.Text==$"{frozen:0}%");
        track.IsPaused = false; await Task.Delay(280); check("resumed task view catches up to actual progress", Math.Abs(track.DisplayedValue-80)<.01);
        var frames = new List<BitmapFrame>(); var values = new List<double>();
        sampleTask.Percent = 0; await Layout();
        for (var i=0;i<36;i++)
        {
            sampleTask.Percent = (i+1)*100d/36; await Task.Delay(75); await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.Render);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
            window.Opacity=1;bitmap.Render(window);window.Opacity=0;
            var bounds=taskView.TransformToAncestor(window).TransformBounds(new Rect(taskView.RenderSize));
            var crop=new CroppedBitmap(bitmap,new Int32Rect((int)Math.Round(bounds.X),(int)Math.Round(bounds.Y),(int)Math.Floor(bounds.Width),(int)Math.Floor(bounds.Height)));
            var metadata=new BitmapMetadata("gif");metadata.SetQuery("/grctlext/Delay",(ushort)8);metadata.SetQuery("/grctlext/Disposal",(byte)2);frames.Add(BitmapFrame.Create(crop,null,metadata,null)); values.Add(track.DisplayedValue);
        }
        var gif=new GifBitmapEncoder();foreach(var frame in frames)gif.Frames.Add(frame);
        using(var stream=new MemoryStream()) {gif.Save(stream);await File.WriteAllBytesAsync(Path.Combine(folder,"reference-task-motion.gif"),EncodeFrameTiming(stream.ToArray(),8));}
        var exported=new GifBitmapDecoder(new Uri(Path.Combine(folder,"reference-task-motion.gif")),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        check("exported task GIF has thirty six frames with actual eighty millisecond delays",exported.Frames.Count==36 && exported.Frames.All(frame=>frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/grctlext/Delay") is ushort delay && delay==8));
        void CurrentTaskVisuals()
        {
            taskView=Descendants(window.ArchiveTasks).OfType<TaskProgressView>().Single(view=>ReferenceEquals(view.DataContext,sampleTask));
            track=Descendants(taskView).OfType<TaskProgressTrack>().Single();ring=Descendants(taskView).OfType<TaskProgressRing>().Single();percent=Descendants(taskView).OfType<TextBlock>().Single(text=>text.Name=="Percentage");
        }
        sampleTask.Completed = true; sampleTask.Running = false; await Layout(); CurrentTaskVisuals(); check("completed task displays exact one hundred immediately", track.DisplayedValue == 100 && ring.Value == 100 && percent.Text == "100%");
        sampleTask.Completed=false;sampleTask.Running=true;sampleTask.Percent=37;await Layout();CurrentTaskVisuals();track.SetAnimationPreferenceForValidation(false);sampleTask.Percent=51;await Layout();check("reduced motion snaps task progress without interpolation",track.DisplayedValue==51);track.SetAnimationPreferenceForValidation(null);
        sampleTask.IsIndeterminate=true;await Layout();check("unknown total does not display a percentage or determinate ring",!percent.IsVisible && !ring.IsVisible && !track.IsVisible);model.Tasks.Clear();
        await File.WriteAllTextAsync(Path.Combine(folder,"reference-task-motion.json"),JsonSerializer.Serialize(new {scope="Actual product task control and ArchiveTaskRow binding; controlled progress/pause UI fixture, not engine throughput or a queue-pause integration test",simulationForAnimationEnabledInIsolatedCi=true,frames=frames.Count,frameDelayMs=80,values,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,"reference-task-motion.gif")))).ToLowerInvariant()}));
        var opening = model.OpenWorkspaceArchiveAsync([archive]);
        try
        {
            await Until(() => window.ArchiveHost.Content is FrameworkElement);
            var content = (FrameworkElement)window.ArchiveHost.Content; var contents = (ArchiveContentsViewModel)content.DataContext;
            contents.CurrentRow = contents.VisibleRows.Single(row => row.Entry.Directory && row.DisplayName == "相册"); contents.OpenCurrentEntryCommand.Execute(null); await Layout();
            check("reference comparison uses twelve actual album entries", contents.VisibleRows.Count == 12);
            var cover = contents.VisibleRows.Single(row => row.DisplayName == "封面.jpg"); cover.Selected = true; contents.CurrentRow = cover; contents.PreviewCommand.Execute(null); await Until(() => !contents.IsWorking && window.PreviewHost.Content is FrameworkElement); await Layout();
            check("reference image decoded through the actual preview service", ((ArchivePreviewViewModel)((FrameworkElement)window.PreviewHost.Content).DataContext).PixelDimensions.Contains("1280") && ((ArchivePreviewViewModel)((FrameworkElement)window.PreviewHost.Content).DataContext).PixelDimensions.Contains("848"));
            var table = Descendants(content).OfType<DataGrid>().Single(value => value.Name == "EntryTable");
            var view = CollectionViewSource.GetDefaultView(table.ItemsSource); view.SortDescriptions.Clear();
            // CI-only visual fixture ordering matches the adopted original screenshot; normal product sorting is not changed.
            string[] approvedOrder = ["旅行照片","家庭相册","封面.jpg","说明.txt","照片001.jpg","照片002.jpg","照片003.jpg","照片004.jpg","旅行路线.jpg","合照.jpg","酒店预订信息.pdf","费用清单.csv"];
            for(var index=0;index<approvedOrder.Length;index++) contents.VisibleRows.Move(contents.VisibleRows.IndexOf(contents.VisibleRows.Single(row=>row.DisplayName==approvedOrder[index])),index);
            foreach(var column in table.Columns)column.SortDirection=null;
            await Layout();
            Capture("reference-file-browser-aligned.png", "same 1152x768, ZIP payload, selected cover and CI-arranged collection order as core-967175a/01-reference-light-1152.png; product default ordering unchanged");
            view.SortDescriptions.Add(new SortDescription("Entry.Size", ListSortDirection.Ascending)); table.Columns[2].SortDirection = ListSortDirection.Ascending;
            await Layout(); check("reference viewport is exact and preview width matches approved baseline", window.ActualWidth == 1152 && window.ActualHeight == 768 && window.PreviewColumn.Width.Value == 350);
            Capture("reference-file-browser-light.png", "1152x768 / same album / actual size ascending; core 10-size-sorted is 800x600 without preview, so this is not an identical-state pair");
            theme.SetMode("dark"); await Layout(); Capture("reference-file-browser-dark.png", "same album / selected cover / dark");
            theme.SetMode("light"); window.MinWidth = 800; window.Width = 800; window.Height = 600; await Layout();
            check("minimum window keeps preview, extraction and compact navigation reachable", table.Columns[1].ActualWidth >= 320 && window.ArchivePreviewPane.IsVisible && window.ReferenceCompactMenu.IsVisible && window.PreviewColumn.Width.Value == 300);
            Capture("reference-file-browser-narrow.png", "800 by 600 / same selected cover");
        }
        finally
        {
            window.CloseArchiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await opening;
            window.DataContext = oldContext; window.MinWidth = 1152; window.Width = 1152; window.Height = 768; theme.SetMode("light");
        }
        await File.WriteAllTextAsync(Path.Combine(folder, "reference-comparison.json"), JsonSerializer.Serialize(new { sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"), approvedCore = "967175a4274835fbfefeb75a6b30718051dfa41c", fixture = "Same names, bytes and ZIP dates as the approved standalone sample; photo attributed in prototypes/UiReference/Assets/ATTRIBUTION.md", screenshots, physicalDpiAccepted = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    // WPF's GIF encoder drops per-frame metadata. Set only the graphic-control timing and disposal fields;
    // preserve the actual rendered frames, colour tables, image descriptors and compressed pixel payloads.
    private static byte[] EncodeFrameTiming(byte[] encoded,ushort delay)
    {
        var offset=13;
        if((encoded[10]&128)!=0)offset+=3*(1<<((encoded[10]&7)+1));
        while(offset<encoded.Length)
        {
            var tag=encoded[offset++];
            if(tag==0x3b)break;
            if(tag==0x21)
            {
                var label=encoded[offset++];
                if(label==0xf9)
                {
                    if(encoded[offset]!=4)throw new InvalidDataException("Unexpected GIF graphic control block.");
                    encoded[offset+1]=(byte)((encoded[offset+1]&~0x1c)|8);
                    encoded[offset+2]=(byte)(delay&255);encoded[offset+3]=(byte)(delay>>8);
                }
            }
            else if(tag==0x2c)
            {
                var packed=encoded[offset+8];offset+=9;
                if((packed&128)!=0)offset+=3*(1<<((packed&7)+1));
                offset++; // LZW minimum code size.
            }
            else throw new InvalidDataException("Unexpected GIF block.");
            while(encoded[offset]!=0)offset+=encoded[offset]+1;
            offset++;
        }
        return encoded;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void CreateFixture(string path, string photoPath)
    {
        var jpg = File.ReadAllBytes(photoPath);
        var files = new Dictionary<string, byte[]>
        {
            ["相册/旅行照片/"] = [], ["相册/家庭相册/"] = [],
            ["相册/封面.jpg"] = jpg, ["相册/说明.txt"] = Encoding.UTF8.GetBytes("图片备份\n\n这是一个独立视觉原型使用的示例压缩包。\n照片由 Brett Hamm 拍摄，CC BY-SA 4.0。\n\n解压演示只将样本读取到内存，不写入磁盘。\n"),
            ["相册/照片001.jpg"] = jpg, ["相册/照片002.jpg"] = jpg, ["相册/照片003.jpg"] = jpg,
            ["相册/照片004.jpg"] = jpg, ["相册/旅行路线.jpg"] = jpg, ["相册/合照.jpg"] = jpg,
            ["相册/酒店预订信息.pdf"] = MinimalPdf(), ["相册/费用清单.csv"] = Encoding.UTF8.GetBytes("项目,金额\n住宿,1200\n交通,600\n"),
            ["相册/旅行照片/山间湖泊.jpg"] = jpg,
            ["相册/家庭相册/使用说明.txt"] = Encoding.UTF8.GetBytes("这里是示例子目录。使用顶部相册按钮返回。")
        };
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in files) { var entry = zip.CreateEntry(name, CompressionLevel.Fastest); entry.LastWriteTime = new DateTimeOffset(2024, 4, 26, 20, 14, 0, TimeSpan.Zero); using var output = entry.Open(); output.Write(bytes); }
    }
    private static byte[] MinimalPdf()
    {
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", "<< /Length 50 >>\nstream\nBT /F1 16 Tf 30 100 Td (Sample reservation) Tj ET\nendstream" };
        var text = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(text.Length); text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = text.Length; text.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) text.Append($"{offset:0000000000} 00000 n \n");
        text.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return Encoding.ASCII.GetBytes(text.ToString());
    }
}
