using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

// Explicit maintainer acceptance mode: real public requests, isolated synthetic data.
internal static class DesktopOnlineValidation
{
    public static async Task<bool> RunAsync(Window window, MainViewModel model, DpapiPasswordVault vault,
        string baseDirectory, string dataDirectory, string root)
    {
        var checks = new Dictionary<string, bool>();
        try
        {
            Directory.CreateDirectory(dataDirectory);
            await vault.SaveAsync(new SavedPassword(Guid.NewGuid(), "synthetic online update sample", "测试密碼 ' $ & 空格", "", true, DateTimeOffset.UtcNow));
            await model.InitializeAsync();
            model.SelectedEngineVersion = "26.03";
            checks["olderEngineSelectable"] = model.ApplyEngineCommand.CanExecute(null);
            model.ApplyEngineCommand.Execute(null); await model.WaitForIdleAsync();
            checks["olderEngineActive"] = model.EngineSummary.Contains("26.03");
            var before = await File.ReadAllBytesAsync(Path.Combine(dataDirectory, "passwords.apv"));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            model.CheckUpdatesCommand.Execute(null); await model.WaitForIdleAsync();
            checks["publicReleaseDiscovered"] = model.InstallUpdateCommand.CanExecute(null);
            model.InstallUpdateCommand.Execute(null); await model.WaitForIdleAsync();
            checks["downloadSignatureInstallAndActivation"] = model.EngineSummary.Contains("26.04") && File.Exists(Path.Combine(dataDirectory, "engines/26.04/manifest.sig"));
            var after = await File.ReadAllBytesAsync(Path.Combine(dataDirectory, "passwords.apv"));
            checks["vaultUnchangedDuringUpdate"] = before.SequenceEqual(after);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            model.OutputPath = Path.Combine(dataDirectory, "output");
            model.AddFiles([Path.Combine(root, "tests/fixtures/generated/encrypted-header.7z")]);
            model.ExtractCommand.Execute(null); await model.WaitForIdleAsync();
            var extracted = Path.Combine(model.OutputPath, "中文示例.txt");
            checks["updatedEngineExtractsWithSavedPassword"] = model.Tasks.Single().Completed && File.Exists(extracted)
                && File.ReadAllBytes(extracted).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/generated/中文示例.txt")));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            model.RollbackEngineCommand.Execute(null); await model.WaitForIdleAsync();
            checks["rollback"] = model.EngineSummary.Contains("26.03");
            var reopened = new MainViewModel(baseDirectory, dataDirectory, vault, new WindowDialogs(window));
            await reopened.InitializeAsync();
            checks["rollbackPersistsAfterReopen"] = reopened.EngineSummary.Contains("26.03");
        }
        catch (Exception exception)
        {
            // Report only the exception type, never vault values or credential data.
            checks["completedWithoutException_" + exception.GetType().Name] = false;
        }
        var passed = checks.Count >= 8 && checks.Values.All(value => value);
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts/online-validation.json"), JsonSerializer.Serialize(new
        {
            passed, repository = "Nomoziya/nomozip", realPublicNetwork = true, syntheticData = true, checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }
}
