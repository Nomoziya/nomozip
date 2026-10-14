namespace ArchivePassword.Core;
public sealed class SfxCompressionService(IArchiveCreator creator,IArchiveEngine validator)
{
    public async Task<CompressionOutcome> CreateAsync(IEnumerable<string> sources,string output,SfxModule module,int level=5,string? password=null,
        bool encryptHeaders=true,bool replace=false,IProgress<TaskUpdate>? progress=null,CancellationToken token=default)
    {
        string? archive=null,executable=null;
        CompressionOutcome Failure(string code)=>new(new EngineResult {Code=code},output);
        try
        {
            if(!module.IsValid()) return Failure("sfx_module_invalid");
            if(level is not (0 or 1 or 3 or 5 or 7 or 9)) return Failure("invalid_compression_options");
            if(!Path.GetExtension(output).Equals(".exe",StringComparison.OrdinalIgnoreCase)) return Failure("sfx_output_extension");
            output=Path.GetFullPath(output);if(File.Exists(output) && !replace) return Failure("output_exists");
            var entries=await CompressionSources.CollectAsync(sources,output,token);if(entries.Count==0) return Failure("empty_sources");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);var stem=Path.Combine(Path.GetDirectoryName(output)!,".nomozip-sfx-"+Guid.NewGuid().ToString("N"));archive=stem+".7z";executable=stem+".exe";
            progress?.Report(new("compressing","正在创建自解压内容…"));
            var created=await creator.CreateAsync(new(entries,"7z",level,encryptHeaders),archive,password,new ForwardProgress<EngineProgress>(value=>progress?.Report(new("compressing","正在创建自解压内容…",value))),token);
            if(!created.IsSuccess) return new(created,output);token.ThrowIfCancellationRequested();
            progress?.Report(new("assembling","正在组合官方自解压模块…"));
            await using(var destination=new FileStream(executable,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
            {
                await destination.WriteAsync(module.Bytes,token);await using var input=File.OpenRead(archive);await input.CopyToAsync(destination,token);
            }
            progress?.Report(new("testing","正在完整校验自解压包…"));
            var tested=await validator.TestAsync(executable,password,cancellationToken:token);if(!tested.IsSuccess) return new(tested,output);
            var listed=await validator.ListAsync(executable,password,token);if(!listed.IsSuccess) return new(listed,output);
            var expected=entries.ToDictionary(entry=>entry.Path.Replace('\\','/'),StringComparer.OrdinalIgnoreCase);
            if(listed.Format.ToLowerInvariant()!="7z" || listed.Items.Count!=entries.Count || listed.Items.Any(entry=>!expected.TryGetValue(entry.Path.Replace('\\','/'),out var source)
                || entry.Directory!=source.Directory || !entry.Directory && entry.Size!=(ulong)new FileInfo(source.SourcePath).Length)) return Failure("sfx_validation_failed");
            progress?.Report(new("committing","正在交付已校验的自解压包…"));token.ThrowIfCancellationRequested();
            if(File.Exists(output)) {if(!replace) return Failure("output_exists");File.Replace(executable,output,null);} else File.Move(executable,output);
            return new(tested,output);
        }
        catch(OperationCanceledException) {return new(new EngineResult {Status="cancelled",Code="cancelled"},output);}
        catch(InvalidDataException error) {return Failure(error.Message);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {return Failure("source_or_output_unavailable");}
        finally {foreach(var file in new[]{archive,executable}) if(file is not null && File.Exists(file)) File.Delete(file);}
    }
}
