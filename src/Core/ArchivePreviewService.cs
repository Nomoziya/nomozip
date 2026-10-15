using System.Text;

namespace ArchivePassword.Core;

public interface IArchivePreviewEngine
{
    Task<EngineResult> ExtractPreviewAsync(string archive, string output, ArchiveEntry entry, ulong limit,
        string? password, IReadOnlyDictionary<uint,string> itemPasswords, IProgress<EngineProgress>? progress=null, CancellationToken token=default);
}

public static class ArchivePreviewPolicy
{
    public const ulong TextLimit=1024*1024, ImageLimit=8*1024*1024;
    public static string Kind(string path)=>Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"=>"image",
        ".txt" or ".log" or ".md" or ".csv" or ".json" or ".xml" or ".yaml" or ".yml" or ".ini" or ".cfg" or ".conf"
        or ".html" or ".htm" or ".css" or ".js" or ".ts" or ".cs" or ".cpp" or ".h" or ".py" or ".ps1" or ".bat" or ".sh"=>"text",
        _=>""
    };
    public static string Rejection(ArchiveEntry entry)=>entry.Directory || Kind(entry.Path)==""?"preview_unsupported":
        entry.Size>(Kind(entry.Path)=="text"?TextLimit:ImageLimit)?"preview_too_large":"";
    public static (string Text,string Encoding) DecodeText(byte[] bytes)
    {
        Encoding encoding=new UTF8Encoding(false,true);int skip=0;
        if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe,0,0})) {encoding=new UTF32Encoding(false,true,true);skip=4;}
        else if(bytes.AsSpan().StartsWith(new byte[]{0,0,0xfe,0xff})) {encoding=new UTF32Encoding(true,true,true);skip=4;}
        else if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe})) {encoding=new UnicodeEncoding(false,true,true);skip=2;}
        else if(bytes.AsSpan().StartsWith(new byte[]{0xfe,0xff})) {encoding=new UnicodeEncoding(true,true,true);skip=2;}
        else if(bytes.AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf})) skip=3;
        var text=encoding.GetString(bytes,skip,bytes.Length-skip);
        if(text.Any(c=>char.IsControl(c) && c is not ('\t' or '\r' or '\n' or '\f'))) throw new DecoderFallbackException();
        return(text,encoding.WebName);
    }
}

public sealed record ArchivePreviewOutcome(EngineResult Engine, byte[]? Content=null, string? Warning=null);

// No vault write API and no final extraction destination: previews never count as delivery.
public sealed class ArchivePreviewService(IArchiveEngine engine, IPasswordCandidateProvider candidates, string temporaryRoot)
{
    public async Task<ArchivePreviewOutcome> ReadAsync(string archive, ArchiveEntry entry, bool autoTry,
        Func<EngineResult,Task<PasswordPromptResponse?>> requestPassword, IProgress<TaskUpdate>? progress=null, CancellationToken token=default,CancellationToken skipSavedPasswords=default)
    {
        string? workspace=null;
        var outcome=new ArchivePreviewOutcome(new EngineResult {Code="preview_failed"});
        async Task<ArchivePreviewOutcome> RunAsync()
        {
            var rejection=ArchivePreviewPolicy.Rejection(entry);
            if(rejection!="") return new(new EngineResult {Code=rejection});
            if(engine is not IArchivePreviewEngine preview) return new(new EngineResult {Code="unsupported_operation"});
            var relative=entry.Path.Replace('\\','/');
            if(Path.IsPathRooted(relative) || relative.Split('/').Any(p=>p.Length==0 || p is "." or ".." || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))
                return new(new EngineResult {Code="unsafe_path"});
            using var sourceLock=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
            var verified=await new ArchiveVerificationService(engine,candidates).VerifyAsync(archive,autoTry,requestPassword,progress,token,skipSavedPasswords,selection:[entry]);
            if(!verified.Engine.IsSuccess) return new(verified.Engine,Warning:verified.Warning);
            token.ThrowIfCancellationRequested();
            var parent=Path.GetFullPath(temporaryRoot);
            workspace=Path.GetFullPath(Path.Combine(parent,Guid.NewGuid().ToString("N")));
            if(!workspace.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException();
            Directory.CreateDirectory(workspace);
            var limit=ArchivePreviewPolicy.Kind(entry.Path)=="text"?ArchivePreviewPolicy.TextLimit:ArchivePreviewPolicy.ImageLimit;
            progress?.Report(new("preview-reading",UiText.Get("Preview.Loading")));
            var passwords=verified.VerifiedPasswords.ToDictionary(p=>p.Key,p=>p.Value.Secret);
            var result=await preview.ExtractPreviewAsync(archive,workspace,entry,limit,verified.Secret,passwords,
                new ForwardProgress<EngineProgress>(p=>progress?.Report(new("preview-reading",UiText.Get("Preview.Loading"),p))),token);
            if(!result.IsSuccess) return new(result,Warning:verified.Warning);
            var file=Path.GetFullPath(Path.Combine(workspace,relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!file.StartsWith(workspace+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException();
            var files=Directory.GetFiles(workspace,"*",SearchOption.AllDirectories);
            if(files.Length!=1 || !files[0].Equals(file,StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length!=(long)entry.Size)
                return new(new EngineResult {Code="preview_content_mismatch"});
            var content=await File.ReadAllBytesAsync(file,token);
            return new(result,content,verified.Warning);
        }
        try {outcome=await RunAsync();}
        catch(OperationCanceledException) {outcome=new(new EngineResult {Status="cancelled",Code="cancelled"});}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {outcome=new(new EngineResult {Code="preview_failed"});}
        finally
        {
            if(workspace is not null && Directory.Exists(workspace))
                try {Directory.Delete(workspace,true);}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {outcome=outcome with {Warning=UiText.Get("Preview.CleanupWarning")+"\n"+workspace};}
        }
        return outcome;
    }
}
