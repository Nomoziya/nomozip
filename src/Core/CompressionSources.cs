namespace ArchivePassword.Core;
internal static class CompressionSources
{
    internal static async Task<IReadOnlyList<CompressionEntry>> CollectAsync(IEnumerable<string> sources,string destination,CancellationToken cancellationToken,
        CompressionExclusions? exclusions=null,Action<CompressionEntry>? excluded=null)
    {
        var entries=new List<CompressionEntry>();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Include(string source, string name)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source = Path.GetFullPath(source);
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) || !sourcePaths.Add(source)) return;
            var attributes = File.GetAttributes(source);
            var directory = (attributes & FileAttributes.Directory) != 0;
            if(exclusions?.Excludes(name,directory)==true){excluded?.Invoke(new(source,name,directory));return;}
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("unsupported_link");
            if (paths.TryGetValue(name, out var previous) && !previous.Equals(source, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("duplicate_archive_path");
            paths[name] = source;
            entries.Add(new(source, name, directory));
            if (directory)
                foreach (var child in Directory.EnumerateFileSystemEntries(source)) Include(child, name + "/" + Path.GetFileName(child));
        }
        // Enumerate before creating the temporary archive, so it cannot include itself.
        await Task.Run(() =>
        {
            foreach (var source in sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value.Length))
            {
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
                if (string.IsNullOrEmpty(name)) throw new InvalidDataException("invalid_source");
                Include(source, name);
            }
        }, cancellationToken);
        return entries;
    }
}
