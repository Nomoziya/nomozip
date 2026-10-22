using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private AsyncCommand? explainPasswordMatchesCommand;
    public ICommand ExplainPasswordMatchesCommand=>explainPasswordMatchesCommand ??= new AsyncCommand(async()=>
    {
        if(dialogs.SelectPasswordMatchArchive() is not {} path)return;
        await VaultOperationAsync(async()=>
        {
            RecordVaultActivity();
            var plan=await new PasswordCandidateProvider(vault).ExplainAsync(path);
            dialogs.ShowPasswordMatches(new(path,plan,AutoTry));
            RecordVaultActivity();
        });
    },()=>IsIdle && vaultAvailable && !VaultLocked);
}

public sealed class PasswordMatchViewModel(string path,PasswordMatchPlan plan,bool autoTry)
{
    public string ArchivePath=>path;
    public string ArchiveName=>System.IO.Path.GetFileName(path);
    public string Summary=>UiText.Format("PasswordMatch.Summary",plan.Entries.Count,plan.DisabledCount,plan.DuplicateCount);
    public string Notice=>UiText.Get(autoTry?"PasswordMatch.Notice":"PasswordMatch.AutoTryOff");
    public IReadOnlyList<PasswordMatchRow> Rows {get;}=plan.Entries.Select(entry=>new PasswordMatchRow(entry.Order,entry.Name,entry.Group,Reason(entry))).ToArray();
    private static string Reason(PasswordMatchEntry entry)
    {
        var reasons=new List<string>();
        if(entry.Associated)reasons.Add(UiText.Get("PasswordMatch.Associated"));
        if(entry.DirectoryMatch)reasons.Add(UiText.Get("PasswordMatch.Directory"));
        if(entry.NameMatch)reasons.Add(UiText.Get("PasswordMatch.Name"));
        if(entry.LastSuccessAt is {} last)reasons.Add(UiText.Format("PasswordMatch.Success",last.ToLocalTime().ToString("yyyy/MM/dd HH:mm")));
        if(reasons.Count==0)reasons.Add(UiText.Get("PasswordMatch.Fallback"));
        return string.Join(" · ",reasons);
    }
}
public sealed record PasswordMatchRow(int Order,string Name,string Group,string Reason);
