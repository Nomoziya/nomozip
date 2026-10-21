using System.ComponentModel;

namespace ArchivePassword.Desktop;

// Global presentation only: no archive paths, searches, selections or passwords.
public sealed record BrowserSortPreference(string Member, ListSortDirection Direction);
public sealed record BrowserColumnPreference(int Column, double Width);
public sealed record BrowserPreferences(bool GridView = false, IReadOnlyList<BrowserSortPreference>? Sorts = null,
    IReadOnlyList<BrowserColumnPreference>? Columns = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => (Sorts is null || Sorts.Count <= 3 && Sorts.All(item => item is not null
        && item.Member is "DisplayName" or "Entry.Size" or "Path" && Enum.IsDefined(item.Direction))
        && Sorts.Select(item => item.Member).Distinct().Count() == Sorts.Count)
        && (Columns is null || Columns.Count <= 5 && Columns.All(item => item is not null
            && item.Column is >= 1 and <= 5 && double.IsFinite(item.Width) && item.Width is >= 40 and <= 4096)
            && Columns.Select(item => item.Column).Distinct().Count() == Columns.Count);
}

public sealed partial class ArchiveContentsViewModel
{
    private BrowserPreferences browserPreferences = new();
    public BrowserPreferences BrowserPreferences => browserPreferences;
    public event EventHandler? BrowserLayoutReset;
    internal void RememberBrowserSort(IReadOnlyList<SortDescription> sorts) => browserPreferences = browserPreferences with
    { Sorts = sorts.Select(item => new BrowserSortPreference(item.PropertyName, item.Direction)).ToArray() };
    internal void RememberBrowserColumns(IReadOnlyList<BrowserColumnPreference> columns) => browserPreferences = browserPreferences with { Columns = columns };
    public System.Windows.Input.ICommand ResetBrowserLayoutCommand => new RelayCommand(() =>
    {
        GridView = false; browserPreferences = new(); BrowserLayoutReset?.Invoke(this, EventArgs.Empty);
    }, () => CanSelect);
}
