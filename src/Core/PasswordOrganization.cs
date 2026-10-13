namespace ArchivePassword.Core;

public interface IPasswordOrganizationVault
{
    Task<int> MergeDuplicatesAsync(Guid keepId, IReadOnlyList<Guid> removeIds, CancellationToken token = default);
}

public static class PasswordOrganization
{
    public static bool ValidMetadata(SavedPassword value) => value.Group is { Length: <= 128 }
        && value.Source is { Length: <= 512 } && value.MatchText is { Length: <= 256 };

    // A filename substring changes priority only. Non-matching candidates remain usable.
    public static bool MatchesArchive(SavedPassword value, string archivePath) => !string.IsNullOrWhiteSpace(value.MatchText)
        && Path.GetFileName(archivePath).Contains(value.MatchText, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesSearch(SavedPassword value, string search) => new[] { value.Name, value.Notes, value.Group, value.Source, value.MatchText }
        .Any(text => text.Contains(search, StringComparison.CurrentCultureIgnoreCase));

    public static SavedPassword Merge(SavedPassword keep, IReadOnlyList<SavedPassword> others)
    {
        if (others.Any(value => value.Id == keep.Id || !string.Equals(value.Secret, keep.Secret, StringComparison.Ordinal)))
            throw new ArgumentException("Only different records with the same password can be merged.");
        var records = others.Prepend(keep).ToArray();
        return keep with { CreatedAt = records.Min(value => value.CreatedAt), LastSuccessAt = records.Max(value => value.LastSuccessAt) };
    }
}
