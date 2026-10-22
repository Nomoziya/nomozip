using System.Windows.Input;

namespace ArchivePassword.Desktop;

public sealed partial class ArchiveContentsViewModel
{
    private int searchScopeIndex;
    private AsyncCommand? locateCurrentEntryCommand;
    public IReadOnlyList<string> SearchScopeChoices {get;}=[UiText.Get("Browser.ScopeArchive"),UiText.Get("Browser.ScopeFolder")];
    public int SearchScopeIndex
    {
        get=>searchScopeIndex;
        set {if(Set(ref searchScopeIndex,Math.Clamp(value,0,1)))FilterRows();}
    }
    public ICommand LocateCurrentEntryCommand=>locateCurrentEntryCommand ??= new AsyncCommand(LocateCurrentEntryAsync,()=>CanSelect && CurrentRow is not null);
    public event EventHandler? EntryLocationRequested;
    private static bool InSearchScope(ArchiveContentRow row,string? scope)=>string.IsNullOrEmpty(scope)
        || row.ParentPath.StartsWith(scope,StringComparison.OrdinalIgnoreCase)
            && (row.ParentPath.Length==scope.Length || row.ParentPath[scope.Length]=='/');
    private async Task LocateCurrentEntryAsync()
    {
        if(CurrentRow is not {} target || !CanSelect)return;
        Navigate(target.ParentPath);
        var revision=viewRevision;
        await WaitForIdleAsync();
        if(revision!=viewRevision || !CanSelect)return;
        CurrentRow=VisibleRows.FirstOrDefault(row=>row.Path==target.Path);
        if(CurrentRow is not null)EntryLocationRequested?.Invoke(this,EventArgs.Empty);
    }
}
