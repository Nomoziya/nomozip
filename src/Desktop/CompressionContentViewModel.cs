using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private string compressionExcludedDirectories="",compressionExcludedExtensions="",compressionContentSummary="";
    public string CompressionExcludedDirectories {get=>compressionExcludedDirectories;set {if(Set(ref compressionExcludedDirectories,value))CompressionExclusionsChanged();}}
    public string CompressionExcludedExtensions {get=>compressionExcludedExtensions;set {if(Set(ref compressionExcludedExtensions,value))CompressionExclusionsChanged();}}
    public string CompressionExclusionError=>CompressionExclusions.TryCreate(compressionExcludedDirectories,compressionExcludedExtensions,out _)?"":UiText.Get("Compression.ExclusionError");
    private CompressionExclusions CompressionRules=>CompressionExclusions.TryCreate(compressionExcludedDirectories,compressionExcludedExtensions,out var rules)?rules:CompressionExclusions.Empty;
    public string CompressionContentSummary=>compressionContentSummary.Length>0?compressionContentSummary:CompressionRules.RuleCount>0?UiText.Format("Compression.RuleCount",CompressionRules.RuleCount):"";
    private RelayCommand? previewCompressionContentsCommand;
    public ICommand PreviewCompressionContentsCommand=>previewCompressionContentsCommand ??= new RelayCommand(()=>
    {
        using var preview=new CompressionContentsViewModel(CompressionSources.Select(row=>row.Path).ToArray(),CompressionOutput,CompressionRules,CompressionSeparate);
        dialogs.ShowCompressionContents(preview);
        if(preview.Result is not null){compressionContentSummary=UiText.Format("Compression.LastPreview",preview.Summary);Changed(nameof(CompressionContentSummary));}
    },()=>IsIdle && CompressionSources.Count>0 && !string.IsNullOrWhiteSpace(CompressionOutput) && CompressionSourceError.Length==0);
    private void CompressionExclusionsChanged()
    {
        InvalidateCompressionContents();Changed(nameof(CompressionExclusionError));Changed(nameof(CompressionSourceError));CommandManager.InvalidateRequerySuggested();
    }
    private void InvalidateCompressionContents(){compressionContentSummary="";Changed(nameof(CompressionContentSummary));}
}

public sealed record CompressionContentRow(CompressionContentItem Item)
{
    public string Package=>Item.Package;
    public string Path=>Item.Path;
    public string State=>UiText.Get(Item.Included?"Compression.Included":"Compression.Excluded");
    public string SizeText=>Item.Bytes is {} bytes?bytes>=1048576?$"{bytes/1048576d:0.#} MiB":bytes>=1024?$"{bytes/1024d:0.#} KiB":$"{bytes} B":"—";
}

public sealed class CompressionContentsViewModel(IReadOnlyList<string> sources,string output,CompressionExclusions rules,bool separate) : ObservableObject,IDisposable
{
    private readonly CancellationTokenSource lifetime=new();
    private Task? loading;
    private bool closed;
    private string summary=UiText.Get("Compression.ScanningContents");
    public ResettableCollection<CompressionContentRow> Rows {get;}=[];
    public CompressionContentPreview? Result {get;private set;}
    public string Summary {get=>summary;private set=>Set(ref summary,value);}
    public Task InitializeAsync()=>loading ??= LoadAsync();
    private async Task LoadAsync()
    {
        try
        {
            var result=await CompressionContentPreview.ScanAsync(sources,output,rules,separate,lifetime.Token);
            if(closed)return;
            Result=result;Rows.ReplaceAll(result.Items.Select(item=>new CompressionContentRow(item)).ToArray());
            Summary=UiText.Format("Compression.ContentCounts",result.FileCount,result.DirectoryCount,result.SourceBytes,result.ExcludedCount);
        }
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested) { }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException)
        {if(!closed)Summary=UiText.Get("Compression.ContentScanFailed");}
    }
    public void Dispose(){if(closed)return;closed=true;lifetime.Cancel();lifetime.Dispose();}
}
