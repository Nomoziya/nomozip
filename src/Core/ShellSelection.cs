namespace ArchivePassword.Core;

public enum ShellAction { ExtractHere, ExtractFolder, Compress }
public sealed record ShellSelection(ShellAction Action, IReadOnlyList<string> Paths)
{
    public ShellSelection Validate()
    {
        if (!Enum.IsDefined(Action) || Paths.Count is 0 or > 10_000) throw new ArgumentException("Invalid shell selection.");
        var normalized = Paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Any(path => Action == ShellAction.Compress ? !File.Exists(path) && !Directory.Exists(path)
            : !File.Exists(path) || !new[] { ".7z", ".zip", ".rar" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Selection contains missing or unsupported items.");
        return this with { Paths = normalized };
    }
    public string OutputFor(string archive)
    {
        var name = Path.GetFileNameWithoutExtension(archive);
        if (string.IsNullOrEmpty(name)) name = UiText.Get("T_b32af2a5114f");
        return Action == ShellAction.ExtractFolder ? Path.Combine(Path.GetDirectoryName(archive)!, name) : Path.GetDirectoryName(archive)!;
    }
}
