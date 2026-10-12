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

internal static class DesktopFailureValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, string baseDirectory, string dataDirectory, string root)
    {
        var checks = new Dictionary<string, bool>();
        var vault = new DpapiPasswordVault(Path.Combine(dataDirectory, "passwords.apv"));
        var before = await File.ReadAllBytesAsync(Path.Combine(dataDirectory, "passwords.apv"));
        var original = window.DataContext;
        var prompts = 0; var shown = 0;
        void Check(string name, bool value) => checks[name] = value;
        var model = new MainViewModel(baseDirectory, dataDirectory, vault, new WindowDialogs(window,
            prepareDialog: dialog =>
            {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000; dialog.ShowActivated = false;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (prompts++ == 0)
                    {
                        ((PasswordBox)dialog.FindName("SecretBox")).Password = "password";
                        ((CheckBox)dialog.FindName("SavePassword")).IsChecked = true;
                        ((Button)dialog.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    else
                    {
                        var summary = (TextBlock)dialog.FindName("FailureSummary");
                        Check("password retry shows partial counts and not extracted", summary.Text.Contains("项通过") && summary.Text.Contains("项失败") && summary.Text.Contains("尚未解压") && summary.Text.Contains("其它密码"));
                        var button = ((StackPanel)dialog.FindName("FailurePanel")).Children.OfType<Button>().Single();
                        ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
                        // Invoke is dispatched; close the password prompt after the nested details dialog.
                        dialog.Dispatcher.BeginInvoke(() => dialog.Close(), DispatcherPriority.ApplicationIdle);
                    }
                });
            }, prepareFailure: dialog =>
            {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000; dialog.ShowActivated = false;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                {
                    shown++;
                    dialog.Width = 640; dialog.Height = 400; dialog.UpdateLayout();
                    var table = (DataGrid)dialog.FindName("FailureTable");
                    var rows = table.ItemsSource.Cast<ExtractionFailureRow>().ToArray();
                    Check("actual details list failure paths and reasons", rows.Length > 0 && rows.All(value => !value.Path.StartsWith("条目 ") && value.Reason.Contains("密码")) && table.IsReadOnly);
                    var peer = UIElementAutomationPeer.CreatePeerForElement(table)!;
                    Check("details list has keyboard and automation name", peer.IsKeyboardFocusable() && peer.GetName() == "解压失败条目列表");
                    table.ScrollIntoView(table.Items[0]); table.UpdateLayout();
                    var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
                    var bounds = row.TransformToAncestor(table).TransformBounds(new Rect(row.RenderSize));
                    Check("small failure dialog has whole visible row", bounds.Bottom <= table.ActualHeight && bounds.Top >= 0);
                    DesktopAccessibilityValidation.Render(dialog, Path.Combine(root, "artifacts/accessibility-preview/extraction-failure-640.png"), 1);
                    dialog.Close();
                });
            }));
        try
        {
            await model.InitializeAsync(); window.DataContext = model; model.AutoTry = false;
            model.OutputPath = Path.Combine(dataDirectory, "failure-must-not-extract");
            model.AddFiles([Path.Combine(root, "tests/fixtures/generated/test_read_format_rar5_encrypted.rar")]); model.SelectedTask = model.Tasks.Single();
            Check("failure command unavailable before processing", !model.ShowFailureCommand.CanExecute(null));
            model.ExtractCommand.Execute(null); await model.WaitForIdleAsync();
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Check("partial result stays incomplete with retained diagnostics", model.IsIdle && !model.SelectedTask.Completed && model.SelectedTask.Failure is { SuccessfulItems: > 0 } && model.SelectedTask.Status.Contains("项失败"));
            var action = (Button)window.FindName("FailureDetailsAction");
            Check("failure action remains available after cancellation", action.IsVisible && action.IsEnabled && model.ShowFailureCommand.CanExecute(null));
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(action)!.GetPattern(PatternInterface.Invoke)).Invoke();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check("retry and final task both open real details", prompts == 2 && shown == 2);
            var after = await File.ReadAllBytesAsync(Path.Combine(dataDirectory, "passwords.apv"));
            Check("no extraction output or vault writes on partial test", !Directory.EnumerateFileSystemEntries(model.OutputPath).Any() && before.SequenceEqual(after));
            var legacy = new ExtractionFailureDialog("legacy.zip", new EngineResult { Failures = [new(2, "corrupt_archive")] });
            var fallback = ((DataGrid)legacy.FindName("FailureTable")).ItemsSource.Cast<ExtractionFailureRow>().Single();
            Check("older worker result identifies item without invented path", fallback.Path == "条目 3（引擎未提供路径）" && fallback.Reason == "文件损坏");
            legacy.Close();
        }
        finally
        {
            window.DataContext = original;
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts/desktop-failure-validation.json"), JsonSerializer.Serialize(new
            { passed = checks.Count == 10 && checks.Values.All(value => value), total = checks.Count, checks, syntheticData = true, humanDesktopAccepted = false }));
        }
        return checks.Count == 10 && checks.Values.All(value => value);
    }
}
