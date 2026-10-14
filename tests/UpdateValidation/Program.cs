using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var testRoot = Path.Combine(root, "artifacts/update-validation", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(testRoot);
var publicKey = await File.ReadAllTextAsync(Path.Combine(root, "artifacts/update-test-assets/public.pem"));
var privateKey = await File.ReadAllTextAsync(Path.Combine(root, "artifacts/update-test-assets/private.pem"));
var package03 = Path.Combine(root, "artifacts/update-test-assets/ArchivePassword-engine-26.03-win-x64.zip");
var package04 = Path.Combine(root, "artifacts/update-test-assets/ArchivePassword-engine-26.04-win-x64.zip");
var installed = Path.Combine(testRoot, "engines"); var statePath = Path.Combine(testRoot, "active-engine.json");
var catalog = new EngineCatalog(Path.Combine(root, "engines"), installed, publicKey);
var worker = Path.Combine(root, "artifacts/worker/ArchiveEngineWorker.exe"); var archive = Path.Combine(root, "tests/fixtures/generated/encrypted-header.7z");
var updater = new EngineUpdateService(installed, statePath, publicKey, worker, archive, catalog);
var checks = new List<object>();
void Check(string name, bool condition) { Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}"); checks.Add(new { name, passed = condition }); if (!condition) throw new Exception(name); }
async Task Reject(string name, Func<Task> action, string? expected = null)
{
    var before = await File.ReadAllBytesAsync(statePath);
    try { await action(); Check(name, false); }
    catch (UpdateException exception) { Check(name, (expected is null || expected == exception.Code) && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(statePath))); }
}
await catalog.ReadVerifiedVersionsAsync(); await updater.ActivateAsync("26.04");
Check("initial engine recorded", (await updater.ReadSelectionAsync()) is { Active: "26.04", Previous: null });
Check("valid package signature and payload", (await updater.InspectAsync(package03)).Version == "26.03");
var unchangedVault = new DpapiPasswordVault(Path.Combine(testRoot, "passwords.apv"));
await unchangedVault.SaveAsync(new SavedPassword(Guid.NewGuid(), "synthetic update sample", "测试密碼 ' $ & 空格", "", true, DateTimeOffset.UtcNow));
var vaultBefore = await File.ReadAllBytesAsync(Path.Combine(testRoot, "passwords.apv"));
await updater.InstallAsync(package03); await updater.ActivateAsync("26.03");
Check("new engine installed separately and activated", File.Exists(Path.Combine(installed, "26.03/7z.dll")) && (await updater.ReadSelectionAsync()) is { Active: "26.03", Previous: "26.04" } && catalog.DllPath("26.03").StartsWith(installed, StringComparison.OrdinalIgnoreCase));
var installedOnly=new EngineCatalog(Path.Combine(testRoot,"no-bundled-engine"),installed,publicKey);await installedOnly.ReadVerifiedVersionsAsync();
var installedModule=await installedOnly.ReadSfxModuleAsync("26.03");
var officialModule=await File.ReadAllBytesAsync(Path.Combine(root,"engines/26.03/7z.sfx"));
Check("signed module is available without any bundled engine fallback",installedModule.IsValid() && installedModule.Version=="26.03" && installedModule.Bytes.SequenceEqual(officialModule));
var sfxInput=Path.Combine(testRoot,"sfx-input.txt");await File.WriteAllTextAsync(sfxInput,"signed update SFX");var signedExe=Path.Combine(testRoot,"signed-module.exe");
var signedClient=new ArchiveEngineClient(worker,installedOnly.DllPath("26.03"));
var signedCreated=await new SfxCompressionService(signedClient,signedClient).CreateAsync([sfxInput],signedExe,installedModule);
Check("installed signed engine module actually creates valid EXE",signedCreated.Engine.IsSuccess && (await signedClient.TestAsync(signedExe)).IsSuccess);
var reopened = new EngineUpdateService(installed, statePath, publicKey, worker, archive, new EngineCatalog(Path.Combine(root, "engines"), installed, publicKey));
Check("active selection survives reopen", (await reopened.ReadSelectionAsync())?.Active == "26.03");
var engine = new ArchiveEngineClient(worker, catalog.DllPath("26.03"));
var extraction = await engine.ExtractAsync(archive, Path.Combine(testRoot, "output"), "测试密碼 ' $ & 空格");
Check("installed engine actually extracts matching data", extraction.IsSuccess && File.ReadAllBytes(Path.Combine(testRoot, "output/中文示例.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/generated/中文示例.txt"))));
Check("rollback validates and restores previous engine", await updater.RollbackAsync() == "26.04" && (await updater.ReadSelectionAsync()) is { Active: "26.04", Previous: "26.03" });
Check("password vault bytes unchanged by upgrade and rollback", Enumerable.SequenceEqual(vaultBefore, await File.ReadAllBytesAsync(Path.Combine(testRoot, "passwords.apv"))));
await updater.InstallAsync(package04);
Check("idempotent verified installation", await updater.InstallAsync(package04) == "26.04");

