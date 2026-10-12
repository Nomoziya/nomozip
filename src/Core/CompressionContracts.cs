namespace ArchivePassword.Core;

public sealed record CompressionEntry(string SourcePath, string Path, bool Directory);
public sealed record CompressionRequest(IReadOnlyList<CompressionEntry> Entries, string Format, int Level, bool EncryptHeaders);
public interface IArchiveCreator
{
    Task<EngineResult> CreateAsync(CompressionRequest request, string outputPath, string? password,
        IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default);
}
public sealed record CompressionOutcome(EngineResult Engine, string OutputPath);

public sealed class CompressionService(IArchiveCreator creator, IArchiveEngine validator)
{
    public async Task<CompressionOutcome> CreateAsync(IEnumerable<string> sources, string outputPath, string format,
        int level = 5, string? password = null, bool encryptHeaders = true, bool replace = false,
        IProgress<TaskUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        string? temporary = null;
        EngineResult Failure(string code) => new() { Code = code };
        try
        {
            if (format is not ("7z" or "zip") || level is not (0 or 1 or 3 or 5 or 7 or 9)) return new(Failure("invalid_compression_options"), outputPath);
            if (format == "zip" && password?.Any(value => value > 127) == true) return new(Failure("zip_password_ascii_only"), outputPath);
            var destination = Path.GetFullPath(outputPath);
            if (File.Exists(destination) && !replace) return new(Failure("output_exists"), destination);
            var entries = new List<CompressionEntry>();
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Include(string source, string name)
            {
                cancellationToken.ThrowIfCancellationRequested();
                source = Path.GetFullPath(source);
                if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) || !sourcePaths.Add(source)) return;
                var attributes = File.GetAttributes(source);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("unsupported_link");
                if (paths.TryGetValue(name, out var previous) && !previous.Equals(source, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("duplicate_archive_path");
                paths[name] = source;
                var directory = (attributes & FileAttributes.Directory) != 0;
                entries.Add(new(source, name, directory));
                if (directory)
                    foreach (var child in Directory.EnumerateFileSystemEntries(source)) Include(child, name + "/" + Path.GetFileName(child));
            }
            // Enumerate before creating the temporary archive, so it cannot include itself.
            progress?.Report(new("preparing", "正在准备文件…"));
            await Task.Run(() =>
            {
                foreach (var source in sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value.Length))
                {
                    var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
                    if (string.IsNullOrEmpty(name)) throw new InvalidDataException("invalid_source");
                    Include(source, name);
                }
            }, cancellationToken);
            if (entries.Count == 0) return new(Failure("empty_sources"), destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".nomozip-create-" + Guid.NewGuid().ToString("N") + ".tmp");
            progress?.Report(new("compressing", "正在创建压缩包…"));
            var forwarded = new ForwardProgress<EngineProgress>(value => progress?.Report(new("compressing", "正在创建压缩包…", value)));
            var result = await creator.CreateAsync(new(entries, format, level, format == "7z" && encryptHeaders), temporary, password, forwarded, cancellationToken);
            if (!result.IsSuccess) return new(result, destination);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("testing", "正在校验新压缩包…"));
            result = await validator.TestAsync(temporary, password, cancellationToken: cancellationToken);
            if (!result.IsSuccess) return new(result, destination);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destination))
            {
                if (!replace) return new(Failure("output_exists"), destination);
                File.Replace(temporary, destination, null);
            }
            else File.Move(temporary, destination);
            return new(result, destination);
        }
        catch (OperationCanceledException) { return new(new EngineResult { Status = "cancelled", Code = "cancelled" }, outputPath); }
        catch (InvalidDataException exception) { return new(Failure(exception.Message), outputPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { return new(Failure("source_or_output_unavailable"), outputPath); }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
    }
}
