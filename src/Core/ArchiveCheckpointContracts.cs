namespace ArchivePassword.Core;

public interface IArchiveCheckpointEngine
{
    Task<EngineResult> ExtractCheckpointAsync(string archive,string output,IReadOnlyList<ArchiveEntry> selection,string? password,
        IReadOnlyDictionary<uint,string> itemPasswords,Func<uint,CancellationToken,Task> saveCompleted,
        IProgress<EngineProgress>? progress=null,CancellationToken token=default);
}
