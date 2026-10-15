using System.Security.Cryptography;

namespace ArchivePassword.Core;

public sealed record ArchiveConversionOutcome(EngineResult Engine,string OutputPath,string? Warning=null);

// Conversion deliberately has no vault write API: source credentials are used for reading only.
public sealed class ArchiveConversionService(IArchiveEngine engine,IArchiveCreator creator,IPasswordCandidateProvider candidates,
    IExtractionOriginReader? origin=null)
{
    public async Task<ArchiveConversionOutcome> ConvertAsync(string archive,string output,string format,int level,bool encrypt,
        string? password,bool encryptHeaders,bool replace,bool autoTry,Func<EngineResult,Task<PasswordPromptResponse?>> askPassword,
        IProgress<TaskUpdate>? progress=null,CancellationToken token=default)
    {
        string? workspace=null;
        var outcome=new ArchiveConversionOutcome(new EngineResult {Code="conversion_failed"},output);
        async Task<ArchiveConversionOutcome> RunAsync()
        {
            ArchiveConversionOutcome Fail(string code)=>new(new EngineResult {Code=code},output);
            archive=Path.GetFullPath(archive);output=Path.GetFullPath(output);
            if(format is not ("7z" or "zip") || level is not (0 or 1 or 3 or 5 or 7 or 9)) return Fail("invalid_compression_options");
            if(encrypt && string.IsNullOrEmpty(password)) return Fail("conversion_password_required");
            password=encrypt?password:null;
            if(format=="zip" && password?.Any(value=>value>127)==true) return Fail("zip_password_ascii_only");
            if(archive.Equals(output,StringComparison.OrdinalIgnoreCase)) return Fail("conversion_same_path");
            if(VolumeCompressionService.IsVolumePath(archive) || Path.GetExtension(archive).Equals(".exe",StringComparison.OrdinalIgnoreCase)) return Fail("conversion_container_unsupported");
            if(File.Exists(output) && !replace) return Fail("output_exists");
            using var sourceLock=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
            var verified=await new ArchiveVerificationService(engine,candidates).VerifyAsync(archive,autoTry,askPassword,progress,token);
            if(!verified.Engine.IsSuccess) return new(verified.Engine,output,verified.Warning);
            var source=await engine.ListAsync(archive,verified.Secret,token);
            if(!source.IsSuccess) return new(source,output,verified.Warning);
            if(source.Format.ToLowerInvariant() is not ("7z" or "zip" or "rar" or "rar5")) return Fail("conversion_container_unsupported");
            if(source.Items.Count==0) return Fail("empty_sources");
            var expected=new Dictionary<string,ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in source.Items)
                if(!expected.TryAdd(Normalize(item.Path),item)) return Fail("duplicate_archive_path");
            foreach(var path in expected.Keys)
            {
                var parent=path;
                while(parent.LastIndexOf('/') is var index && index>=0)
                {parent=parent[..index];if(expected.TryGetValue(parent,out var item) && !item.Directory) return Fail("duplicate_archive_path");}
            }
            var mark=origin is null?null:await origin.ReadAsync(archive,token);
            var parentDirectory=Path.GetDirectoryName(output)!;Directory.CreateDirectory(parentDirectory);
            workspace=Path.Combine(parentDirectory,".nomozip-convert-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            var input=Path.Combine(workspace,"input");Directory.CreateDirectory(input);
            progress?.Report(new("converting-extract",UiText.Get("Conversion.Extracting")));
            var overrides=verified.VerifiedPasswords.Where(value=>value.Value.Secret!=verified.Secret).ToDictionary(value=>value.Key,value=>value.Value.Secret);
            var extracted=overrides.Count>0
                ? engine is IArchiveMultiPasswordEngine multi ? await multi.ExtractWithPasswordsAsync(archive,input,verified.Secret,overrides,cancellationToken:token) : new EngineResult {Code="unsupported_selection"}
                : await engine.ExtractAsync(archive,input,verified.Secret,cancellationToken:token);
            if(!extracted.IsSuccess) return new(extracted,output,verified.Warning);
            var entries=await Task.Run(()=>ArchiveEditingService.CollectSources(Directory.EnumerateFileSystemEntries(input),token),token);
            var files=entries.ToDictionary(value=>Normalize(value.Path),StringComparer.OrdinalIgnoreCase);
            // Materialized parent directories need not have explicit entries in the source archive.
            if(expected.Any(pair=>!files.TryGetValue(pair.Key,out var entry) || entry.Directory!=pair.Value.Directory
                || (!entry.Directory && (ulong)new FileInfo(entry.SourcePath).Length!=pair.Value.Size))
                || files.Any(pair=>!expected.ContainsKey(pair.Key) && (!pair.Value.Directory || !expected.Keys.Any(path=>path.StartsWith(pair.Key+"/",StringComparison.OrdinalIgnoreCase)))))
                return Fail("conversion_inventory_mismatch");
            var originalHashes=await HashesAsync(input,token);
            var temporary=Path.Combine(workspace,"converted."+format);
            progress?.Report(new("converting-create",UiText.Get("Conversion.Creating")));
            var created=await creator.CreateAsync(new(entries,format,level,encrypt && format=="7z" && encryptHeaders),temporary,password,
                new ForwardProgress<EngineProgress>(value=>progress?.Report(new("converting-create",UiText.Get("Conversion.Creating"),value))),token);
            if(!created.IsSuccess) return new(created,output,verified.Warning);
            progress?.Report(new("converting-verify",UiText.Get("Conversion.Verifying")));
            var tested=await engine.TestAsync(temporary,password,cancellationToken:token);
            if(!tested.IsSuccess) return new(tested,output,verified.Warning);
            var final=await engine.ListAsync(temporary,password,token);
            if(!final.IsSuccess) return new(final,output,verified.Warning);
            if(final.Format!=format || final.Items.Count!=entries.Count
                || final.Items.Select(value=>Normalize(value.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=entries.Count
                || final.Items.Any(item=>!files.TryGetValue(Normalize(item.Path),out var entry) || item.Directory!=entry.Directory
                    || (!item.Directory && (item.Size!=(ulong)new FileInfo(entry.SourcePath).Length || ((item.Size>0 || !encrypt) && item.Encrypted!=encrypt)))))
                return Fail("conversion_inventory_mismatch");
            if(format=="7z" && encrypt && encryptHeaders && !(await engine.ListAsync(temporary,cancellationToken:token)).NeedsPassword)
                return Fail("conversion_encryption_mismatch");
            var check=Path.Combine(workspace,"verify");Directory.CreateDirectory(check);
            var reread=await engine.ExtractAsync(temporary,check,password,cancellationToken:token);
            if(!reread.IsSuccess) return new(reread,output,verified.Warning);
            var finalHashes=await HashesAsync(check,token);
            if(originalHashes.Count!=finalHashes.Count || originalHashes.Any(pair=>!finalHashes.TryGetValue(pair.Key,out var hash) || hash!=pair.Value)
                || entries.Where(value=>value.Directory).Any(value=>!Directory.Exists(Path.Combine(check,value.Path)))) return Fail("conversion_content_mismatch");
            var warning=verified.Warning;
            if(mark is not null)
            {
                var additional=await mark.ApplyAsync([temporary],token);
                if(!string.IsNullOrEmpty(additional)) warning=string.IsNullOrEmpty(warning)?additional:warning+"\n"+additional;
            }
            progress?.Report(new("converting-commit",UiText.Get("Conversion.Delivering")));
            token.ThrowIfCancellationRequested();
            if(File.Exists(output)) {if(!replace) return Fail("output_exists");File.Replace(temporary,output,null);}
            else File.Move(temporary,output);
            return new(tested,output,warning);
        }
        try {outcome=await RunAsync();}
        catch(OperationCanceledException) {outcome=new(new EngineResult {Status="cancelled",Code="cancelled"},output);}
        catch(ExtractionOriginException error) {outcome=new(new EngineResult {Code=error.Code},output,error.Message);}
        catch(InvalidDataException error) {outcome=new(new EngineResult {Code=error.Message},output);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {outcome=new(new EngineResult {Code="source_or_output_unavailable"},output);}
        finally
        {
            if(workspace is not null && Directory.Exists(workspace))
                try {Directory.Delete(workspace,true);}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {outcome=outcome with {Warning=UiText.Get("Conversion.CleanupWarning")+"\n"+workspace};}
        }
        return outcome;
    }
    private static string Normalize(string path)
    {
        path=path.Replace('\\','/').TrimEnd('/');
        if(path.Length==0 || Path.IsPathRooted(path) || path.Split('/').Any(part=>part.Length==0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
            || part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)) throw new InvalidDataException("unsafe_archive_path");
        return path;
    }
    private static async Task<Dictionary<string,string>> HashesAsync(string root,CancellationToken token)
    {
        var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
            hashes.Add(Normalize(Path.GetRelativePath(root,path)),Convert.ToHexString(await SHA256.HashDataAsync(stream,token)));
        }
        return hashes;
    }
}
