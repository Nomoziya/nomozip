namespace ArchivePassword.Core;

public sealed record ArchiveEditRequest(IReadOnlyList<ArchiveEntry> Snapshot,IReadOnlyList<uint> RemovedIndices,
    IReadOnlyList<CompressionEntry> Additions,bool EncryptHeaders);
public interface IArchiveEditor
{
    Task<EngineResult> UpdateAsync(string archive,string output,ArchiveEditRequest request,string? password,
        IProgress<EngineProgress>? progress=null,CancellationToken token=default);
}
public sealed record ArchiveEditOutcome(EngineResult Engine,IReadOnlyList<ArchiveEntry> Items,string? BackupPath=null,string? Warning=null);

public sealed class ArchiveEditingService(IArchiveEngine engine,IArchiveEditor editor,IPasswordCandidateProvider candidates)
{
    public static IReadOnlyList<CompressionEntry> CollectSources(IEnumerable<string> sources,CancellationToken token=default)
    {
        var entries=new List<CompressionEntry>();
        void Include(string source,string name)
        {
            token.ThrowIfCancellationRequested();
            var attributes=File.GetAttributes(source);
            if((attributes&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("unsupported_link");
            var directory=(attributes&FileAttributes.Directory)!=0;
            entries.Add(new(Path.GetFullPath(source),NormalizePath(name),directory));
            if(directory) foreach(var child in Directory.EnumerateFileSystemEntries(source)) Include(child,name+"/"+Path.GetFileName(child));
        }
        foreach(var source in sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)) Include(source,Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
        return entries;
    }
    private static string NormalizePath(string value)
    {
        value=value.Replace('\\','/').TrimEnd('/');
        if(value.Length==0 || value.Split('/').Any(part=>part.Length==0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
            || part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)) throw new InvalidDataException("unsafe_archive_path");
        return value;
    }
    public async Task<ArchiveEditOutcome> EditAsync(string archive,IReadOnlyList<ArchiveEntry> snapshot,IReadOnlyList<uint> removed,
        IReadOnlyList<CompressionEntry> additions,bool autoTry,Func<EngineResult,Task<PasswordPromptResponse?>> askPassword,
        IProgress<TaskUpdate>? progress=null,CancellationToken token=default)
    {
        string? temporary=null;FileStream? sourceLock=null;
        ArchiveEditOutcome Failure(string code)=>new(new EngineResult {Code=code},snapshot);
        try
        {
            archive=Path.GetFullPath(archive);
            // Block writers throughout verification and creation. Release only for atomic replacement.
            sourceLock=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
            var verified=await new ArchiveVerificationService(engine,candidates).VerifyAsync(archive,autoTry,askPassword,progress,token);
            if(!verified.Engine.IsSuccess) return new(verified.Engine,snapshot,Warning:verified.Warning);
            var listed=await engine.ListAsync(archive,verified.Secret,token);
            if(!listed.IsSuccess) return new(listed,snapshot);
            if(listed.Format?.ToLowerInvariant() is not ("7z" or "zip")) return Failure("archive_edit_read_only");
            if(!snapshot.SequenceEqual(listed.Items)) return Failure("selection_changed");
            // Keep mixed-password reading/extraction supported, without guessing which secret new files should inherit.
            if(verified.VerifiedPasswords.Values.Select(value=>value.Secret).Distinct().Skip(1).Any()) return Failure("archive_edit_mixed_passwords");
            if(listed.Format.Equals("zip",StringComparison.OrdinalIgnoreCase) && verified.Secret?.Any(value=>value>127)==true && additions.Count>0) return Failure("zip_password_ascii_only");
            var removals=removed.ToHashSet();
            if(removals.Count!=removed.Count || removed.Any(index=>!snapshot.Any(item=>item.Index==index))) return Failure("invalid_selection");
            if(removals.Count==0 && additions.Count==0) return Failure("empty_changes");
            var expected=new Dictionary<string,(bool Directory,ulong Size,bool Encrypted)>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in snapshot.Where(item=>!removals.Contains(item.Index)))
                if(!expected.TryAdd(NormalizePath(item.Path),(item.Directory,item.Size,item.Encrypted))) return Failure("duplicate_archive_path");
            foreach(var addition in additions)
            {
                token.ThrowIfCancellationRequested();
                if(Path.GetFullPath(addition.SourcePath).Equals(archive,StringComparison.OrdinalIgnoreCase)) return Failure("invalid_source");
                var attributes=File.GetAttributes(addition.SourcePath);
                if((attributes&FileAttributes.ReparsePoint)!=0 || ((attributes&FileAttributes.Directory)!=0)!=addition.Directory) return Failure("unsupported_link");
                var name=NormalizePath(addition.Path);
                if(!expected.TryAdd(name,(addition.Directory,addition.Directory ? 0 : (ulong)new FileInfo(addition.SourcePath).Length,!addition.Directory && verified.Secret is not null))) return Failure("duplicate_archive_path");
            }
            foreach(var name in expected.Keys)
            {
                var parent=name;
                while(parent.LastIndexOf('/') is var slash && slash>=0)
                {parent=parent[..slash];if(expected.TryGetValue(parent,out var entry) && !entry.Directory) return Failure("duplicate_archive_path");}
            }
            var headerProbe=await engine.ListAsync(archive,cancellationToken:token);
            if(expected.Count==0 && headerProbe.NeedsPassword) return Failure("archive_edit_empty_encrypted");
            token.ThrowIfCancellationRequested();
            temporary=Path.Combine(Path.GetDirectoryName(archive)!,".nomozip-edit-"+Guid.NewGuid().ToString("N")+".tmp");
            progress?.Report(new("editing","正在写入临时新包，原包保持不变…"));
            var result=await editor.UpdateAsync(archive,temporary,new(snapshot,removed,additions,headerProbe.NeedsPassword),verified.Secret,
                new ForwardProgress<EngineProgress>(value=>progress?.Report(new("editing","正在写入临时新包…",value))),token);
            if(!result.IsSuccess) return new(result,snapshot);
            progress?.Report(new("testing","正在完整校验修改后的压缩包…"));
            result=await engine.TestAsync(temporary,verified.Secret,cancellationToken:token);
            if(!result.IsSuccess) return new(result,snapshot);
            var final=await engine.ListAsync(temporary,verified.Secret,token);
            if(!final.IsSuccess) return new(final,snapshot);
            if(final.Items.Count!=expected.Count || final.Items.Any(item=>!expected.TryGetValue(NormalizePath(item.Path),out var entry)
                || item.Directory!=entry.Directory || item.Size!=entry.Size || item.Encrypted!=entry.Encrypted)) return Failure("archive_edit_inventory_mismatch");
            if(headerProbe.NeedsPassword && !(await engine.ListAsync(temporary,cancellationToken:token)).NeedsPassword) return Failure("archive_edit_encryption_changed");
            token.ThrowIfCancellationRequested();
            var backup=archive+".before-edit-"+Guid.NewGuid().ToString("N")+".bak";
            sourceLock.Dispose();sourceLock=null;
            // The backup contains the original bytes, including deleted/replaced contents.
            File.Replace(temporary,archive,backup);
            return new(result,final.Items,backup,verified.Warning);
        }
        catch(OperationCanceledException) {return new(new EngineResult {Status="cancelled",Code="cancelled"},snapshot);}
        catch(InvalidDataException error) {return Failure(error.Message);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {return Failure("source_or_output_unavailable");}
        finally {sourceLock?.Dispose();if(temporary is not null && File.Exists(temporary)) File.Delete(temporary);}
    }
}
