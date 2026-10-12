namespace ArchivePassword.Core;

public enum PasswordImportMode { SkipDuplicates, KeepBoth }
public sealed record PasswordImportRow(SavedPassword Password, bool Duplicate);
public sealed record PasswordImportPlan(IReadOnlyList<PasswordImportRow> Rows, IReadOnlyList<SavedPassword> Additions)
{
    public int Added => Additions.Count;
    public int Skipped => Rows.Count - Added;
}

public static class PasswordImportPlanner
{
    public static PasswordImportPlan Build(IReadOnlyList<SavedPassword> existing, IReadOnlyList<SavedPassword> incoming, PasswordImportMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var secrets = existing.Select(value => value.Secret).ToHashSet(StringComparer.Ordinal);
        var ids = existing.Select(value => value.Id).ToHashSet();
        var rows = new List<PasswordImportRow>();
        var additions = new List<SavedPassword>();
        foreach (var value in incoming)
        {
            var duplicate = !secrets.Add(value.Secret);
            rows.Add(new(value, duplicate));
            if (duplicate && mode == PasswordImportMode.SkipDuplicates) continue;
            var added = value;
            while (!ids.Add(added.Id)) added = added with { Id = Guid.NewGuid() };
            additions.Add(added);
        }
        return new(rows, additions);
    }
}

public sealed class VaultTransferException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
