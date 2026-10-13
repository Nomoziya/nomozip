namespace ArchivePassword.Core;

public enum ExtractionLocation { Custom, ArchiveParent, Desktop, Documents }
public enum ExtractionLayout { Direct, ArchiveFolder, Smart }
public enum ContainerCollision { Numbered, Merge }
public enum ContainerNaming { ArchiveName, ArchiveNameDate }
public sealed record ExtractionDirectoryRules(ExtractionLocation Location = ExtractionLocation.Custom,
    ExtractionLayout Layout = ExtractionLayout.Direct, ContainerCollision Collision = ContainerCollision.Numbered,
    ContainerNaming Naming = ContainerNaming.ArchiveName, bool OpenAfterSuccess = false);

public static class ExtractionDirectoryPlanner
{
    public static string BaseDirectory(string archive, string custom, ExtractionLocation location) => Path.GetFullPath(location switch
    {
        ExtractionLocation.ArchiveParent => Path.GetDirectoryName(Path.GetFullPath(archive))!,
        ExtractionLocation.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        ExtractionLocation.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        _ => custom
    });

    public static string Resolve(string archive, string root, ExtractionDirectoryRules rules, IReadOnlyList<ArchiveEntry> items, DateOnly date)
    {
        root = Path.GetFullPath(root);
        if (rules.Layout == ExtractionLayout.Direct || rules.Layout == ExtractionLayout.Smart && HasSingleRoot(items)) return root;
        var name = Path.GetFileNameWithoutExtension(archive);
        if (int.TryParse(Path.GetExtension(archive).TrimStart('.'), out _) && Path.GetExtension(name).ToLowerInvariant() is ".7z" or ".zip" or ".rar")
            name = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(name)) name = "解压文件";
        if (rules.Naming == ContainerNaming.ArchiveNameDate) name += "_" + date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        var destination = Path.Combine(root,name);
        if (rules.Collision == ContainerCollision.Merge)
        {
            if (File.Exists(destination)) throw new IOException("A file occupies the output directory name.");
            return destination;
        }
        for (var suffix=2; Directory.Exists(destination) || File.Exists(destination); suffix++) destination=Path.Combine(root,$"{name} ({suffix})");
        return destination;
    }

    public static bool HasSingleRoot(IReadOnlyList<ArchiveEntry> items)
    {
        var paths = items.Select(item=>(Item:item, Parts:item.Path.Replace('\\','/').TrimEnd('/').Split('/'))).ToArray();
        if (paths.Length == 0) return true;
        if (paths.Any(value=>value.Parts.Any(part=>part is "" or "." or ".."))) return false;
        if (paths.Length == 1 && !paths[0].Item.Directory && paths[0].Parts.Length == 1) return true;
        var root = paths[0].Parts[0];
        return paths.All(value=>value.Parts[0].Equals(root,StringComparison.OrdinalIgnoreCase) && (value.Parts.Length>1 || value.Item.Directory));
    }
}
