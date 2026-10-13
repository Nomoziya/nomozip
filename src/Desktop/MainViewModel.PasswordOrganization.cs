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
    public string PasswordListSummary => $"显示 {PasswordRows.Count} / {allPasswords.Count} 条";
    public string DuplicateSummary => SelectedPassword is { } value && DuplicateIds(value).Length > 0
        ? $"所选密码另有 {DuplicateIds(value).Length} 条相同密码记录，可合并。" : "";
    private Guid[] DuplicateIds(SavedPassword keep) => allPasswords.Where(value => value.Id != keep.Id && value.Secret == keep.Secret).Select(value => value.Id).ToArray();
    private void InitializePasswordOrganizationCommands() => MergePasswordsCommand = new AsyncCommand(MergePasswordsAsync,
        () => IsIdle && vaultAvailable && vault is IPasswordOrganizationVault && SelectedPassword is { } value && DuplicateIds(value).Length > 0);
    private void RefreshPasswordGroups()
    {
        var prior = selectedPasswordGroup?.Value;
        PasswordGroups.Clear(); PasswordGroups.Add(new(null, "全部分组"));
        foreach (var group in allPasswords.Select(value => value.Group).Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCultureIgnoreCase))
            PasswordGroups.Add(new(group, group.Length == 0 ? "未分组" : group));
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
            Banner = $"已合并 {removed} 条重复记录，压缩包关联已保留。";
        }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException)
        { Banner = "未能合并重复密码。原密码库已保留，请刷新后重试。"; }
        finally { IsBusy = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
    }
}
