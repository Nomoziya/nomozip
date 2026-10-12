using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed class EngineUpdateService(string installedDirectory, string selectionPath, string publicKey,
    string workerPath, string probePath, EngineCatalog catalog) : IUpdateService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<EngineUpdateManifest> InspectAsync(string packagePath, CancellationToken cancellationToken = default) =>
        (await EngineUpdatePackage.ReadAsync(packagePath, publicKey, cancellationToken)).Manifest;
    public async Task<EngineSelection?> ReadSelectionAsync(CancellationToken token = default)
    {
        if (!File.Exists(selectionPath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<EngineSelection>(await File.ReadAllTextAsync(selectionPath, token), EngineUpdatePackage.JsonOptions);
            if (state is null || state.Schema != 1 || !EngineUpdatePackage.VersionPattern().IsMatch(state.Active ?? "") || (state.Previous is not null && !EngineUpdatePackage.VersionPattern().IsMatch(state.Previous))) throw new UpdateException("invalid_selection");
            return state;
        }
        catch (JsonException exception) { throw new UpdateException("invalid_selection", exception); }
    }
    public async Task<string> InstallAsync(string packagePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        var installedRoot = Path.GetFullPath(installedDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var staging = Path.GetFullPath(Path.Combine(installedRoot, ".prepare-" + Guid.NewGuid().ToString("N")));
        if (!staging.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase)) { gate.Release(); throw new UpdateException("invalid_package"); }
        try
        {
            progress?.Report("正在校验更新包…");
            var package = await EngineUpdatePackage.ReadAsync(packagePath, publicKey, cancellationToken);
            Directory.CreateDirectory(staging);
            foreach (var (name, bytes) in package.Payload) await File.WriteAllBytesAsync(Path.Combine(staging, name), bytes, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(staging, "update-manifest.json"), package.ManifestBytes, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(staging, "update-manifest.sig"), package.Signature, cancellationToken);
            progress?.Report("正在验证新引擎能否正常解压…");
            await ProbeAsync(Path.Combine(staging, "7z.dll"), cancellationToken);
            var target = Path.GetFullPath(Path.Combine(installedRoot, package.Manifest.Version));
            if (!target.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase)) throw new UpdateException("invalid_package");
            if (Directory.Exists(target))
            {
                var previous = await EngineUpdatePackage.VerifyInstalledAsync(target, publicKey, cancellationToken);
                if (!previous.Files["7z.dll"].Equals(package.Manifest.Files["7z.dll"], StringComparison.OrdinalIgnoreCase)) throw new UpdateException("version_conflict");
            }
            else { cancellationToken.ThrowIfCancellationRequested(); Directory.Move(staging, target); }
            await catalog.ReadVerifiedVersionsAsync();
            return package.Manifest.Version;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            gate.Release();
        }
    }
    public async Task ActivateAsync(string version, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!(await catalog.ReadVerifiedVersionsAsync()).Contains(version)) throw new UpdateException("engine_not_available");
            await ProbeAsync(catalog.DllPath(version), cancellationToken);
            var previous = await ReadSelectionAsync(cancellationToken);
            var state = new EngineSelection(1, version, previous?.Active == version ? previous.Previous : previous?.Active);
            await SaveSelectionAsync(state, cancellationToken);
        }
        finally { gate.Release(); }
    }
    public async Task<string> RollbackAsync(CancellationToken cancellationToken = default)
    {
        var state = await ReadSelectionAsync(cancellationToken);
        if (state?.Previous is not { } version) throw new UpdateException("no_previous_engine");
        await ActivateAsync(version, cancellationToken); return version;
    }
    private async Task SaveSelectionAsync(EngineSelection state, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(selectionPath))!);
        var temporary = selectionPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, EngineUpdatePackage.JsonOptions), token);
            token.ThrowIfCancellationRequested();
            if (File.Exists(selectionPath)) File.Replace(temporary, selectionPath, selectionPath + ".bak");
            else File.Move(temporary, selectionPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task ProbeAsync(string dll, CancellationToken token)
    {
        var client = new ArchiveEngineClient(workerPath, dll);
        var capabilities = await client.GetCapabilitiesAsync(token);
        token.ThrowIfCancellationRequested();
        if (!capabilities.IsSuccess || !new[] { "7z", "zip", "Rar5" }.All(name => capabilities.Formats.Any(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))) throw new UpdateException("engine_validation_failed");
        var result = await client.TestAsync(probePath, "测试密碼 ' $ & 空格", cancellationToken: token);
        token.ThrowIfCancellationRequested();
        if (!result.IsSuccess) throw new UpdateException("engine_validation_failed");
    }
}