async Task<string> Mutate(string name, Action<Dictionary<string, byte[]>> change, bool resign = false, string? signingKey = null)
{
    var entries = new Dictionary<string, byte[]>();
    using (var original = ZipFile.OpenRead(package03)) foreach (var entry in original.Entries)
    { using var source = entry.Open(); using var memory = new MemoryStream(); source.CopyTo(memory); entries[entry.FullName] = memory.ToArray(); }
    change(entries);
    if (resign) { using var rsa = RSA.Create(); rsa.ImportFromPem(signingKey ?? privateKey); entries["manifest.sig"] = rsa.SignData(entries["manifest.json"], HashAlgorithmName.SHA256, RSASignaturePadding.Pss); }
    var target = Path.Combine(testRoot, name + ".zip");
    using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) foreach (var (entryName, bytes) in entries)
    { using var output = zip.CreateEntry(entryName).Open(); output.Write(bytes); }
    return target;
}
async Task<string> SfxVariant(string name,Action<System.Text.Json.Nodes.JsonObject> change)
{
    return await Mutate(name,values=>
    {
        var report=(System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(values["engine/compatibility-report.json"])!;change(report);
        values["engine/compatibility-report.json"]=JsonSerializer.SerializeToUtf8Bytes(report);
        var manifest=JsonSerializer.Deserialize<EngineUpdateManifest>(values["manifest.json"],EngineUpdatePackage.JsonOptions)!;
        manifest.Files["compatibility-report.json"]=Convert.ToHexString(SHA256.HashData(values["engine/compatibility-report.json"]));values["manifest.json"]=JsonSerializer.SerializeToUtf8Bytes(manifest,EngineUpdatePackage.JsonOptions);
    },true);
}
foreach(var kind in new[]{"hash","version","encoding"})
{
    var variant=await SfxVariant("sfx-"+kind,report=>{var module=report["sfx"]!;if(kind=="hash") module["sha256"]=new string('0',64);else if(kind=="version") module["version"]="26.04";else module["moduleBase64"]="invalid base64";});
    var invalidStore=Path.Combine(testRoot,"bad-"+kind);var invalidCatalog=new EngineCatalog(Path.Combine(testRoot,"no-bundle"),invalidStore,publicKey);
    var invalidUpdater=new EngineUpdateService(invalidStore,Path.Combine(testRoot,"bad-"+kind+".json"),publicKey,worker,archive,invalidCatalog);await invalidUpdater.InstallAsync(variant);
    bool refused=false;try {await invalidCatalog.ReadSfxModuleAsync("26.03");} catch(UpdateException error) {refused=error.Code=="sfx_module_invalid";}
    Check("signed invalid SFX "+kind+" cannot provide a module",refused);
}
var legacyPackage=await SfxVariant("sfx-legacy",report=>report.Remove("sfx"));var legacyFolder=Path.Combine(testRoot,"legacy");
var legacyCatalog=new EngineCatalog(Path.Combine(root,"engines"),legacyFolder,publicKey);var legacyUpdater=new EngineUpdateService(legacyFolder,Path.Combine(testRoot,"legacy.json"),publicKey,worker,archive,legacyCatalog);await legacyUpdater.InstallAsync(legacyPackage);
Check("legacy signed package reuses only matching bundled module",(await legacyCatalog.ReadSfxModuleAsync("26.03")).Bytes.SequenceEqual(officialModule));
var noFallback=new EngineCatalog(Path.Combine(testRoot,"absent-bundle"),legacyFolder,publicKey);await noFallback.ReadVerifiedVersionsAsync();bool unavailable=false;
try {await noFallback.ReadSfxModuleAsync("26.03");} catch(UpdateException error) {unavailable=error.Code=="sfx_module_unavailable";}
Check("legacy engine without matching module stays usable but SFX unavailable",unavailable && (await new ArchiveEngineClient(worker,noFallback.DllPath("26.03")).TestAsync(archive,"测试密碼 ' $ & 空格")).IsSuccess);
var changedManifest = await Mutate("changed-manifest", values => values["manifest.json"][8] ^= 1);
await Reject("manifest tampering rejected without switch", () => updater.InstallAsync(changedManifest), "invalid_signature");
var changedDll = await Mutate("changed-dll", values => values["engine/7z.dll"][100] ^= 1);
await Reject("DLL tampering rejected without switch", () => updater.InstallAsync(changedDll), "hash_mismatch");
using (var foreignKey = RSA.Create(3072))
{
    var foreign = await Mutate("foreign-key", _ => { }, true, foreignKey.ExportPkcs8PrivateKeyPem());
    await Reject("untrusted signing identity rejected", () => updater.InstallAsync(foreign), "invalid_signature");
}
var pathTraversal = await Mutate("path-traversal", values => values["../../outside.dll"] = [1]);
await Reject("unexpected and traversal entries rejected", () => updater.InstallAsync(pathTraversal), "invalid_package");
var incompatible = await Mutate("incompatible", values =>
{
    var manifest = JsonSerializer.Deserialize<EngineUpdateManifest>(values["manifest.json"], EngineUpdatePackage.JsonOptions)!;
    values["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest with { Protocol = 99 }, EngineUpdatePackage.JsonOptions);
}, true);
await Reject("incompatible signed protocol rejected", () => updater.InstallAsync(incompatible), "incompatible_update");
var broken = await Mutate("broken-engine", values =>
{
    values["engine/7z.dll"] = "not a DLL"u8.ToArray();
    var manifest = JsonSerializer.Deserialize<EngineUpdateManifest>(values["manifest.json"], EngineUpdatePackage.JsonOptions)!;
    manifest.Files["7z.dll"] = Convert.ToHexString(SHA256.HashData(values["engine/7z.dll"]));
    values["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest, EngineUpdatePackage.JsonOptions);
}, true);
await Reject("signed but non-working engine rejected by real worker", () => updater.InstallAsync(broken), "engine_validation_failed");
Check("failed preparations cleaned", !Directory.EnumerateDirectories(installed, ".prepare-*").Any());
using (var cancellation = new CancellationTokenSource())
{
    var state = await File.ReadAllBytesAsync(statePath); cancellation.Cancel();
    try { await updater.InstallAsync(package03, cancellationToken: cancellation.Token); Check("cancel update", false); }
    catch (OperationCanceledException) { Check("cancel update preserves active engine", Enumerable.SequenceEqual(state, await File.ReadAllBytesAsync(statePath))); }
}
var installedDll = Path.Combine(installed, "26.03/7z.dll"); var dllBefore = await File.ReadAllBytesAsync(installedDll);
await File.WriteAllBytesAsync(installedDll, "tampered installed fixture"u8.ToArray());
await catalog.ReadVerifiedVersionsAsync();
Check("tampered installed engine excluded, bundled engine remains usable", !catalog.DllPath("26.03").StartsWith(installed, StringComparison.OrdinalIgnoreCase));
await File.WriteAllBytesAsync(installedDll, dllBefore);
var normalSelection = await File.ReadAllBytesAsync(statePath);
await File.WriteAllTextAsync(statePath, "{\"schema\":99,\"active\":\"26.04\"}");
await Reject("unknown selection schema preserved", () => updater.ActivateAsync("26.03"), "invalid_selection");
await File.WriteAllBytesAsync(statePath, normalSelection);

Check("repository URL normalized", GitHubEngineChannel.NormalizeRepository("https://github.com/Nomoziya/nomozip/") == "Nomoziya/nomozip");
using var http = new HttpClient(new FixtureHandler(package04)) { Timeout = TimeSpan.FromSeconds(10) };
var channel = new GitHubEngineChannel(http); var found = await channel.CheckAsync("Nomoziya/nomozip");
Check("GitHub release selects named stable engine asset", found is { Version: "26.04" });
await channel.DownloadAsync(found!, Path.Combine(testRoot, "downloaded.zip"), null);
Check("GitHub download reaches same signature verification", (await updater.InspectAsync(Path.Combine(testRoot, "downloaded.zip"))).Version == "26.04");
using var notFound = new HttpClient(new FixtureHandler(package04, notFound: true));
Check("channel without a release has no update", await new GitHubEngineChannel(notFound).CheckAsync("Nomoziya/nomozip") is null);
await File.WriteAllTextAsync(Path.Combine(root, "artifacts/update-validation.json"), JsonSerializer.Serialize(new { passed = checks.Count, total = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count}/{checks.Count} passed."); return 0;

sealed class FixtureHandler(string packagePath, bool notFound = false) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (notFound) return new HttpResponseMessage(HttpStatusCode.NotFound);
        var package = await File.ReadAllBytesAsync(packagePath, cancellationToken);
        if (request.RequestUri!.Host == "api.github.com") return new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { draft = false, prerelease = false, assets = new[] { new { name = "ArchivePassword-engine-26.04-win-x64.zip", size = package.Length, browser_download_url = "https://github.com/Nomoziya/nomozip/releases/download/test/ArchivePassword-engine-26.04-win-x64.zip" } } })) };
        return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(package) };
    }
}
