namespace ArchivePassword.Core;

public sealed class CompressionExclusions
{
    private readonly HashSet<string> directories;
    private readonly string[] extensions;
    private CompressionExclusions(IEnumerable<string> directories,IEnumerable<string> extensions)
    {this.directories=new(directories,StringComparer.OrdinalIgnoreCase);this.extensions=extensions.ToArray();}
    public static CompressionExclusions Empty {get;}=new([],[]);
    public int RuleCount=>directories.Count+extensions.Length;
    public bool Excludes(string archivePath,bool directory)
    {
        if(RuleCount==0)return false;
        var name=archivePath.Replace('\\','/').TrimEnd('/').Split('/')[^1];
        return directory?directories.Contains(name):extensions.Any(extension=>name.EndsWith(extension,StringComparison.OrdinalIgnoreCase));
    }
    public static bool TryCreate(string directoryNames,string extensionNames,out CompressionExclusions result)
    {
        result=Empty;
        if(directoryNames.Length>1024 || extensionNames.Length>1024)return false;
        var names=directoryNames.Split([';','\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var types=extensionNames.Split([';',',',' ','\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
            .Select(value=>value.StartsWith('.')?value:"."+value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(names.Any(value=>value is "." or ".." || value.IndexOfAny(['\\','/',':','*','?','"','<','>','|','\0'])>=0)
            || types.Any(value=>value.Length<2 || value.Skip(1).Any(c=>!char.IsLetterOrDigit(c) && c is not '.' and not '-' and not '_')))return false;
        result=new(names,types);return true;
    }
}

public sealed record CompressionContentItem(string Package,string Path,bool Directory,bool Included,long? Bytes);
public sealed record CompressionContentPreview(IReadOnlyList<CompressionContentItem> Items,int FileCount,int DirectoryCount,long SourceBytes,int ExcludedCount)
{
    public static async Task<CompressionContentPreview> ScanAsync(IReadOnlyList<string> sources,string output,CompressionExclusions exclusions,
        bool separate=false,CancellationToken token=default)
    {
        var groups=separate
            ?SeparateCompressionPlan.Create(sources,System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output))!,System.IO.Path.GetExtension(output))
                .Select(item=>(sources:(IReadOnlyList<string>)[item.SourcePath],output:item.OutputPath)).ToArray()
            :[(sources:sources,output:output)];
        var items=new List<CompressionContentItem>();var files=0;var directories=0;long bytes=0;var excludedCount=0;
        foreach(var group in groups)
        {
            var excluded=new List<CompressionEntry>();
            var entries=await CompressionSources.CollectAsync(group.sources,System.IO.Path.GetFullPath(group.output),token,exclusions,excluded.Add);
            await Task.Run(()=>
            {
                var package=System.IO.Path.GetFileName(group.output);
                foreach(var entry in entries)
                {
                    token.ThrowIfCancellationRequested();long? length=entry.Directory?null:new FileInfo(entry.SourcePath).Length;
                    items.Add(new(package,entry.Path,entry.Directory,true,length));
                    if(entry.Directory)directories++;else {files++;bytes=checked(bytes+length!.Value);}
                }
                foreach(var entry in excluded){token.ThrowIfCancellationRequested();items.Add(new(package,entry.Path,entry.Directory,false,null));excludedCount++;}
            },token);
        }
        return new(items,files,directories,bytes,excludedCount);
    }
}
