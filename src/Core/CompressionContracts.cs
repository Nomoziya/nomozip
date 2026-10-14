namespace ArchivePassword.Core;

public sealed record CompressionEntry(string SourcePath, string Path, bool Directory);
public sealed record CompressionRequest(IReadOnlyList<CompressionEntry> Entries, string Format, int Level, bool EncryptHeaders,ulong VolumeSize=0);
public interface IArchiveCreator
{
    Task<EngineResult> CreateAsync(CompressionRequest request, string outputPath, string? password,
        IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default);
}
public sealed record CompressionOutcome(EngineResult Engine, string OutputPath,IReadOnlyList<string>? Volumes=null);

public sealed class CompressionService(IArchiveCreator creator, IArchiveEngine validator)
{
    public async Task<CompressionOutcome> CreateAsync(IEnumerable<string> sources, string outputPath, string format,
        int level = 5, string? password = null, bool encryptHeaders = true, bool replace = false,
        IProgress<TaskUpdate>? progress = null, CancellationToken cancellationToken = default,ulong volumeSize=0)
    {
        if(volumeSize>0) return await new VolumeCompressionService(creator,validator).CreateAsync(sources,outputPath,format,level,password,encryptHeaders,volumeSize,progress,cancellationToken);
        string? temporary = null;
        EngineResult Failure(string code) => new() { Code = code };
        try
        {
            var options = CompressionFormats.Find(format);
            if (options is null || level is not (0 or 1 or 3 or 5 or 7 or 9)) return new(Failure("invalid_compression_options"), outputPath);
            if (!options.Encryption && !string.IsNullOrEmpty(password)) return new(Failure("format_encryption_unsupported"), outputPath);
            if (format == "zip" && password?.Any(value => value > 127) == true) return new(Failure("zip_password_ascii_only"), outputPath);
            var destination = Path.GetFullPath(outputPath);
            if (File.Exists(destination) && !replace) return new(Failure("output_exists"), destination);
            progress?.Report(new("preparing", "正在准备文件…"));
            var entries=await CompressionSources.CollectAsync(sources,destination,cancellationToken);
            if (entries.Count == 0) return new(Failure("empty_sources"), destination);
            if (options.SingleFile && (entries.Count != 1 || entries[0].Directory)) return new(Failure("format_single_file_required"), destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".nomozip-create-" + Guid.NewGuid().ToString("N") + options.Extension);
            progress?.Report(new("compressing", "正在创建压缩包…"));
            var forwarded = new ForwardProgress<EngineProgress>(value => progress?.Report(new("compressing", "正在创建压缩包…", value)));
            var result = await creator.CreateAsync(new(entries, format, options.CompressionLevel ? level : 0, format == "7z" && encryptHeaders), temporary, password, forwarded, cancellationToken);
            if (!result.IsSuccess) return new(result, destination);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("testing", "正在校验新压缩包…"));
            result = await validator.TestAsync(temporary, password, cancellationToken: cancellationToken);
            if (!result.IsSuccess) return new(result, destination);
            if (!options.Encryption)
            {
                var listed = await validator.ListAsync(temporary, cancellationToken: cancellationToken);
                if (!listed.IsSuccess) return new(listed, destination);
                var expected = entries.ToDictionary(entry => entry.Path.Replace('\\', '/').TrimEnd('/'), StringComparer.OrdinalIgnoreCase);
                var valid = listed.Format == format && listed.Items.Count == entries.Count
                    && listed.Items.Select(entry => entry.Path.Replace('\\', '/').TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() == entries.Count && listed.Items.All(entry =>
                {
                    var path = entry.Path.Replace('\\', '/').TrimEnd('/');
                    var original = options.SingleFile ? entries[0] : expected.GetValueOrDefault(path);
                    return original is not null && !entry.Encrypted && entry.Directory == original.Directory
                        && (options.SingleFile ? path == Path.GetFileNameWithoutExtension(temporary) : expected.ContainsKey(path))
                        && (entry.Directory || entry.Size == (ulong)new FileInfo(original.SourcePath).Length);
                });
                if (!valid) return new(Failure("format_validation_failed"), destination);
            }
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
