using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopInstalledValidation
{
    public static async Task<bool> RunAsync(MainViewModel model, DpapiPasswordVault vault, string baseDirectory, string root, bool sharedRuntime = false)
    {
        var runtimeLocal = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory()).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        var extraction = false;
        var reused = false;
        try
        {
            var saved = await vault.ReadAsync();
            reused = saved.Count > 0;
            if (!reused) await vault.SaveAsync(new SavedPassword(Guid.NewGuid(), "synthetic installed sample", "测试密碼 ' $ & 空格", "", true, DateTimeOffset.UtcNow));
            await model.InitializeAsync();
            model.OutputPath = Path.Combine(root, "artifacts/installed-validation/output", Guid.NewGuid().ToString("N"));
            var fixture=Path.GetFullPath(Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"));
            model.AddFiles([fixture]);model.SelectedTask=model.Tasks.Single(task=>task.Path==fixture);
            model.ExtractSelectedTaskCommand.Execute(null); await model.WaitForIdleAsync();
            var actual = Path.Combine(model.OutputPath, "中文示例.txt");
            extraction = model.Tasks.Single(task=>task.Path==fixture).Completed && File.Exists(actual)
                && File.ReadAllBytes(actual).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/generated/中文示例.txt")));
        }
        catch { extraction = false; }
        var passed = runtimeLocal != sharedRuntime && extraction;
        await File.WriteAllTextAsync(Path.Combine(root, sharedRuntime ? "artifacts/light-installed-smoke-validation.json" : "artifacts/installed-smoke-validation.json"), JsonSerializer.Serialize(new
        { passed, runtimeLocal, sharedRuntime, runtimeVersion = RuntimeEnvironment.GetSystemVersion(), savedPasswordExtraction = extraction, existingVaultReused = reused, realNetwork = false, syntheticData = true }));
        return passed;
    }
}
