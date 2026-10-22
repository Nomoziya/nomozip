namespace ArchivePassword.Core;

public sealed record SavedPassword(Guid Id, string Name, string Secret, string Notes, bool AutoTry, DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt = null,
    string Group = "", string Source = "", string MatchText = "", string MatchDirectory = "");
public sealed record PasswordCandidate(Guid? SavedId, string Secret);
public sealed record VaultBackupState(IReadOnlyList<SavedPassword> Passwords, string ContentStamp);
public interface IPasswordVaultBackupState
{
    Task<VaultBackupState> ReadBackupStateAsync(CancellationToken cancellationToken = default);
}
public interface IPasswordVault
{
    Task<IReadOnlyList<SavedPassword>> ReadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SavedPassword password, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task RecordSuccessAsync(Guid id, string archivePath, CancellationToken cancellationToken = default);
    Task<Guid?> GetAssociatedPasswordAsync(string archivePath, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(CancellationToken cancellationToken = default);
    Task<PasswordImportPlan> ImportAsync(IReadOnlyList<SavedPassword> passwords, PasswordImportMode mode, CancellationToken cancellationToken = default);
}
public interface IPasswordCandidateProvider
{
    Task<IReadOnlyList<PasswordCandidate>> GetAsync(string archivePath, CancellationToken cancellationToken = default);
}
public sealed class VaultException(string code, bool backupAvailable = false) : Exception(code)
{
    public string Code { get; } = code;
    public bool BackupAvailable { get; } = backupAvailable;
}
public sealed class PasswordCandidateProvider(IPasswordVault vault) : IPasswordCandidateProvider
{
    public async Task<IReadOnlyList<PasswordCandidate>> GetAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        var associated = await vault.GetAssociatedPasswordAsync(archivePath, cancellationToken);
        var records = await vault.ReadAsync(cancellationToken);
        return Order(records,archivePath,associated).Select(value => new PasswordCandidate(value.Id, value.Secret)).ToArray();
    }
    internal static IEnumerable<SavedPassword> Order(IReadOnlyList<SavedPassword> records,string archivePath,Guid? associated)=>records.Where(value => value.AutoTry)
            .OrderByDescending(value => value.Id == associated)
            .ThenByDescending(value => PasswordOrganization.MatchesArchive(value, archivePath))
            .ThenByDescending(value => value.LastSuccessAt)
            .ThenBy(value => value.CreatedAt)
            .DistinctBy(value => value.Secret, StringComparer.Ordinal);
    public async Task<PasswordMatchPlan> ExplainAsync(string archivePath,CancellationToken cancellationToken=default)
    {
        var associated=await vault.GetAssociatedPasswordAsync(archivePath,cancellationToken);
        var records=await vault.ReadAsync(cancellationToken);
        var candidates=Order(records,archivePath,associated).Select((value,index)=>new PasswordMatchEntry(index+1,value.Id,value.Name,value.Group,
            value.Id==associated,PasswordOrganization.MatchesDirectory(value,archivePath),
            !string.IsNullOrWhiteSpace(value.MatchText) && Path.GetFileName(archivePath).Contains(value.MatchText,StringComparison.OrdinalIgnoreCase),value.LastSuccessAt)).ToArray();
        var enabled=records.Count(value=>value.AutoTry);
        return new(candidates,records.Count-enabled,enabled-candidates.Length);
    }
}
public sealed record PasswordMatchEntry(int Order,Guid Id,string Name,string Group,bool Associated,bool DirectoryMatch,bool NameMatch,DateTimeOffset? LastSuccessAt);
public sealed record PasswordMatchPlan(IReadOnlyList<PasswordMatchEntry> Entries,int DisabledCount,int DuplicateCount);
