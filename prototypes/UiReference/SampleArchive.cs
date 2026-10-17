using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nomozip.DesignPreview;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class Entry : Observable
{
    private bool selected;
    public required string Path { get; init; }
    public required byte[] Bytes { get; init; }
    public required DateTimeOffset Date { get; init; }
    public bool Directory { get; init; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('/'));
    public bool IsImage => Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
    public BitmapSource? Thumbnail { get; init; }
    public Visibility ThumbnailVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IconVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;
    public string Icon => Directory ? "Folder" : "DocumentText";
    public Brush IconBrush => Directory ? Brushes.DarkGoldenrod : Brushes.SlateGray;
    public string TypeLabel => Directory ? "文件夹" : IsImage ? "JPG 图片" : Name.EndsWith(".pdf") ? "PDF 文档" : Name.EndsWith(".csv") ? "CSV 表格" : "文本文档";
    public string SizeLabel => Directory ? "—" : FormatSize(Bytes.LongLength);
    public string Modified => Date.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    public bool Checked { get => selected; set { if (selected == value) return; selected = value; Notify(); } }
    public static string FormatSize(long size) => size < 1024 ? $"{size} B" : size < 1024 * 1024 ? $"{size / 1024d:0.0} KB" : $"{size / 1048576d:0.0} MB";
}
public sealed class ArchiveModel : Observable
{
    private string search = "";
    private Entry? current;
    private string folder = "相册/";
    public ObservableCollection<Entry> Entries { get; } = [];
    public ICollectionView VisibleEntries { get; }
    public byte[] ArchiveBytes { get; }
    public ArchiveModel()
    {
        var photo = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/moraine-lake.jpg"));
        using var resource = photo.Stream; using var buffer = new MemoryStream(); resource.CopyTo(buffer); var jpg = buffer.ToArray();
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
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true, Encoding.UTF8))
            foreach (var (name, bytes) in files)
            {
                var item = zip.CreateEntry(name, CompressionLevel.Fastest); item.LastWriteTime = new DateTimeOffset(2024, 4, 26, 20, 14, 0, TimeSpan.Zero);
                using var output = item.Open(); output.Write(bytes);
            }
        ArchiveBytes = memory.ToArray();
        using (var zip = new ZipArchive(new MemoryStream(ArchiveBytes), ZipArchiveMode.Read))
            foreach (var item in zip.Entries)
            {
                using var input = item.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes);
                var payload = bytes.ToArray();
                var row = new Entry { Path = item.FullName, Directory = item.FullName.EndsWith('/'), Bytes = payload, Date = item.LastWriteTime, Thumbnail = item.Name.EndsWith(".jpg") ? Decode(payload) : null };
                row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Entry.Checked)) RefreshSelection(); }; Entries.Add(row);
            }
        VisibleEntries = CollectionViewSource.GetDefaultView(Entries);
        VisibleEntries.Filter = obj => obj is Entry row && row.Path.StartsWith(folder, StringComparison.Ordinal) && !row.Path[folder.Length..].TrimEnd('/').Contains('/') && row.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase);
        current = Entries.Single(row => row.Name == "封面.jpg"); current.Checked = true;
    }
    public static BitmapSource Decode(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = input; image.EndInit(); image.Freeze(); return image;
    }
    private static byte[] MinimalPdf()
    {
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", "<< /Length 50 >>\nstream\nBT /F1 16 Tf 30 100 Td (Sample reservation) Tj ET\nendstream" };
        var text = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(text.Length); text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = text.Length; text.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) text.Append($"{offset:0000000000} 00000 n \n");
        text.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return Encoding.ASCII.GetBytes(text.ToString());
    }
    public string Search { get => search; set { if (search == value) return; search = value; VisibleEntries.Refresh(); Notify(); RefreshSelection(); Notify(nameof(EmptyVisibility)); } }
    public Entry? CurrentEntry { get => current; set { current = value; Notify(); } }
    public bool HasSelection => Entries.Any(row => row.Checked);
    public bool AllSelected { get => VisibleEntries.Cast<Entry>().Any() && VisibleEntries.Cast<Entry>().All(row => row.Checked); set { foreach (var row in VisibleEntries.Cast<Entry>().ToArray()) row.Checked = value; RefreshSelection(); } }
    public string Summary => $"{VisibleEntries.Cast<Entry>().Count()} 项 · 已选择 {Entries.Count(row => row.Checked)} 项";
    public string FolderLabel => folder.TrimEnd('/').Split('/').Last();
    public Visibility EmptyVisibility => VisibleEntries.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public void Navigate(Entry? entry = null) { folder = entry is { Directory: true } ? entry.Path : "相册/"; Search = ""; VisibleEntries.Refresh(); CurrentEntry = VisibleEntries.Cast<Entry>().FirstOrDefault(); Notify(nameof(FolderLabel)); Notify(nameof(EmptyVisibility)); RefreshSelection(); }
    public void Sort(string property, ListSortDirection direction) { VisibleEntries.SortDescriptions.Clear(); VisibleEntries.SortDescriptions.Add(new(property, direction)); }
    private void RefreshSelection() { Notify(nameof(HasSelection)); Notify(nameof(AllSelected)); Notify(nameof(Summary)); }
}
