using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace Nomozip.DesignPreview;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class Entry : Observable
{
    private bool selected;
    public required string Name { get; init; }
    public required byte[] Bytes { get; init; }
    public required DateTimeOffset Date { get; init; }
    public bool Directory { get; init; }
    public string Icon => Directory ? "Folder" : "DocumentText";
    public string CheckLabel => "选择 " + Name;
    public string SizeLabel => Directory ? "—" : FormatSize(Bytes.LongLength);
    public string Modified => Date.ToString("yyyy/M/d HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    public bool Checked { get => selected; set { if (selected == value) return; selected = value; Notify(); } }
    public static string FormatSize(long size) => size < 1024 ? $"{size} B" : $"{size / 1024.0:0.0} KB";
}

public sealed class ArchiveModel : Observable
{
    private string query = "";
    private Entry? current;
    private Entry preview = null!;
    private bool compact;
    public ObservableCollection<Entry> Entries { get; } = [];
    public ICollectionView VisibleEntries { get; }
    public byte[] ArchiveBytes { get; }
    public ArchiveModel()
    {
        // Only generated sample bytes are read. No product services, user files or vaults are loaded.
        var documents = new Dictionary<string, string>
        {
            ["资料/"] = "", ["图片/"] = "",
            ["交付说明.txt"] = "项目交付说明\n\n本次交付包含：\n界面源文件、使用说明与配置。\n\n使用前请核对文件清单，\n并将文件解压到独立文件夹。\n\n版本：1.0\n更新：2026 年 10 月 17 日\n\n如需调整配置，请先保留原文件。\n",
            ["更新记录.md"] = "# 更新记录\n\n## 1.0\n- 整理交付目录。\n- 补齐使用说明。\n- 增加示例配置。\n",
            ["项目清单.csv"] = "名称,类型,说明\n资料,文件夹,项目文档\n图片,文件夹,图片素材\n交付说明.txt,文本,交付使用说明\n",
            ["配置.json"] = "{\n  \"version\": \"1.0\",\n  \"language\": \"zh-CN\",\n  \"output\": \"./交付文件\"\n}\n",
            ["安装说明与常见问题（请在开始使用前完整阅读此文件）.txt"] = "使用说明\n\n先解压全部文件，再按交付说明使用。\n出现同名文件时，请确认目标位置并保留需要的原文件。\n",
            ["校验说明.txt"] = "校验说明\n\n可使用文件 SHA-256 工具计算并比较摘要。\n请从可信来源获取预期摘要。\n"
        };
        var timestamp = new DateTimeOffset(2026, 10, 17, 10, 30, 0, TimeSpan.Zero);
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true, Encoding.UTF8))
            foreach (var (name, text) in documents)
            {
                var item = zip.CreateEntry(name, CompressionLevel.Optimal); item.LastWriteTime = timestamp;
                using var output = item.Open(); output.Write(Encoding.UTF8.GetBytes(text));
            }
        ArchiveBytes = memory.ToArray();
        using (var zip = new ZipArchive(new MemoryStream(ArchiveBytes), ZipArchiveMode.Read, false, Encoding.UTF8))
            foreach (var item in zip.Entries)
            {
                using var input = item.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes);
                var row = new Entry { Name = item.FullName.TrimEnd('/'), Directory = item.FullName.EndsWith('/'), Bytes = bytes.ToArray(), Date = item.LastWriteTime };
                row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Entry.Checked)) SelectionChanged(); };
                Entries.Add(row);
            }
        VisibleEntries = CollectionViewSource.GetDefaultView(Entries);
        VisibleEntries.Filter = item => ((Entry)item).Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase);
        current = Entries.Single(item => item.Name == "交付说明.txt"); preview = current;
    }
    public string ArchiveSummary => $"ZIP 压缩包  ·  {Entry.FormatSize(ArchiveBytes.LongLength)}  ·  {Entries.Count} 个项目";
    public string TotalSummary => $"{Entries.Count} 个项目  ·  解压后 {Entry.FormatSize(Entries.Sum(item => item.Bytes.LongLength))}";
    public string SelectionSummary => $"{VisibleEntries.Cast<Entry>().Count()} 个项目" + (HasSelection ? $"  ·  已选 {Entries.Count(item => item.Checked)} 个" : "  ·  未选择文件");
    public bool HasSelection => Entries.Any(item => item.Checked);
    public string Search { get => query; set { if (query == value) return; query = value; VisibleEntries.Refresh(); Notify(); Notify(nameof(EmptyVisibility)); SelectionChanged(); } }
    public Entry? CurrentEntry { get => current; set { current = value; Notify(); } }
    public bool AllSelected
    {
        get => VisibleEntries.Cast<Entry>().Any() && VisibleEntries.Cast<Entry>().All(item => item.Checked);
        set { foreach (var item in VisibleEntries.Cast<Entry>().ToArray()) item.Checked = value; SelectionChanged(); }
    }
    public Visibility EmptyVisibility => VisibleEntries.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public GridLength DateColumnWidth => compact ? new(0) : new(132);
    public string PreviewName => preview.Name;
    public string PreviewMetadata => $"文本文件  ·  {preview.SizeLabel}  ·  UTF-8";
    public string PreviewText => Encoding.UTF8.GetString(preview.Bytes);
    public void RequestPreview()
    {
        if (current is null || current.Directory) return;
        preview = current; Notify(nameof(PreviewName)); Notify(nameof(PreviewMetadata)); Notify(nameof(PreviewText));
    }
    public void SetCompact(bool value) { if (compact == value) return; compact = value; Notify(nameof(DateColumnWidth)); }
    private void SelectionChanged() { Notify(nameof(HasSelection)); Notify(nameof(AllSelected)); Notify(nameof(SelectionSummary)); }
}
