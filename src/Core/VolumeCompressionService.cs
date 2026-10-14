namespace ArchivePassword.Core;

public sealed class VolumeCompressionService(IArchiveCreator creator,IArchiveEngine validator)
{
    public static string PartPath(string archive,int number)=>archive+"."+number.ToString("D3",System.Globalization.CultureInfo.InvariantCulture);
    public static bool IsVolumePath(string path)
    {
        var extension=Path.GetExtension(path);var inner=Path.GetExtension(Path.GetFileNameWithoutExtension(path));
        return (inner.Equals(".7z",StringComparison.OrdinalIgnoreCase) || inner.Equals(".zip",StringComparison.OrdinalIgnoreCase))
            && extension.Length>=4 && extension[1..].All(char.IsAsciiDigit);
    }
    public static bool HasExistingTarget(string archive)
    {
        archive=Path.GetFullPath(archive);if(File.Exists(archive) || Directory.Exists(archive)) return true;
        var parent=Path.GetDirectoryName(archive)!;if(!Directory.Exists(parent)) return false;
        var prefix=Path.GetFileName(archive)+".";
        return Directory.EnumerateFileSystemEntries(parent).Any(path=>Path.GetFileName(path).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(path)[prefix.Length..] is var suffix && suffix.Length>=3 && suffix.All(char.IsAsciiDigit));
    }
    public async Task<CompressionOutcome> CreateAsync(IEnumerable<string> sources,string output,string format,int level,string? password,bool headers,
        ulong size,IProgress<TaskUpdate>? progress=null,CancellationToken token=default)
    {
        string? staging=null;string? parent=null;var committed=new List<string>();bool delivered=false;
        CompressionOutcome Failure(string code)=>new(new EngineResult {Code=code},output);
        try
        {
            if(size<128 || size>(1UL<<40)) return Failure("invalid_volume_size");
            if(format is not ("7z" or "zip") || level is not (0 or 1 or 3 or 5 or 7 or 9)) return Failure("invalid_compression_options");
            if(format=="zip" && password?.Any(c=>c>127)==true) return Failure("zip_password_ascii_only");
            output=Path.GetFullPath(output);parent=Path.GetDirectoryName(output)!;
            if(HasExistingTarget(output)) return Failure("output_exists");
            progress?.Report(new("preparing","正在准备分卷压缩内容…"));
            var entries=await CompressionSources.CollectAsync(sources,output,token);
            if(entries.Count==0) return Failure("empty_sources");
            Directory.CreateDirectory(parent);staging=Path.Combine(parent,".nomozip-volumes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            var stagedBase=Path.Combine(staging,Path.GetFileName(output));
            progress?.Report(new("compressing","正在创建分卷，目标文件保持不变…"));
            var created=await creator.CreateAsync(new(entries,format,level,format=="7z" && headers,size),stagedBase,password,
                new ForwardProgress<EngineProgress>(value=>progress?.Report(new("compressing","正在创建分卷…",value))),token);
            if(!created.IsSuccess) return new(created,output);
            token.ThrowIfCancellationRequested();
            var files=Directory.GetFiles(staging);if(created.VolumeCount==0 || created.VolumeCount>(ulong)int.MaxValue || files.Length!=(long)created.VolumeCount) return Failure("invalid_volume_set");
            var parts=Enumerable.Range(1,files.Length).Select(index=>PartPath(stagedBase,index)).ToArray();
            if(parts.Any(path=>!File.Exists(path) || new FileInfo(path).Length<=0 || (ulong)new FileInfo(path).Length>size)
                || parts.Take(parts.Length-1).Any(path=>(ulong)new FileInfo(path).Length!=size)) return Failure("invalid_volume_set");
            progress?.Report(new("testing","正在完整校验全部分卷…"));
            var tested=await validator.TestAsync(parts[0],password,cancellationToken:token);
            if(!tested.IsSuccess) return new(tested,output);
            var listed=await validator.ListAsync(parts[0],password,token);
            if(!listed.IsSuccess) return new(listed,output);
            if(listed.Items.Count!=entries.Count || !string.Equals(listed.Format,format,StringComparison.OrdinalIgnoreCase)) return Failure("invalid_volume_set");
            var expected=entries.ToDictionary(entry=>entry.Path.Replace('\\','/'),StringComparer.OrdinalIgnoreCase);
            if(listed.Items.Any(entry=>!expected.TryGetValue(entry.Path.Replace('\\','/'),out var source) || entry.Directory!=source.Directory
                || (!entry.Directory && entry.Size!=(ulong)new FileInfo(source.SourcePath).Length))) return Failure("invalid_volume_set");
            token.ThrowIfCancellationRequested();if(HasExistingTarget(output)) return Failure("output_exists");
            var targets=Enumerable.Range(1,parts.Length).Select(index=>PartPath(output,index)).ToArray();
            for(var index=0;index<parts.Length;index++)
            {
                token.ThrowIfCancellationRequested();File.Move(parts[index],targets[index]);committed.Add(targets[index]);
                progress?.Report(new("committing",$"正在交付分卷 {index+1}/{parts.Length}…"));
            }
            token.ThrowIfCancellationRequested();delivered=true;return new(tested,targets[0],targets);
        }
        catch(OperationCanceledException) {return new(new EngineResult {Status="cancelled",Code="cancelled"},output);}
        catch(InvalidDataException error) {return Failure(error.Message);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {return Failure("source_or_output_unavailable");}
        finally
        {
            if(!delivered) foreach(var file in committed) if(File.Exists(file)) File.Delete(file);
            if(staging is not null && Directory.Exists(staging))
            {
                if(!Path.GetFullPath(staging).StartsWith(parent!+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(staging).StartsWith(".nomozip-volumes-",StringComparison.Ordinal)) throw new IOException("Invalid temporary volume scope.");
                Directory.Delete(staging,true);
            }
        }
    }
}
