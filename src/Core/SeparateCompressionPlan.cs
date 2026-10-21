namespace ArchivePassword.Core;

public sealed record SeparateCompressionItem(string SourcePath, string OutputPath);

public static class SeparateCompressionPlan
{
    public static IReadOnlyList<SeparateCompressionItem> Create(IEnumerable<string> sources, string outputDirectory, string extension)
    {
        var directory=Path.GetFullPath(outputDirectory);
        var sourcePaths=sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Later sources must not collect archives produced for earlier sources.
        if(sourcePaths.Any(source=>Directory.Exists(source) && IsWithin(directory,source)))
            throw new InvalidDataException("batch_output_inside_source");
        var reserved=new HashSet<string>(sourcePaths,StringComparer.OrdinalIgnoreCase);
        var result=new List<SeparateCompressionItem>();
        foreach(var source in sourcePaths)
        {
            var name=Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
            if(name.Length==0)throw new InvalidDataException("invalid_source");
            var stem=Directory.Exists(source)?name:Path.GetFileNameWithoutExtension(name);
            if(stem.Length==0)stem=name;
            var candidate=Path.Combine(directory,stem+extension);var suffix=2;
            while(reserved.Contains(candidate) || VolumeCompressionService.HasExistingTarget(candidate))
                candidate=Path.Combine(directory,$"{stem} ({suffix++}){extension}");
            reserved.Add(candidate);result.Add(new(source,candidate));
        }
        return result;
    }

    public static bool IsWithin(string directory,string sourceDirectory)
    {
        var source=Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var output=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return output.Equals(source,StringComparison.OrdinalIgnoreCase)
            || output.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
}
