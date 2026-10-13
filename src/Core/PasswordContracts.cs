namespace ArchivePassword.Core;

public sealed record SavedPassword(Guid Id, string Name, string Secret, string Notes, bool AutoTry, DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt = null,
    string Group = "", string Source = "", string MatchText = "");
public sealed record PasswordCandidate(Guid? SavedId, string Secret);
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
        return records.Where(value => value.AutoTry)
            .OrderByDescending(value => value.Id == associated)
            .ThenByDescending(value => PasswordOrganization.MatchesArchive(value, archivePath))
            .ThenByDescending(value => value.LastSuccessAt)
            .ThenBy(value => value.CreatedAt)
            .DistinctBy(value => value.Secret, StringComparer.Ordinal)
            .Select(value => new PasswordCandidate(value.Id, value.Secret)).ToArray();
    }
}
