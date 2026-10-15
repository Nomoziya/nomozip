using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed record PasswordGroupFilter(string? Value, string Label);

public sealed partial class MainViewModel
{
    private PasswordGroupFilter? selectedPasswordGroup;
    public ObservableCollection<PasswordGroupFilter> PasswordGroups { get; } = [];
    public PasswordGroupFilter? SelectedPasswordGroup
    {
        get => selectedPasswordGroup;
        set { if (Set(ref selectedPasswordGroup, value)) FilterPasswords(); }
    }
    public ICommand MergePasswordsCommand { get; private set; } = null!;
    public string PasswordListSummary => UiText.Format("T_397dc2bf2897", PasswordRows.Count, allPasswords.Count);
    public string DuplicateSummary => SelectedPassword is { } value && DuplicateIds(value).Length > 0
        ? UiText.Format("T_a2cd3d94cd10", DuplicateIds(value).Length) : "";
    private Guid[] DuplicateIds(SavedPassword keep) => allPasswords.Where(value => value.Id != keep.Id && value.Secret == keep.Secret).Select(value => value.Id).ToArray();
    private void InitializePasswordOrganizationCommands() => MergePasswordsCommand = new AsyncCommand(MergePasswordsAsync,
        () => IsIdle && vaultAvailable && vault is IPasswordOrganizationVault && SelectedPassword is { } value && DuplicateIds(value).Length > 0);
    private void RefreshPasswordGroups()
    {
        var prior = selectedPasswordGroup?.Value;
        PasswordGroups.Clear(); PasswordGroups.Add(new(null, UiText.Get("T_140e85a15cfe")));
        foreach (var group in allPasswords.Select(value => value.Group).Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCultureIgnoreCase))
            PasswordGroups.Add(new(group, group.Length == 0 ? UiText.Get("T_e86ea3f280d9") : group));
        SelectedPasswordGroup = PasswordGroups.FirstOrDefault(value => value.Value == prior) ?? PasswordGroups[0];
    }
    private async Task MergePasswordsAsync()
    {
        if (SelectedPassword is not { } keep || vault is not IPasswordOrganizationVault organization) return;
        var ids = DuplicateIds(keep);
        if (ids.Length == 0 || !dialogs.ConfirmMergePasswords(keep.Name, ids.Length)) return;
        IsBusy = true; idle = new TaskCompletionSource();
        try
        {
            var removed = await organization.MergeDuplicatesAsync(keep.Id, ids);
            await RefreshVaultAsync(); SelectedPassword = allPasswords.FirstOrDefault(value => value.Id == keep.Id);
            Banner = UiText.Format("T_f6b557598ac8", removed);
        }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException)
        { Banner = UiText.Get("T_c9936e785515"); }
        finally { IsBusy = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
    }
}
