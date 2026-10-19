using System.Text.Json;
using System.Security.Cryptography;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed class EngineUpdateService(string installedDirectory, string selectionPath, string publicKey,
    string workerPath, string probePath, EngineCatalog catalog) : IUpdateService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<string> InstallOfficialAsync(OfficialEngineUpdate update, string installerPath, string extractionVersion,
        IProgress<string>? progress = null, CancellationToken token = default)
    {
        OfficialEngineChannel.Validate(update);
        var root = catalog.OfficialDirectory ?? throw new UpdateException("engine_not_available");
        await gate.WaitAsync(token);
        var staging = Path.Combine(root, ".prepare-" + Guid.NewGuid().ToString("N"));
        try
        {
            progress?.Report(UiText.Get("EngineSource.Verifying"));
            Directory.CreateDirectory(staging);
            // Prefer the 7z payload handler over the outer PE handler, without running the installer.
            var archivePath=Path.Combine(staging,".installer.7z");
            await using (var file = File.OpenRead(installerPath))
            {
                if (file.Length != update.Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException("hash_mismatch");
                file.Position=0;
                await using var copy=new FileStream(archivePath,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                await file.CopyToAsync(copy,token);
            }
            await catalog.ReadVerifiedVersionsAsync();
            var extractor = new ArchiveEngineClient(workerPath, catalog.DllPath(extractionVersion));
            var inventory = await extractor.ListAsync(archivePath, cancellationToken: token); token.ThrowIfCancellationRequested();
            var selected = inventory.Items.Where(item => OfficialEngineReceipt.PayloadNames.Contains(item.Path, StringComparer.Ordinal)).ToArray();
            if (!inventory.IsSuccess || selected.Length != OfficialEngineReceipt.PayloadNames.Length
                || OfficialEngineReceipt.PayloadNames.Any(name => selected.Count(item => item.Path == name) != 1)
                || selected.Any(item => item.Directory || item.Encrypted || item.Size is 0 or > 16 * 1024 * 1024)) throw new UpdateException("invalid_official_update");
            var extracted = await extractor.ExtractSelectionAsync(archivePath, staging, selected, null, new Dictionary<uint,string>(), token: token);
            token.ThrowIfCancellationRequested();
            if (!extracted.IsSuccess) throw new UpdateException("engine_validation_failed");
            File.Delete(archivePath);
            var hashes = new Dictionary<string,string>();
            foreach (var name in OfficialEngineReceipt.PayloadNames)
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(staging,name),token);
                if (name is "7z.dll" or "7z.exe" && !EngineArchitecture.IsNativeImage(bytes)) throw new UpdateException("incompatible_update");
                hashes[name] = Convert.ToHexString(SHA256.HashData(bytes));
            }
            // The official SFX is an x86 executable in both native installers; use the established SFX contract.
            if(!new SfxModule(update.Version,hashes["7z.sfx"],await File.ReadAllBytesAsync(Path.Combine(staging,"7z.sfx"),token)).IsValid())throw new UpdateException("sfx_module_invalid");
            var actualVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(staging,"7z.dll")).FileVersion;
            var expected = Version.Parse(update.Version);
            if (!Version.TryParse(actualVersion, out var actual) || actual.Major != expected.Major || actual.Minor != expected.Minor) throw new UpdateException("incompatible_update");
            var knownVersions = await catalog.ReadVerifiedVersionsAsync();
            if (knownVersions.Contains(update.Version))
            {
                var known = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(catalog.DllPath(update.Version),token)));
                if (!known.Equals(hashes["7z.dll"],StringComparison.OrdinalIgnoreCase)) throw new UpdateException("version_conflict");
            }
            progress?.Report(UiText.Get("EngineSource.Testing"));
            await ProbeAsync(Path.Combine(staging,"7z.dll"), token);
            await ProbeOfficialCreationAsync(staging, token);
            await new OfficialEngineReceipt(update,hashes).SaveAsync(staging,token);
            await OfficialEngineReceipt.VerifyInstalledAsync(staging,token);
            var target = Path.Combine(root,update.Version);
            if (Directory.Exists(target))
            {
                var previous = await OfficialEngineReceipt.VerifyInstalledAsync(target,token);
                if (hashes.Any(pair => !previous.Files[pair.Key].Equals(pair.Value,StringComparison.OrdinalIgnoreCase))) throw new UpdateException("version_conflict");
            }
            else { token.ThrowIfCancellationRequested(); Directory.Move(staging,target); }
            await catalog.ReadVerifiedVersionsAsync();
            return update.Version;
        }
        finally { try { await DeletePreparationAsync(staging); } finally { gate.Release(); } }
    }

    private async Task ProbeOfficialCreationAsync(string staging, CancellationToken token)
    {
        var folder=Path.Combine(staging,".probe");Directory.CreateDirectory(folder);
        try
        {
            var source=Path.Combine(folder,"中文.txt");var bytes="nomozip official engine compatibility\n中文内容"u8.ToArray();
            await File.WriteAllBytesAsync(source,bytes,token);
            var engine=new ArchiveEngineClient(workerPath,Path.Combine(staging,"7z.dll"));
            foreach(var format in new[]{"7z","zip"})
            {
                var archive=Path.Combine(folder,"probe."+format);var output=Path.Combine(folder,format);
                var request=new CompressionRequest([new(source,"中文.txt",false)],format,5,format=="7z");
                const string password="测试密碼 ' $ & 空格";
                var created=await engine.CreateAsync(request,archive,password,cancellationToken:token);token.ThrowIfCancellationRequested();
                if(!created.IsSuccess)throw new UpdateException("engine_validation_failed");
                var wrongPassword=await engine.TestAsync(archive,"incorrect synthetic password",cancellationToken:token);token.ThrowIfCancellationRequested();
                if(wrongPassword.IsSuccess)throw new UpdateException("engine_validation_failed");
                var extracted=await engine.ExtractAsync(archive,output,password,cancellationToken:token);token.ThrowIfCancellationRequested();
                var restored=await File.ReadAllBytesAsync(Path.Combine(output,"中文.txt"),token);
                if(!extracted.IsSuccess || !bytes.SequenceEqual(restored))throw new UpdateException("engine_validation_failed");
            }
        }
        finally {await DeletePreparationAsync(folder);}
    }
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
            progress?.Report(UiText.Get("T_d0b2c6fecb84"));
            var package = await EngineUpdatePackage.ReadAsync(packagePath, publicKey, cancellationToken);
            Directory.CreateDirectory(staging);
            foreach (var (name, bytes) in package.Payload) await File.WriteAllBytesAsync(Path.Combine(staging, name), bytes, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(staging, "update-manifest.json"), package.ManifestBytes, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(staging, "update-manifest.sig"), package.Signature, cancellationToken);
            progress?.Report(UiText.Get("T_5679ccc535d2"));
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
            try { await DeletePreparationAsync(staging); }
            finally { gate.Release(); }
        }
    }
    private static async Task DeletePreparationAsync(string staging)
    {
        // A just-probed DLL can still have a transient Windows image/file handle.
        // Limit this wait to preparation cleanup; permanent errors still propagate.
        for(var attempt=0;;attempt++)
        {
            try { if(Directory.Exists(staging)) Directory.Delete(staging,true);return; }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException && (error.HResult&0xffff) is 5 or 32 or 33 && attempt<20)
            { await Task.Delay(100); }
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
