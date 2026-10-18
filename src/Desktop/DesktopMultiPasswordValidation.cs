using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using ArchivePassword.Infrastructure;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

internal static class DesktopMultiPasswordValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, string baseDirectory, string dataDirectory, string root)
    {
        var checks = new Dictionary<string, bool>(); var original = window.DataContext;
        using var confirmation=DesktopExtractionConfirmationValidation.Accept(window);
        try
        {
            foreach (var savedCount in new[] { 2, 1, 0 })
            {
                var data = Path.Combine(dataDirectory, "multi-password-" + savedCount); Directory.CreateDirectory(data);
                var vault = new DpapiPasswordVault(Path.Combine(data, "passwords.apv"));
                for (var index = 0; index < savedCount; index++) await vault.SaveAsync(new SavedPassword(Guid.NewGuid(), "saved " + index, index == 0 ? "password" : "password2", "", true, DateTimeOffset.UtcNow));
                var prompts = 0;
                var model = new MainViewModel(baseDirectory, data, vault, new WindowDialogs(window, dialog =>
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000; dialog.ShowActivated = false;
                    dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                    {
                        var secret = savedCount == 1 || prompts > 0 ? "password2" : "password";
                        if (secret == "password2") checks[savedCount + " supplementary prompt explains verified passwords"] = ((TextBlock)dialog.FindName("FailureSummary")).Text.Contains("已验证的密码");
                        prompts++;
                        ((PasswordBox)dialog.FindName("SecretBox")).Password = secret;
                        ((CheckBox)dialog.FindName("SavePassword")).IsChecked = true;
                        ((Button)dialog.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    });
                }));
                await model.InitializeAsync(); window.DataContext = model; model.AutoTry = true;
                model.OutputPath = Path.Combine(data, "output"); model.AddFiles([Path.Combine(root, "tests/fixtures/generated/mixed-aes.zip")]);
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                var button = (Button)window.FindName("ExtractAction");
                ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var deadline=DateTime.UtcNow.AddSeconds(30);
                while(!model.IsBusy && !model.Tasks.Single().Completed)
                {if(DateTime.UtcNow>deadline)throw new TimeoutException("Extraction did not start after confirmation.");await Task.Delay(25);}
                await model.WaitForIdleAsync();
                checks[savedCount + " actual extraction command completes with expected prompts"] = model.IsIdle && model.Tasks.Single().Completed && prompts == 2 - savedCount;
                checks[savedCount + " actual output matches original bytes"] = new[] { "第一文件.txt", "第二文件.txt" }.All(name => File.ReadAllBytes(Path.Combine(model.OutputPath, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/generated", name))));
                var records = await vault.ReadAsync();
                checks[savedCount + " verified passwords retained and used"] = records.Count == 2 && records.All(value => value.LastSuccessAt is not null) && records.Select(value => value.Secret).Order().SequenceEqual(new[] { "password", "password2" });
                checks[savedCount + " successful task has no failure action"] = model.Tasks.Single().Failure is null && !model.ShowFailureCommand.CanExecute(null);
            }
        }
        finally
        {
            window.DataContext = original;
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts/desktop-multi-password-validation.json"), JsonSerializer.Serialize(new
            { passed = checks.Count == 14 && checks.Values.All(value => value), total = checks.Count, checks, syntheticData = true, humanDesktopAccepted = false }));
        }
        return checks.Count == 14 && checks.Values.All(value => value);
    }
}
