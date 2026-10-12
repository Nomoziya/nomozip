namespace ArchivePassword.Core;

public sealed record EngineUpdateManifest(int Schema, string Version, string Architecture, int Protocol,
    string MinimumAppVersion, string Source, Dictionary<string, string> Files);
public sealed record EngineSelection(int Schema, string Active, string? Previous);
public sealed record AvailableEngineUpdate(string Version, Uri DownloadUrl, long Size);
public sealed class UpdateException(string code, Exception? inner = null) : Exception(code, inner)
{
    public string Code { get; } = code;
}
public interface IUpdateService
{
    Task<EngineUpdateManifest> InspectAsync(string packagePath, CancellationToken cancellationToken = default);
    Task<string> InstallAsync(string packagePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task ActivateAsync(string version, CancellationToken cancellationToken = default);
    Task<string> RollbackAsync(CancellationToken cancellationToken = default);
}
