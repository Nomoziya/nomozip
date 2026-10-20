namespace ArchivePassword.Core;

public interface IPasswordOrganizationVault
{
    Task<int> MergeDuplicatesAsync(Guid keepId, IReadOnlyList<Guid> removeIds, CancellationToken token = default);
}

public static class PasswordOrganization
{
    public static bool ValidMetadata(SavedPassword value) => value.Group is { Length: <= 128 }
        && value.Source is { Length: <= 512 } && value.MatchText is { Length: <= 256 } && ValidDirectory(value.MatchDirectory);

    public static bool ValidDirectory(string? path)
    {
        if (path is null || path.Length > 1024) return false;
        if (path.Length == 0) return true;
        try { return Path.IsPathFullyQualified(path) && Path.GetFullPath(path).Length > 0; }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static bool MatchesDirectory(SavedPassword value, string archivePath)
    {
        if (value.MatchDirectory.Length == 0 || !ValidDirectory(value.MatchDirectory)) return false;
        try
        {
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.MatchDirectory));
            var parent = Path.GetDirectoryName(Path.GetFullPath(archivePath));
            return parent is not null && (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)
                || parent.StartsWith(Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    // A filename substring changes priority only. Non-matching candidates remain usable.
    public static bool MatchesArchive(SavedPassword value, string archivePath) => (!string.IsNullOrWhiteSpace(value.MatchText)
        && Path.GetFileName(archivePath).Contains(value.MatchText, StringComparison.OrdinalIgnoreCase)) || MatchesDirectory(value, archivePath);

    public static bool MatchesSearch(SavedPassword value, string search) => new[] { value.Name, value.Notes, value.Group, value.Source, value.MatchText, value.MatchDirectory }
        .Any(text => text.Contains(search, StringComparison.CurrentCultureIgnoreCase));

    public static SavedPassword Merge(SavedPassword keep, IReadOnlyList<SavedPassword> others)
    {
        if (others.Any(value => value.Id == keep.Id || !string.Equals(value.Secret, keep.Secret, StringComparison.Ordinal)))
            throw new ArgumentException("Only different records with the same password can be merged.");
        var records = others.Prepend(keep).ToArray();
        return keep with { CreatedAt = records.Min(value => value.CreatedAt), LastSuccessAt = records.Max(value => value.LastSuccessAt) };
    }
}
