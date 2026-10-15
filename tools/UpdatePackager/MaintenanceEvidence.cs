using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class MaintenanceEvidence
{
    internal static async Task<JsonObject> ReadAsync(string reviewPath, string engineDirectory, string version, string publicKey)
    {
        var candidate = Path.GetDirectoryName(Path.GetFullPath(reviewPath))!;
        using var review = JsonDocument.Parse(await File.ReadAllBytesAsync(reviewPath));
        var root = review.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1 || root.GetProperty("status").GetString() != "ready-for-signing" || root.GetProperty("version").GetString() != version
            || !Path.GetFullPath(engineDirectory).Equals(Path.Combine(candidate, "workspace", "engines", version), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A prepared candidate for this exact engine is required.");
        using var manifest=JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(engineDirectory,"manifest.json")));
        var architecture=manifest.RootElement.GetProperty("architecture").GetString()!;
        if(root.GetProperty("architecture").GetString()!=architecture || architecture is not ("x64" or "arm64")) throw new InvalidOperationException("Candidate architecture differs from the engine.");
        var reports = new[] { "engine-validation.json", "compression-validation.json", "selection-validation.json", "encoding-validation.json", "pause-validation.json", "archive-editing-validation.json", "volume-validation.json", "sfx-validation.json", "format-validation.json", "architecture-validation.json", "conversion-validation.json", "preview-validation.json" };
        var names = new[] { "7z.dll", "7z.exe", "7z.sfx", "License.txt", "manifest.json" }.Select(name => $"workspace/engines/{version}/{name}")
            .Concat(new[] { "public-key.pem", "workspace/artifacts/worker/ArchiveEngineWorker.exe" })
            .Concat(reports.Select(name => "workspace/artifacts/" + name)).ToArray();
        var hashes = root.GetProperty("hashes");
        if (hashes.EnumerateObject().Count() != names.Length || names.Any(name => !hashes.TryGetProperty(name, out _)))
            throw new InvalidOperationException("Complete bound maintenance evidence is required.");
        async Task<string> Hash(string path) => Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
        foreach (var name in names)
            if (await Hash(Path.Combine(candidate, name.Replace('/', Path.DirectorySeparatorChar))) != hashes.GetProperty(name).GetString())
                throw new InvalidOperationException("Candidate binaries or maintenance evidence changed.");
        if (await Hash(publicKey) != hashes.GetProperty("public-key.pem").GetString()) throw new InvalidOperationException("Candidate uses another product public key.");
        if (!ArchivePassword.Core.EngineArchitecture.IsNativeImage(await File.ReadAllBytesAsync(Path.Combine(candidate,"workspace/artifacts/worker/ArchiveEngineWorker.exe")),architecture)) throw new InvalidOperationException("Candidate worker architecture differs from the native engine.");
        var result = new JsonObject { ["workerSha256"] = hashes.GetProperty("workspace/artifacts/worker/ArchiveEngineWorker.exe").GetString() };
        var dllHash = await Hash(Path.Combine(engineDirectory, "7z.dll"));
        foreach (var name in reports.Skip(2))
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(candidate, "workspace", "artifacts", name));
            using var document = JsonDocument.Parse(bytes); var report = document.RootElement;
            var values = report.TryGetProperty("checks",out var checks)
                ? checks.EnumerateObject().Select(value=>(Name:value.Name,Passed:value.Value.ValueKind==JsonValueKind.True)).ToArray()
                : report.GetProperty("cases").EnumerateArray().Select(value=>(Name:value.GetProperty("name").GetString()!,Passed:value.GetProperty("passed").ValueKind==JsonValueKind.True)).ToArray();
            var passed = report.GetProperty("passed"); var total = report.GetProperty("total").GetInt32();
            var minimum = name == "pause-validation.json" ? 4 : name == "architecture-validation.json" ? 10 : 20;
            if (total < 1 || total != values.Length || values.Any(value => !value.Passed)
                || !(passed.ValueKind == JsonValueKind.True || passed.ValueKind == JsonValueKind.Number && passed.GetInt32() == total)
                || values.Count(value => value.Name.StartsWith(version + " ", StringComparison.Ordinal)) < minimum)
                throw new InvalidOperationException("Passing target-version maintenance coverage is required.");
            if ((name is "pause-validation.json" or "archive-editing-validation.json" or "volume-validation.json" or "sfx-validation.json" or "format-validation.json" or "architecture-validation.json" or "conversion-validation.json" or "preview-validation.json") && !dllHash.Equals(report.GetProperty("engineHashes").GetProperty(version).GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Maintenance evidence belongs to another engine binary.");
            if(name=="sfx-validation.json" && !hashes.GetProperty($"workspace/engines/{version}/7z.sfx").GetString()!.Equals(report.GetProperty("moduleHashes").GetProperty(version).GetString(),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SFX evidence belongs to another module.");
            if(name=="architecture-validation.json" && (report.GetProperty("architecture").GetString()!=architecture || !architecture.Equals(report.GetProperty("osArchitecture").GetString(),StringComparison.OrdinalIgnoreCase) || !report.GetProperty("nativeProcess").GetBoolean())) throw new InvalidOperationException("Actual native target execution is required.");
            result[name] = JsonNode.Parse(bytes);
        }
        return result;
    }
}
