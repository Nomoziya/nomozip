namespace ArchivePassword.Core;

public sealed record ExistingFileConflict(string TargetPath, string SourcePath, IReadOnlySet<string>? ReservedTargets = null);
public sealed record ExistingFileDecision(ExistingFilePolicy Policy, string? NewName = null, bool ApplyToAll = false);

public static class ConflictFileNames
{
    public static bool IsValid(string? name)
    {
        if(string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')
            || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.Contains('/') || name.Contains('\\')) return false;
        var stem=name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL") && !(stem.Length==4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }
    public static string Suggest(string target, Func<string,bool>? reserved = null)
    {
        var directory=Path.GetDirectoryName(target)!;var stem=Path.GetFileNameWithoutExtension(target);var extension=Path.GetExtension(target);
        for(var number=2;;number++)
        {
            var name=$"{stem} ({number}){extension}";var path=Path.Combine(directory,name);
            if(!File.Exists(path) && !Directory.Exists(path) && reserved?.Invoke(path)!=true) return name;
        }
    }
}
