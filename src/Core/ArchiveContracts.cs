namespace ArchivePassword.Core;

public sealed record ArchiveEntry(uint Index, string Path, ulong Size, bool Directory, bool Encrypted);
public sealed record EngineProgress(ulong Completed, ulong Total,bool? PausedState=null);
public sealed record EngineFailure(uint Index, string Code, string Path = "");
public sealed record EngineFormat(string Name, string Extensions);

public sealed record EngineResult
{
    public ulong VolumeCount {get;init;}
    public int Protocol { get; init; }
    public string Kind { get; init; } = "result";
    public string Status { get; init; } = "error";
    public string Code { get; init; } = "";
    public string Format { get; init; } = "";
    public uint ItemCount { get; init; }
    public uint SuccessfulItems { get; init; }
    public IReadOnlyList<ArchiveEntry> Items { get; init; } = [];
    public IReadOnlyList<EngineFailure> Failures { get; init; } = [];
    public IReadOnlyList<uint> VerifiedEncryptedItems { get; init; } = [];
    public IReadOnlyList<EngineFormat> Formats { get; init; } = [];
    public bool IsSuccess => Status == "success";
    public bool NeedsPassword => Code is "password_required" or "wrong_password" or "password_or_corrupt";
}

public interface IArchiveMultiPasswordEngine
{
    Task<EngineResult> TestWithPasswordsAsync(string archivePath, string? password, IReadOnlyDictionary<uint, string> itemPasswords, IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<EngineResult> ExtractWithPasswordsAsync(string archivePath, string outputPath, string? password, IReadOnlyDictionary<uint, string> itemPasswords, IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default);
}

public interface IArchiveEngine
{
    Task<EngineResult> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<EngineResult> ListAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default);
    Task<EngineResult> TestAsync(string archivePath, string? password = null, IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<EngineResult> ExtractAsync(string archivePath, string outputPath, string? password = null, IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default);
}
