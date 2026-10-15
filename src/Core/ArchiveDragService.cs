namespace ArchivePassword.Core;

public sealed record ArchiveDragOutcome(EngineResult Engine,ArchiveDragLease? Content=null,string? Warning=null);

// A preparation is not a completed delivery. It never records or saves passwords.
public sealed class ArchiveDragService(IArchiveEngine engine,IPasswordVault vault,IPasswordCandidateProvider candidates,string temporaryRoot,IExtractionOriginReader? origin=null)
{
    public async Task<ArchiveDragOutcome> PrepareAsync(string archive,IReadOnlyList<ArchiveEntry> selection,bool autoTry,
        Func<EngineResult,Task<PasswordPromptResponse?>> requestPassword,IProgress<TaskUpdate>? progress=null,
        CancellationToken token=default,CancellationToken skipSavedPasswords=default)
    {
        ArchiveDragLease? lease=null;
        var outcome=new ArchiveDragOutcome(new EngineResult {Code="output_write_failed"});
        try
        {
            if(selection.Count==0 || selection.Select(e=>e.Index).Distinct().Count()!=selection.Count)
                return new(new EngineResult {Code="invalid_selection"});
            using var sourceLock=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
            lease=ArchiveDragLease.Create(temporaryRoot);
            var result=await new ExtractionService(engine,vault,candidates,origin).ExtractAsync(archive,lease.DirectoryPath,autoTry,
                requestPassword,_=>Task.FromResult(ExistingFilePolicy.Replace),progress,token,skipSavedPasswords,
                selection:selection,recordPasswordSuccess:false);
            outcome=new(result.Engine,Warning:result.Warning);
            token.ThrowIfCancellationRequested();
            if(result.Engine.IsSuccess)
            {
                lease.SetPaths();
                if(lease.Paths.Count==0) outcome=new(new EngineResult {Code="invalid_selection"},Warning:result.Warning);
                else {outcome=outcome with {Content=lease};lease=null;}
            }
        }
        catch(OperationCanceledException) {outcome=new(new EngineResult {Status="cancelled",Code="cancelled"});}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {outcome=new(new EngineResult {Code="output_write_failed"});}
        finally
        {
            var warning=lease?.Cleanup();
            if(warning is not null) outcome=outcome with {Warning=string.Join("\n",new[]{outcome.Warning,warning}.Where(s=>!string.IsNullOrEmpty(s)))};
        }
        return outcome;
    }
}

// Only this type allocates its GUID workspace. Cleanup cannot target the caller's root.
public sealed class ArchiveDragLease
{
    private readonly string parent;
    private ArchiveDragLease(string parent,string path) {this.parent=parent;DirectoryPath=path;}
    public string DirectoryPath {get;}
    public IReadOnlyList<string> Paths {get;private set;}=[];
    internal static ArchiveDragLease Create(string root)
    {
        var parent=Path.GetFullPath(root);var path=Path.GetFullPath(Path.Combine(parent,Guid.NewGuid().ToString("N")));
        var lease=new ArchiveDragLease(parent,path);lease.Validate();Directory.CreateDirectory(path);return lease;
    }
    private void Validate()
    {
        if(!DirectoryPath.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(DirectoryPath),"N",out _)) throw new IOException("Invalid drag workspace.");
    }
    internal void SetPaths()=>Paths=Directory.GetFileSystemEntries(DirectoryPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public string? Cleanup()
    {
        try {Validate();if(Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath,true);Paths=[];return null;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {return UiText.Get("Drag.CleanupWarning")+"\n"+DirectoryPath;}
    }
}
