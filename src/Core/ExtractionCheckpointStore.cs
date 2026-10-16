using System.Security.Cryptography;
using System.Text.Json;

namespace ArchivePassword.Core;

// Deliberately contains no password, vault identifier, or decoder state.
public sealed record CheckpointBinding(string EngineVersion, string EngineSha256, string Architecture, int FilenameCodePage);
public sealed record CheckpointSource(string Path, long Length, string Sha256);
public sealed record CheckpointReceipt(uint Index, long Length, string Sha256, string State = "staged", string? TargetPath = null);
public sealed record CheckpointRecord(int Version, Guid Id, string ArchivePath, string Destination,
    CheckpointBinding Binding, IReadOnlyList<CheckpointSource> Sources, IReadOnlyList<ArchiveEntry> Entries,
    IReadOnlyList<CheckpointReceipt> Receipts, DateTimeOffset Created, string Phase = "extracting", Guid Journal = default);
public sealed record ExtractionCheckpointRequest(ExtractionCheckpointStore Store, CheckpointBinding Binding, Guid? Id = null);
public sealed class CheckpointException(string code, Exception? inner = null) : IOException(code, inner)
{ public string Code { get; } = code; }

public sealed class ExtractionCheckpointStore(string root)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string root = Path.GetFullPath(root);

    public IReadOnlyList<CheckpointRecord> ReadPending(Action<string>? unreadable = null)
    {
        if (!Directory.Exists(root)) return [];
        EnsureNoLinks(root);
        var result = new List<CheckpointRecord>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            var path = Path.Combine(directory, "session.json");
            if (!File.Exists(path)) continue;
            try {result.Add(Read(path, id));}
            catch(Exception error) when(unreadable is not null && error is IOException or UnauthorizedAccessException)
            {unreadable(directory);}
        }
        return result.OrderBy(value => value.Created).ToArray();
    }

    public async Task<ExtractionCheckpointSession> ResumeAsync(Guid id, CheckpointBinding binding, CancellationToken token = default)
    {
        var (directory, owner) = Own(id, false);
        ExtractionCheckpointSession? session = null;
        try
        {
            var record = Read(Path.Combine(directory, "session.json"), id);
            if (record.Binding != binding) throw new CheckpointException("checkpoint_binding_changed");
            session = new(this, directory, owner, record);
            await session.ValidateAsync(token);
            return session;
        }
        catch { if (session is not null) await session.DisposeAsync(); else owner.Dispose(); throw; }
    }

    public async Task<ExtractionCheckpointSession> CreateAsync(string archive, string destination, CheckpointBinding binding,
        IReadOnlyList<string> sourcePaths, IReadOnlyList<ArchiveEntry> entries, CancellationToken token = default)
    {
        if (sourcePaths.Count == 0) throw new CheckpointException("checkpoint_sources_unavailable");
        var id = Guid.NewGuid(); var (directory, owner) = Own(id, true);
        var record = new CheckpointRecord(1, id, Path.GetFullPath(archive), Path.GetFullPath(destination), binding, [], entries, [], DateTimeOffset.UtcNow);
        var session = new ExtractionCheckpointSession(this, directory, owner, record);
        try
        {
            ValidateRecord(record, id, requireSources: false);
            EnsureNoLinks(record.Destination);
            Directory.CreateDirectory(session.StagingRoot);
            Directory.CreateDirectory(session.StagingPath);
            session.OwnWriter();
            await session.BindSourcesAsync(sourcePaths, token);
            ValidateRecord(session.Record, id);
            await session.SaveAsync(token);
            return session;
        }
        catch
        {
            // No native extraction starts before the first durable manifest.
            // Remove only this newly allocated, still empty session on setup failure.
            if(!File.Exists(Path.Combine(directory,"session.json")))
            {
                try {await session.RemoveAsync(CancellationToken.None);}
                catch(Exception cleanup) when(cleanup is IOException or UnauthorizedAccessException) { }
            }
            await session.DisposeAsync();
            throw;
        }
    }

    public async Task DiscardAsync(Guid id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var (directory, owner) = Own(id, false);
        ExtractionCheckpointSession? session=null;
        try
        {
            session=new(this,directory,owner,Read(Path.Combine(directory,"session.json"),id));
            session.OwnWriter();
            await session.RemoveAsync(token);
        }
        finally {if(session is not null) await session.DisposeAsync();else owner.Dispose();}
    }

    private (string Directory, FileStream Owner) Own(Guid id, bool create)
    {
        Directory.CreateDirectory(root); EnsureNoLinks(root);
        var directory = Path.Combine(root, id.ToString("N"));
        if (create) Directory.CreateDirectory(directory);
        else if (!Directory.Exists(directory)) throw new CheckpointException("checkpoint_missing");
        EnsureNoLinks(directory);
        try { return (directory, new FileStream(Path.Combine(directory, ".owner"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)); }
        catch (IOException error) { throw new CheckpointException("checkpoint_busy", error); }
    }

    private static CheckpointRecord Read(string path, Guid id)
    {
        try
        {
            EnsureNoLinks(path);
            var record = JsonSerializer.Deserialize<CheckpointRecord>(File.ReadAllBytes(path), JsonOptions) ?? throw new JsonException();
            ValidateRecord(record, id);
            var journal=Path.Combine(Path.GetDirectoryName(path)!,"progress-"+record.Journal.ToString("N")+".jsonl");
            EnsureNoLinks(journal);
            if(File.Exists(journal))
            {
                var bytes=File.ReadAllBytes(journal);
                var end=Array.LastIndexOf(bytes,(byte)'\n');
                var receipts=record.Receipts.ToDictionary(receipt=>receipt.Index);
                if(end>=0) foreach(var line in System.Text.Encoding.UTF8.GetString(bytes,0,end).Split('\n'))
                {
                    var receipt=JsonSerializer.Deserialize<CheckpointReceipt>(line,JsonOptions) ?? throw new JsonException();
                    receipts[receipt.Index]=receipt;
                }
                record=record with {Receipts=receipts.Values.ToArray()};
                ValidateRecord(record,id);
            }
            return record;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NullReferenceException or NotSupportedException)
        { throw new CheckpointException("checkpoint_invalid", error); }
    }

    private static void ValidateRecord(CheckpointRecord record, Guid id, bool requireSources = true)
    {
        if (record.Version is not (1 or 2) || record.Id != id || !Path.IsPathFullyQualified(record.ArchivePath) || !Path.IsPathFullyQualified(record.Destination)
            || record.Binding is null || !ZipFilenameEncoding.IsSupported(record.Binding.FilenameCodePage) || !HashValid(record.Binding.EngineSha256)
            || string.IsNullOrWhiteSpace(record.Binding.EngineVersion) || record.Binding.Architecture is not ("x64" or "arm64")
            || record.Phase is not ("extracting" or "committing" or "complete") || record.Sources is null || record.Entries is null || record.Receipts is null
            || (requireSources && (record.Sources.Count == 0 || !record.Sources.Any(source => SamePath(source.Path, record.ArchivePath))))
            || record.Sources.Any(source => !Path.IsPathFullyQualified(source.Path) || source.Length < 0 || !HashValid(source.Sha256))
            || record.Sources.Select(source => Path.GetFullPath(source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != record.Sources.Count
            || record.Entries.Select(entry => entry.Index).Distinct().Count() != record.Entries.Count
            || record.Entries.Select(entry => Relative(entry.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != record.Entries.Count
            || record.Receipts.Select(receipt => receipt.Index).Distinct().Count() != record.Receipts.Count)
            throw new CheckpointException("checkpoint_invalid");
        var files=record.Entries.Where(entry=>!entry.Directory).Select(entry=>Relative(entry.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var indexed=record.Entries.ToDictionary(entry=>entry.Index);
        foreach(var entry in record.Entries)
        {
            var parent=Path.GetDirectoryName(Relative(entry.Path));
            while(!string.IsNullOrEmpty(parent))
            {if(files.Contains(parent)) throw new CheckpointException("checkpoint_invalid");parent=Path.GetDirectoryName(parent);}
        }
        foreach (var receipt in record.Receipts)
        {
            indexed.TryGetValue(receipt.Index,out var entry);
            if (entry is null || receipt.Length < 0 || (!entry.Directory && ((ulong)receipt.Length != entry.Size || !HashValid(receipt.Sha256)))
                || receipt.State is not ("staged" or "intent" or "delivered" or "skipped")) throw new CheckpointException("checkpoint_invalid");
            if(receipt.TargetPath is not null && (entry.Directory || !ConflictFileNames.IsValid(Path.GetFileName(Relative(receipt.TargetPath)))
                || !string.Equals(Path.GetDirectoryName(Relative(entry.Path)),Path.GetDirectoryName(Relative(receipt.TargetPath)),StringComparison.OrdinalIgnoreCase)))
                throw new CheckpointException("checkpoint_invalid");
            if(record.Version==1 && receipt.TargetPath is not null && !string.Equals(Relative(entry.Path),Relative(receipt.TargetPath),StringComparison.OrdinalIgnoreCase))
                throw new CheckpointException("checkpoint_invalid");
        }
        var renamed=record.Receipts.Where(receipt=>receipt.TargetPath is not null).ToDictionary(receipt=>receipt.Index,receipt=>receipt.TargetPath!);
        var targets=record.Entries.Select(entry=>Relative(renamed.GetValueOrDefault(entry.Index) ?? entry.Path));
        if(targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=record.Entries.Count) throw new CheckpointException("checkpoint_invalid");
    }

    internal static bool HashValid(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    internal static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    internal static string Relative(string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) || normalized.Contains(':')) throw new CheckpointException("checkpoint_invalid");
        foreach (var component in normalized.Split(Path.DirectorySeparatorChar))
        {
            if (component is "" or "." or ".." || component.EndsWith('.') || component.EndsWith(' ')) throw new CheckpointException("checkpoint_invalid");
            var stem = component.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')
                throw new CheckpointException("checkpoint_invalid");
        }
        return normalized;
    }

    internal static void EnsureNoLinks(string path)
    {
        var cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new CheckpointException("checkpoint_unsafe_path");
            cursor = Path.GetDirectoryName(cursor);
        }
    }

    internal async Task SaveAsync(string directory, CheckpointRecord record, CancellationToken token)
    {
        EnsureNoLinks(directory);
        var temp = Path.Combine(directory, "session.next"); EnsureNoLinks(temp);
        await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { await JsonSerializer.SerializeAsync(output, record, JsonOptions, token); output.Flush(flushToDisk: true); }
        token.ThrowIfCancellationRequested();
        File.Move(temp, Path.Combine(directory, "session.json"), overwrite: true);
    }
}

public sealed class ExtractionCheckpointSession : IAsyncDisposable
{
    private readonly ExtractionCheckpointStore store;
    private readonly string directory;
    private readonly FileStream owner;
    private FileStream? writer;
    private readonly List<FileStream> sources = [];
    private bool removed;
    private CheckpointRecord record;
    private readonly Dictionary<uint,ArchiveEntry> entries;
    private readonly Dictionary<string,ArchiveEntry> files;
    private Dictionary<uint,CheckpointReceipt> receipts;
    public CheckpointRecord Record => record with {Receipts=receipts.Values.ToArray()};
    public int CompletedCount => receipts.Count;
    public int EntryCount => entries.Count;
    public IEnumerable<string> StagedFiles => receipts.Values.Where(receipt=>receipt.State=="staged" && !entries[receipt.Index].Directory).Select(receipt=>Source(entries[receipt.Index]));
    public string StagingRoot => Path.Combine(record.Destination, ".nomozip-checkpoint-" + record.Id.ToString("N"));
    public string StagingPath => Path.Combine(StagingRoot, "files");
    public IReadOnlyList<ArchiveEntry> Remaining => record.Entries.Where(entry => !receipts.ContainsKey(entry.Index)).ToArray();
    public IReadOnlyList<string> DeliveredFiles => receipts.Values.Where(receipt => receipt.State == "delivered")
        .Select(receipt => entries[receipt.Index]).Where(entry => !entry.Directory).Select(Target).ToArray();

    internal ExtractionCheckpointSession(ExtractionCheckpointStore store, string directory, FileStream owner, CheckpointRecord record)
    {
        this.store=store;this.directory=directory;this.owner=owner;this.record=record;
        entries=record.Entries.ToDictionary(entry=>entry.Index);
        files=record.Entries.Where(entry=>!entry.Directory).ToDictionary(entry=>ExtractionCheckpointStore.Relative(entry.Path),StringComparer.OrdinalIgnoreCase);
        receipts=record.Receipts.ToDictionary(receipt=>receipt.Index);
    }

    internal void OwnWriter()
    {
        ExtractionCheckpointStore.EnsureNoLinks(StagingRoot);
        Directory.CreateDirectory(StagingRoot);
        var path = Path.Combine(StagingRoot, ".nomozip-checkpoint-writer"); ExtractionCheckpointStore.EnsureNoLinks(path);
        try { writer ??= new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new CheckpointException("checkpoint_busy", error); }
    }
    public void BeginExtraction() { writer?.Dispose(); writer = null; }
    public void EndExtraction() => OwnWriter();
    public string Source(ArchiveEntry entry) => SafePath(StagingPath, entry.Path);
    public string DeliveryTarget(string source) => Target(files[Path.GetRelativePath(StagingPath,source)]);
    public string Target(ArchiveEntry entry) => SafePath(record.Destination, receipts.GetValueOrDefault(entry.Index)?.TargetPath ?? entry.Path);
    private static string SafePath(string root, string relative)
    {
        var path = Path.Combine(root, ExtractionCheckpointStore.Relative(relative));
        ExtractionCheckpointStore.EnsureNoLinks(path);
        return path;
    }
    private static async Task<(long Length, string Hash)> FingerprintAsync(FileStream stream, CancellationToken token)
    { stream.Position = 0; return (stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant()); }
    private static async Task<bool> MatchesAsync(string path, CheckpointReceipt receipt, bool directory, CancellationToken token)
    {
        ExtractionCheckpointStore.EnsureNoLinks(path);
        if (directory) return Directory.Exists(path);
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        var fingerprint = await FingerprintAsync(stream, token);
        return fingerprint.Length == receipt.Length && fingerprint.Hash == receipt.Sha256;
    }
    internal async Task BindSourcesAsync(IReadOnlyList<string> paths, CancellationToken token)
    {
        var records = new List<CheckpointSource>();
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            ExtractionCheckpointStore.EnsureNoLinks(path);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            sources.Add(stream);
            var fingerprint = await FingerprintAsync(stream, token);
            records.Add(new(path, fingerprint.Length, fingerprint.Hash));
        }
        record = record with { Sources = records };
    }
    internal async Task ValidateAsync(CancellationToken token)
    {
        OwnWriter();
        var expected = record.Sources;
        try { await BindSourcesAsync(expected.Select(source => source.Path).ToArray(), token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { throw new CheckpointException("checkpoint_source_changed", error); }
        if (!expected.SequenceEqual(record.Sources)) throw new CheckpointException("checkpoint_source_changed");
        var first=new FileInfo(record.ArchivePath);
        if(first.Extension==".001" && Path.GetExtension(Path.GetFileNameWithoutExtension(first.Name)).ToLowerInvariant() is ".7z" or ".zip")
        {
            var prefix=first.Name[..^3];
            var parts=Directory.EnumerateFiles(first.DirectoryName!,prefix+"*")
                .Where(path=>{var suffix=Path.GetFileName(path)[prefix.Length..];return suffix.Length>=3 && suffix.All(char.IsAsciiDigit);})
                .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(!parts.SetEquals(expected.Select(source=>source.Path))) throw new CheckpointException("checkpoint_source_changed");
        }
        var valid = new List<CheckpointReceipt>();
        Directory.CreateDirectory(StagingPath); ExtractionCheckpointStore.EnsureNoLinks(StagingPath);
        foreach (var entry in record.Entries)
        {
            token.ThrowIfCancellationRequested();
            receipts.TryGetValue(entry.Index,out var receipt);
            if (receipt is not null)
            {
                if (receipt.State == "skipped") { valid.Add(receipt); continue; }
                if (receipt.State is "intent" or "delivered" && await MatchesAsync(Target(entry), receipt, entry.Directory, token))
                { valid.Add(receipt with { State = "delivered" }); continue; }
                if (await MatchesAsync(Source(entry), receipt, entry.Directory, token))
                { valid.Add(receipt with { State = "staged" }); continue; }
            }
            // A file without a durable successful receipt is an incomplete output.
            if (!entry.Directory && File.Exists(Source(entry))) File.Delete(Source(entry));
        }
        receipts=valid.ToDictionary(receipt=>receipt.Index); record = record with { Phase = "extracting" };
        await SaveAsync(token);
    }
    public void ValidateListing(EngineResult listed)
    {
        if (!record.Entries.SequenceEqual(listed.Items) || !record.Sources.Select(source => source.Path).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .IsSupersetOf(listed.SourcePaths.Select(Path.GetFullPath))) throw new CheckpointException("checkpoint_source_changed");
    }
    public async Task SaveCompletedAsync(uint index, CancellationToken token)
    {
        if(!entries.TryGetValue(index,out var entry)) throw new CheckpointException("checkpoint_invalid");
        if (receipts.ContainsKey(index)) throw new CheckpointException("checkpoint_invalid");
        CheckpointReceipt receipt;
        if (entry.Directory)
        {
            if (!Directory.Exists(Source(entry))) throw new CheckpointException("checkpoint_output_changed");
            receipt = new(index, 0, "");
        }
        else
        {
            await using var stream = new FileStream(Source(entry), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            var fingerprint = await FingerprintAsync(stream, token);
            if ((ulong)fingerprint.Length != entry.Size) throw new CheckpointException("checkpoint_output_changed");
            receipt = new(index, fingerprint.Length, fingerprint.Hash);
        }
        await AppendAsync(receipt,token);
    }
    public async Task SetIntentAsync(string source, ExistingFilePolicy policy, CancellationToken token, string? target=null)
    {
        var entry=files[Path.GetRelativePath(StagingPath,source)];
        var relative=target is null?null:ExtractionCheckpointStore.Relative(Path.GetRelativePath(record.Destination,target));
        if(relative is not null && !relative.Equals(ExtractionCheckpointStore.Relative(entry.Path),StringComparison.OrdinalIgnoreCase) && record.Version==1)
        {
            // Older applications must refuse this session instead of ignoring
            // the new destination and delivering to the original filename.
            record=record with {Version=2};await SaveAsync(token);
        }
        await AppendAsync(receipts[entry.Index] with {State=policy==ExistingFilePolicy.Skip?"skipped":"intent",TargetPath=relative},token);
    }
    public Task SetDeliveredAsync(string target, CancellationToken token, string? source=null)
    {
        var entry=source is null?files[Path.GetRelativePath(record.Destination,target)]:files[Path.GetRelativePath(StagingPath,source)];
        return AppendAsync(receipts[entry.Index] with {State="delivered"},token);
    }
    public async Task<FileStream> VerifyForDeliveryAsync(string source,CancellationToken token)
    {
        var entry=files[Path.GetRelativePath(StagingPath,source)];
        ExtractionCheckpointStore.EnsureNoLinks(source);
        var stream=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,65536,FileOptions.SequentialScan);
        try
        {
            var fingerprint=await FingerprintAsync(stream,token);var receipt=receipts[entry.Index];
            if(fingerprint.Length!=receipt.Length || fingerprint.Hash!=receipt.Sha256) throw new CheckpointException("checkpoint_output_changed");
            return stream;
        }
        catch {await stream.DisposeAsync();throw;}
    }
    private async Task AppendAsync(CheckpointReceipt receipt,CancellationToken token)
    {
        var path=Path.Combine(directory,"progress-"+record.Journal.ToString("N")+".jsonl");
        ExtractionCheckpointStore.EnsureNoLinks(path);
        try
        {
            await using var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);
            await JsonSerializer.SerializeAsync(stream,receipt,new JsonSerializerOptions(JsonSerializerDefaults.Web),token);
            await stream.WriteAsync("\n"u8.ToArray(),token);stream.Flush(flushToDisk:true);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {throw new CheckpointException("checkpoint_save_failed",error);}
        receipts[receipt.Index]=receipt;
    }
    public async Task SaveAsync(CancellationToken token = default)
    {
        // A new generation makes a crash between manifest replacement and old-log
        // cleanup harmless. Per-entry updates append small flushed records.
        var next=record with {Receipts=receipts.Values.ToArray(),Journal=Guid.NewGuid()};
        try {await store.SaveAsync(directory,next,token);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {throw new CheckpointException("checkpoint_save_failed",error);}
        record=next;
        foreach(var path in Directory.EnumerateFiles(directory,"progress-*.jsonl"))
        {
            if(Path.GetFileName(path)=="progress-"+record.Journal.ToString("N")+".jsonl") continue;
            ExtractionCheckpointStore.EnsureNoLinks(path);File.Delete(path);
        }
    }
    public async Task CompleteAsync(CancellationToken token = default)
    { record = record with { Phase = "complete" }; await SaveAsync(token); await RemoveAsync(token); }
    internal Task RemoveAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); OwnWriter();
        // Never follow a link introduced into retained decrypted output during a restart.
        if (Directory.Exists(StagingRoot)) CheckTree(StagingRoot,token);
        writer?.Dispose(); writer = null;
        if (Directory.Exists(StagingRoot)) DeleteTree(StagingRoot,token);
        foreach (var name in new[] { "session.json", "session.next" })
        { var path = Path.Combine(directory, name); ExtractionCheckpointStore.EnsureNoLinks(path); if (File.Exists(path)) File.Delete(path); }
        foreach(var path in Directory.EnumerateFiles(directory,"progress-*.jsonl"))
        {ExtractionCheckpointStore.EnsureNoLinks(path);File.Delete(path);}
        removed = true; return Task.CompletedTask;
    }
    private static void CheckTree(string root,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ExtractionCheckpointStore.EnsureNoLinks(root);
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        { token.ThrowIfCancellationRequested();ExtractionCheckpointStore.EnsureNoLinks(path); if (Directory.Exists(path)) CheckTree(path,token); }
    }
    private static void DeleteTree(string root,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();ExtractionCheckpointStore.EnsureNoLinks(root);
        foreach(var path in Directory.EnumerateFileSystemEntries(root))
        {token.ThrowIfCancellationRequested();ExtractionCheckpointStore.EnsureNoLinks(path);if(Directory.Exists(path)) DeleteTree(path,token);else File.Delete(path);}
        Directory.Delete(root);
    }
    public ValueTask DisposeAsync()
    {
        writer?.Dispose(); foreach (var stream in sources) stream.Dispose(); owner.Dispose();
        if (removed) { try { Directory.Delete(directory); } catch (Exception error) when(error is IOException or UnauthorizedAccessException) { } }
        return ValueTask.CompletedTask;
    }
}
