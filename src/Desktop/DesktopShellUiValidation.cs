using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;
using Microsoft.Win32;

namespace ArchivePassword.Desktop;
internal static class DesktopShellUiValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, ThemeResources theme, string root, string baseDirectory)
    {
        var scope = @"Software\nomozip-validation\ShellUi-" + Guid.NewGuid().ToString("N");
        var data = Path.Combine(root, "artifacts/shell-ui", Guid.NewGuid().ToString("N"));
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool passed) => checks[name] = passed;
        async Task Layout() => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        try
        {
            var model = new MainViewModel(baseDirectory, data, new DpapiPasswordVault(Path.Combine(data,"passwords.apv")), new WindowDialogs(window), scope);
            window.DataContext = model; await model.InitializeAsync(); model.Page = 2; model.SettingsSection = 3;
            foreach (var contrast in new[] { false, true })
            {
                theme.Apply(contrast);
                foreach (var width in new[] { 1120, 800 })
                {
                    window.Width = width; window.Height = width == 800 ? 500 : 760; await Layout();
                    foreach (var name in new[] { "RegisterShellMenuButton", "RemoveShellMenuButton" })
                    {
                        var button = (Button)window.FindName(name); button.BringIntoView(); await Layout();
                        var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
                        Check($"{name} reachable {width} contrast {contrast}", button.IsVisible && button.IsEnabled && peer.IsKeyboardFocusable() && !string.IsNullOrEmpty(peer.GetName()));
                        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); await Layout();
                        var registered = new ShellMenuService(Path.Combine(baseDirectory,"ArchivePassword.exe"),scope).IsRegistered();
                        Check($"{name} actual binding {width} contrast {contrast}", registered == (name == "RegisterShellMenuButton"));
                    }
                }
            }
            theme.Apply(SystemParameters.HighContrast);
            Directory.CreateDirectory(data);
            var source = Path.Combine(data,"中文示例.txt");
            File.Copy(Path.Combine(root,"tests/fixtures/generated/中文示例.txt"),source);
            await model.ApplyShellSelectionAsync(new ShellSelection(ShellAction.Compress,[source]));
            Check("compression menu opens populated page", model.Page == 3 && model.CompressionSources.Single().Path == Path.GetFullPath(source) && !model.CompressionRunning);
            model.CompressionFormat = 1;
            Check("format change updates suggested extension", model.CompressionOutput.EndsWith(".zip"));
            model.CreateArchiveCommand.Execute(null);
            Check("compression disables registration until idle", !model.RegisterShellMenuCommand.CanExecute(null));
            await model.WaitForIdleAsync();
            var engine = new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines/26.04/7z.dll"));
            Check("menu selected source creates real validated ZIP", File.Exists(model.CompressionOutput) && (await engine.TestAsync(model.CompressionOutput,null)).IsSuccess);
            // The archive is created beside a synthetic source, then removed after verification.
            if (File.Exists(model.CompressionOutput)) File.Delete(model.CompressionOutput);
            Check("registration re-enabled after compression", model.RegisterShellMenuCommand.CanExecute(null));
        }
        catch (Exception error) { checks["unexpected " + error.GetType().Name] = false; }
        finally { Registry.CurrentUser.DeleteSubKeyTree(scope,false); theme.Apply(SystemParameters.HighContrast); }
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/shell-ui-validation.json"), JsonSerializer.Serialize(new { passed = checks.Values.All(value => value),total=checks.Count,checks,isolatedRegistry=true }));
        return checks.Values.All(value => value);
    }
}
