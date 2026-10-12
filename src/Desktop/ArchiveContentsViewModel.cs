using System.Collections.ObjectModel;
using System.IO;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed class ArchiveContentsViewModel(string archivePath, IReadOnlyList<ArchiveEntry> items) : ObservableObject
{
    private readonly IReadOnlyList<ArchiveContentRow> allRows = items.Select(value => new ArchiveContentRow(value)).ToArray();
    private string search = "";
    public string ArchivePath => archivePath;
    public string ArchiveName => Path.GetFileName(archivePath);
    public ObservableCollection<ArchiveContentRow> Rows { get; } = new(items.Select(value => new ArchiveContentRow(value)));
    public string Search { get => search; set { if (Set(ref search, value)) { Rows.Clear(); foreach (var row in allRows.Where(row => row.Path.Contains(search, StringComparison.CurrentCultureIgnoreCase))) Rows.Add(row); Changed(nameof(Summary)); } } }
    public string Summary => $"显示 {Rows.Count} / {allRows.Count} 项 · {items.Count(value => !value.Directory)} 个文件 · {items.Count(value => value.Directory)} 个文件夹";
}

public sealed class ArchiveContentRow(ArchiveEntry entry)
{
    public string Path => entry.Path;
    public string Kind => entry.Directory ? "文件夹" : "文件";
    public string SizeText => entry.Directory ? "—" : entry.Size < 1024 ? $"{entry.Size} B" : entry.Size < 1024 * 1024 ? $"{entry.Size / 1024.0:0.#} KB" : entry.Size < 1024ul * 1024 * 1024 ? $"{entry.Size / (1024.0 * 1024):0.#} MB" : $"{entry.Size / (1024.0 * 1024 * 1024):0.#} GB";
    public string EncryptionText => entry.Encrypted ? "已加密" : "未加密";
}
