using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

internal static class CompressionExclusionsValidation
{
    internal static async Task RunAsync(string root,string folder,string version,ArchiveEngineClient client,Action<string,bool> check)
    {
        var source=Path.Combine(folder,"content-"+version);Directory.CreateDirectory(source);
        foreach(var name in new[]{"cache","cache-old","sub/cache"})Directory.CreateDirectory(Path.Combine(source,name));
        foreach(var name in new[]{"keep.txt","cache-old/keep.txt"})await File.WriteAllTextAsync(Path.Combine(source,name),"abc");
        foreach(var name in new[]{"skip.tmp","skip.tar.gz","cache/ignored.bin","sub/cache/ignored.bin"})await File.WriteAllTextAsync(Path.Combine(source,name),"excluded");
        check(version+" exclusion rules parse",CompressionExclusions.TryCreate("cache","tmp;tar.gz",out var rules));
        check(version+" exclusion rules reject ambiguous paths and wildcards",!CompressionExclusions.TryCreate("../cache","tmp",out _) && !CompressionExclusions.TryCreate("cache","*.tmp",out _));
        check(version+" exclusion leaf matching handles both separators case and trailing slashes",rules.Excludes("root\\CACHE\\",true) && rules.Excludes("root/cache/",true)
            && !rules.Excludes("root/cache-old",true) && rules.Excludes("root\\file.TAR.GZ",false) && !rules.Excludes("root/cache/keep.txt",false));
        var output=Path.Combine(folder,version+"-content.zip");
        var preview=await CompressionContentPreview.ScanAsync([source],output,rules);
        check(version+" content preview counts included bytes and pruned exclusions",preview.FileCount==2 && preview.SourceBytes==6 && preview.ExcludedCount==4
            && preview.Items.Any(row=>row.Directory && !row.Included) && !preview.Items.Any(row=>row.Path.EndsWith("ignored.bin")));
        var all=await CompressionContentPreview.ScanAsync([source],output,CompressionExclusions.Empty);
        check(version+" empty content rules retain all sources",all.FileCount==6 && all.ExcludedCount==0);
        var service=new CompressionService(client,client);
        var created=await service.CreateAsync([source],output,"zip",exclusions:rules);
        var listed=await client.ListAsync(output);
        var expected=new[]{Path.GetFileName(source)+"/keep.txt",Path.GetFileName(source)+"/cache-old/keep.txt"}.ToHashSet(StringComparer.OrdinalIgnoreCase);
        check(version+" real ZIP matches preview exclusions",created.Engine.IsSuccess && listed.IsSuccess && expected.SetEquals(listed.Items.Where(row=>!row.Directory).Select(row=>row.Path.Replace('\\','/'))));
        var extracted=output+".out";var result=await client.ExtractAsync(output,extracted);
        check(version+" filtered ZIP extracts exact included bytes",result.IsSuccess && expected.All(path=>File.ReadAllText(Path.Combine(extracted,path))=="abc")
            && Directory.GetFiles(extracted,"*",SearchOption.AllDirectories).Length==2);
        var volume=await service.CreateAsync([source],Path.Combine(folder,version+"-content-volumes.zip"),"zip",volumeSize:1048576,exclusions:rules);
        var volumeList=volume.Engine.IsSuccess?await client.ListAsync(volume.Volumes![0]):null;
        check(version+" volume compression applies identical exclusions",volume.Engine.IsSuccess && volumeList?.IsSuccess==true
            && expected.SetEquals(volumeList.Items.Where(row=>!row.Directory).Select(row=>row.Path.Replace('\\','/'))));
        using var manifest=System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root,"engines",version,"manifest.json")));
        var module=new SfxModule(version,manifest.RootElement.GetProperty("sfxSha256").GetString()!,await File.ReadAllBytesAsync(Path.Combine(root,"engines",version,"7z.sfx")));
        var sfx=await new SfxCompressionService(client,client).CreateAsync([source],Path.Combine(folder,version+"-filtered.exe"),module,exclusions:rules);
        var sfxList=sfx.Engine.IsSuccess?await client.ListAsync(sfx.OutputPath):null;
        check(version+" SFX applies identical exclusions without execution",sfx.Engine.IsSuccess && sfxList?.IsSuccess==true
            && expected.SetEquals(sfxList.Items.Where(row=>!row.Directory).Select(row=>row.Path.Replace('\\','/'))));
        var separate=await CompressionContentPreview.ScanAsync([source,Path.Combine(source,"skip.tmp")],Path.Combine(folder,"separate","combined.zip"),rules,separate:true);
        check(version+" separate preview keeps package groups and excluded sources",separate.Items.Select(row=>row.Package).Distinct().Count()==2 && separate.FileCount==2 && separate.ExcludedCount==5);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();var observed=false;
        try {await CompressionContentPreview.ScanAsync([source],output,rules,token:cancelled.Token);}catch(OperationCanceledException){observed=true;}
        check(version+" content scanning honors cancellation",observed);
    }
}
