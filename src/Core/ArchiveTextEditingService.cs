using System.Security.Cryptography;
using System.Text;

namespace ArchivePassword.Core;

public sealed record ArchiveTextEditStart(EngineResult Engine, ArchiveTextEditSession? Session=null, string? Warning=null);

// Text copies are private to this session; no external program is launched.
public sealed class ArchiveTextEditingService(IArchiveEngine engine,IArchiveEditor editor,IPasswordCandidateProvider candidates,string temporaryRoot)
{
    public async Task<ArchiveTextEditStart> BeginAsync(string archive,ArchiveEntry entry,bool autoTry,
        Func<EngineResult,Task<PasswordPromptResponse?>> askPassword,IProgress<TaskUpdate>? progress=null,CancellationToken token=default)
    {
        if(ArchivePreviewPolicy.Kind(entry.Path)!="text" || ArchivePreviewPolicy.Rejection(entry)!="") return new(new EngineResult {Code="preview_unsupported"});
        using var sourceLock=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
        var hash=Convert.ToHexString(await SHA256.HashDataAsync(sourceLock,token));
        var preview=await new ArchivePreviewService(engine,candidates,temporaryRoot).ReadAsync(archive,entry,autoTry,askPassword,progress,token);
        if(!preview.Engine.IsSuccess || preview.Content is null) return new(preview.Engine,Warning:preview.Warning);
        try
        {
            var (text,encoding)=ArchivePreviewPolicy.DecodeText(preview.Content);
            var session=await ArchiveTextEditSession.CreateAsync(editor,engine,candidates,temporaryRoot,archive,entry,hash,preview.Content,text,encoding,token);
            return new(preview.Engine,session,preview.Warning);
        }
        catch(DecoderFallbackException) {return new(new EngineResult {Code="preview_unsupported"});}
    }
}

public sealed class ArchiveTextEditSession : IDisposable
{
    private readonly IArchiveEditor editor;
    private readonly IArchiveEngine engine;
    private readonly IPasswordCandidateProvider candidates;
    private readonly string archive,hash,workspace;
    private readonly byte[] original;
    private readonly Encoding encoding;
    private readonly byte[] preamble;
    public ArchiveEntry Entry {get;}
    public string OriginalText {get;}
    public string EncodingName=>encoding.WebName;
    public string CopyPath {get;}
    public string? CleanupWarning {get;private set;}
    private ArchiveTextEditSession(IArchiveEditor editor,IArchiveEngine engine,IPasswordCandidateProvider candidates,string workspace,string archive,ArchiveEntry entry,string hash,byte[] original,string text,string encodingName)
    {
        this.editor=editor;this.engine=engine;this.candidates=candidates;this.workspace=workspace;this.archive=archive;Entry=entry;this.hash=hash;this.original=original;OriginalText=text;
        encoding=Encoding.GetEncoding(encodingName,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
        var signature=encoding.GetPreamble();
        // UTF8's named encoding includes a BOM, while BOM-less input must stay BOM-less.
        preamble=signature.Length>0 && original.AsSpan().StartsWith(signature)?signature:[];
        CopyPath=Path.Combine(workspace,"copy.txt");
    }
    internal static async Task<ArchiveTextEditSession> CreateAsync(IArchiveEditor editor,IArchiveEngine engine,IPasswordCandidateProvider candidates,string root,string archive,ArchiveEntry entry,string hash,byte[] bytes,string text,string encoding,CancellationToken token)
    {
        var workspace=Path.Combine(Path.GetFullPath(root),"edit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(workspace);
        var session=new ArchiveTextEditSession(editor,engine,candidates,workspace,archive,entry,hash,bytes,text,encoding);
        try {await File.WriteAllBytesAsync(session.CopyPath,bytes,token);return session;}
        catch {session.Dispose();throw;}
    }
    public byte[] Encode(string text)=>text==OriginalText?original:preamble.Concat(encoding.GetBytes(text)).ToArray();
    public async Task<ArchiveEditOutcome> WriteBackAsync(string text,IReadOnlyList<ArchiveEntry> snapshot,bool autoTry,
        Func<EngineResult,Task<PasswordPromptResponse?>> askPassword,IProgress<TaskUpdate>? progress=null,CancellationToken token=default)
    {
        if(text==OriginalText) return new(new EngineResult {Code="empty_changes"},snapshot);
        if(!snapshot.Contains(Entry)) return new(new EngineResult {Code="selection_changed"},snapshot);
        var bytes=Encode(text);
        if(bytes.Length>(long)ArchivePreviewPolicy.TextLimit) return new(new EngineResult {Code="preview_too_large"},snapshot);
        await File.WriteAllBytesAsync(CopyPath,bytes,token);
        return await new ArchiveEditingService(engine,editor,candidates).EditAsync(archive,snapshot,[Entry.Index],[new(CopyPath,Entry.Path,false)],autoTry,askPassword,progress,token,hash);
    }
    public void Dispose()
    {
        try {if(Directory.Exists(workspace))Directory.Delete(workspace,true);CleanupWarning=null;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {CleanupWarning=workspace;}
    }
}
