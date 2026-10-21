using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed partial class ArchiveContentsViewModel
{
    internal const int BackgroundViewThreshold=5000;
    private bool backgroundView,refreshing;
    private int viewRevision;
    private CancellationTokenSource? viewCancellation;
    private Task viewUpdate=Task.CompletedTask;
    private IReadOnlyList<SortDescription> viewSorts=[];
    internal bool UsesBackgroundView=>backgroundView;
    internal void EnableBackgroundView()=>backgroundView=allRows.Count>=BackgroundViewThreshold;
    public bool IsRefreshing {get=>refreshing;private set {if(Set(ref refreshing,value)){Changed(nameof(CanSelect));Changed(nameof(ViewStatus));CommandManager.InvalidateRequerySuggested();}}}
    public string ViewStatus=>IsRefreshing?UiText.Get("Browser.Updating"):OperationMessage;
    internal void SetViewSort(IReadOnlyList<SortDescription> fields)
    {
        if(viewSorts.SequenceEqual(fields))return;
        viewSorts=fields.ToArray();
        if(UsesBackgroundView)QueueViewUpdate();
    }
    internal void CancelViewUpdate()
    {
        viewRevision++;var previous=viewCancellation;viewCancellation=null;previous?.Cancel();IsRefreshing=false;
    }
    private void QueueViewUpdate()
    {
        viewCancellation?.Cancel();var cancellation=new CancellationTokenSource();viewCancellation=cancellation;
        var revision=++viewRevision;
        var valid=ArchiveEntryFilter.TryCreate(filterExtensions,filterMinimum,filterMaximum,filterKindIndex,out var filter);
        FilterError=valid?"":UiText.Get("Browser.FilterError");
        var query=search;var folder=currentFolder;var selectedOnly=showSelectedOnly;
        var across=selectedOnly || query.Length>0 || filterExtensions.Length>0 || filterMinimum.Length>0 || filterMaximum.Length>0 || filterKindIndex!=0;
        var selected=selectedOnly?selectedRows.ToHashSet():null;
        var rows=orderedRows;var fields=viewSorts;var culture=CultureInfo.CurrentCulture;
        IsRefreshing=true;
        viewUpdate=UpdateAsync();
        async Task UpdateAsync()
        {
            try
            {
                var result=await Task.Run(()=>
                {
                    var matches=new List<ArchiveContentRow>();var visible=new List<ArchiveContentRow>();
                    for(var index=0;index<rows.Count;index++)
                    {
                        if((index&255)==0)cancellation.Token.ThrowIfCancellationRequested();
                        var row=rows[index];
                        if(!valid || selectedOnly && !selected!.Contains(row) || !row.Path.Contains(query,StringComparison.CurrentCultureIgnoreCase) || !filter.Matches(row.Entry))continue;
                        matches.Add(row);if(across || string.Equals(row.ParentPath,folder,StringComparison.OrdinalIgnoreCase))visible.Add(row);
                    }
                    var comparer=new ArchiveContentComparer(fields,culture,across);
                    var ordered=fields.Count==0?visible.ToArray():visible.OrderBy(row=>row,Comparer<ArchiveContentRow>.Create((left,right)=>comparer.Compare(left,right))).ToArray();
                    cancellation.Token.ThrowIfCancellationRequested();return(matches:matches.ToArray(),visible:ordered);
                },cancellation.Token);
                if(revision!=viewRevision)return;
                ApplyView(result.matches,result.visible,across);
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
            finally
            {
                if(revision==viewRevision){IsRefreshing=false;viewCancellation=null;}
                cancellation.Dispose();
            }
        }
    }
}
