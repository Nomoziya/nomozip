using System.ComponentModel;
using System.Windows.Data;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private int settingsSection;
    private bool preparingExtraction;
    public int SettingsSection { get => settingsSection; set => Set(ref settingsSection, Math.Clamp(value, 0, 5)); }
    public IReadOnlyList<string> SettingsCategories { get; } = [UiText.Get("Redesign.General"), UiText.Get("Redesign.FileOperations"), UiText.Get("Redesign.Security"), UiText.Get("Redesign.Integration"), UiText.Get("Redesign.Updates"), UiText.Get("Redesign.About")];
    public bool PreparingExtraction { get => preparingExtraction; internal set { if (Set(ref preparingExtraction, value)) { Changed(nameof(CanConfigureExtraction)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } } }
    public bool CanConfigureExtraction => IsIdle || PreparingExtraction;
    public bool HasCompressionActivity => CompressionRunning || CompressionOutput.Length > 0 && compressionActivityStarted;
    private bool compressionActivityStarted;
    private int taskFilterIndex;
    private ListCollectionView? taskView;
    public ICollectionView TaskView => taskView ??= new ListCollectionView(Tasks) { Filter = value => MatchesTaskFilter((ArchiveTaskRow)value) };
    public int TaskFilterIndex { get => taskFilterIndex; set { if (Set(ref taskFilterIndex,Math.Clamp(value,0,3))) { taskView?.Refresh(); Changed(nameof(ShowCompressionActivity)); } } }
    public IReadOnlyList<string> TaskFilters {get;} = [UiText.Get("Redesign.AllTasks"),UiText.Get("Redesign.RunningTasks"),UiText.Get("Redesign.PendingTasks"),UiText.Get("Redesign.EndedTasks")];
    public IReadOnlyList<TaskFilterBadge> TaskFilterBadges {get;} = new[] {"Redesign.AllTasks","Redesign.RunningTasks","Redesign.PendingTasks","Redesign.EndedTasks"}.Select(key=>new TaskFilterBadge(UiText.Get(key))).ToArray();
    public bool ShowCompressionActivity => HasCompressionActivity && (TaskFilterIndex==0 || TaskFilterIndex==1 && CompressionRunning || TaskFilterIndex==3 && !CompressionRunning);
    private bool MatchesTaskFilter(ArchiveTaskRow row) => TaskFilterIndex switch {1=>row.Running,2=>!row.Running && !row.Completed && row.Failure is null,3=>row.Completed || row.Failure is not null,_=>true};
    private void RefreshWorkspaceTasks(bool refreshView = true)
    {
        // Collection changes already reach ListCollectionView. Refreshing during
        // its source notification would make it process the new row twice.
        if (refreshView) taskView?.Refresh();
        for(var index=0;index<4;index++) TaskFilterBadges[index].Count=Tasks.Count(row=>index switch {1=>row.Running,2=>!row.Running && !row.Completed && row.Failure is null,3=>row.Completed || row.Failure is not null,_=>true});
        Changed(nameof(TaskSummary));Changed(nameof(ShowCompressionActivity));
    }
    private void WorkspaceTaskChanged(object? sender,PropertyChangedEventArgs e)
    {if(e.PropertyName is nameof(ArchiveTaskRow.Running) or nameof(ArchiveTaskRow.Completed) or nameof(ArchiveTaskRow.Failure)) RefreshWorkspaceTasks();}

    internal async Task SaveVaultDetailsAsync(SavedPassword edited)
    {
        if (!IsIdle || !vaultAvailable || VaultLocked) return;
        var duplicates = allPasswords.Count(value => value.Id != edited.Id && value.Secret == edited.Secret);
        if (duplicates > 0 && !dialogs.ConfirmDuplicatePassword(duplicates)) return;
        IsBusy = true; idle = new TaskCompletionSource();
        try { await vault.SaveAsync(edited); await RefreshVaultAsync(); SelectedPassword = allPasswords.FirstOrDefault(value => value.Id == edited.Id); Banner = ""; }
        catch (Exception error) when (error is VaultException or System.IO.IOException or UnauthorizedAccessException) { Banner = UiText.Get("T_91d7521c9927"); }
        finally { IsBusy = false; idle.TrySetResult(); }
    }

    internal async Task OpenWorkspaceArchiveAsync(IEnumerable<string> paths)
    {
        if (!IsIdle) return;
        var added = paths.Where(System.IO.File.Exists).Select(System.IO.Path.GetFullPath).ToArray();
        AddFiles(added);
        SelectedTask = Tasks.FirstOrDefault(value => added.Contains(value.Path, StringComparer.OrdinalIgnoreCase));
        Page = 4;
        if(added.Length>1) {Page=0;return;}
        if (SelectedTask is not null) await BrowseArchiveAsync();
    }
}

public sealed class TaskFilterBadge(string label) : ObservableObject
{
    public string Label {get;}=label;
    private int count;
    public int Count {get=>count;set=>Set(ref count,value);}
}
