namespace ArchivePassword.Core;

public static class ArchiveSelection
{
    public static IReadOnlyList<ArchiveEntry> Expand(IReadOnlyList<ArchiveEntry> items,IEnumerable<uint> selected)
    {
        var ids=selected.ToHashSet();var map=items.ToDictionary(item=>item.Index);
        if(ids.Count==0 || ids.Any(id=>!map.ContainsKey(id))) throw new ArgumentException("Select existing archive entries.",nameof(selected));
        var folders=ids.Select(id=>map[id]).Where(item=>item.Directory).Select(item=>Normalize(item.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Included(ArchiveEntry item)
        {
            if(ids.Contains(item.Index)) return true;
            var path=Normalize(item.Path);var slash=path.LastIndexOf('/');
            while(slash>0) {if(folders.Contains(path[..slash])) return true;slash=path.LastIndexOf('/',slash-1);}
            return false;
        }
        return items.Where(Included).OrderBy(item=>item.Index).ToArray();
    }
    private static string Normalize(string path)=>path.Replace('\\','/').TrimEnd('/');
}

public interface IArchiveSelectionEngine
{
    Task<EngineResult> TestSelectionAsync(string archive,IReadOnlyList<ArchiveEntry> selection,string? password,
        IReadOnlyDictionary<uint,string> itemPasswords,IProgress<EngineProgress>? progress=null,CancellationToken token=default);
    Task<EngineResult> ExtractSelectionAsync(string archive,string output,IReadOnlyList<ArchiveEntry> selection,string? password,
        IReadOnlyDictionary<uint,string> itemPasswords,IProgress<EngineProgress>? progress=null,CancellationToken token=default);
}
