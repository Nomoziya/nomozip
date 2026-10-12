using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopBrowserValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, string baseDirectory, string dataDirectory, string root)
    {
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool passed) => checks[name] = passed;
        var path = Path.Combine(dataDirectory, "passwords.apv");
        var vault = new DpapiPasswordVault(path);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var original = window.DataContext;
        var dialogsShown = 0;
        var passwordPrompts = 0;
        var model = new MainViewModel(baseDirectory, dataDirectory, vault, new WindowDialogs(window,
            dialog =>
            {
                passwordPrompts++;
                dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000; dialog.ShowActivated = false;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                {
                    Check("browse password has no save option", !((CheckBox)dialog.FindName("SavePassword")).IsEnabled
                        && ((CheckBox)dialog.FindName("SavePassword")).Visibility == Visibility.Collapsed && dialog.Title == "输入查看密码");
                    ((PasswordBox)dialog.FindName("SecretBox")).Password = "测试密碼 ' $ & 空格";
                    ((Button)dialog.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
            }, prepareContents: dialog =>
            {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000; dialog.ShowActivated = false;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(async () =>
                {
                    dialogsShown++;
                    var view = (ArchiveContentsViewModel)dialog.DataContext;
                    var table = (DataGrid)dialog.FindName("EntryTable");
                    var search = (TextBox)dialog.FindName("EntrySearch");
                    Check("actual content dialog lists archive filenames", view.Rows.Any(row => row.Path == "中文示例.txt") && table.IsReadOnly);
                    Check("content search is named and keyboard focusable", UIElementAutomationPeer.CreatePeerForElement(search)!.GetName() == "搜索包内文件或文件夹路径"
                        && UIElementAutomationPeer.CreatePeerForElement(search)!.IsKeyboardFocusable());
                    search.Text = "not-present"; search.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    Check("search has empty match state", view.Rows.Count == 0 && view.Summary.Contains("显示 0 /"));
                    search.Text = "中文"; search.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    Check("search filters actual paths", view.Rows.Count == 1 && view.Rows[0].Path == "中文示例.txt");
                    search.Text = ""; search.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    WindowPlacement.FitWorkArea(dialog, new Rect(0, 0, 640, 400));
                    await dialog.Dispatcher.InvokeAsync(() => dialog.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                    Check("contents dialog fits small work area", dialog.Width <= 640 && dialog.Height <= 400 && dialog.MinHeight <= 400);
                    var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
                    var rowBounds = row.TransformToAncestor(table).TransformBounds(new Rect(row.RenderSize));
                    Check("small contents area shows whole file row", rowBounds.Top >= 0 && rowBounds.Bottom <= table.ActualHeight);
                    Directory.CreateDirectory(Path.Combine(root, "artifacts/accessibility-preview"));
                    DesktopAccessibilityValidation.Render(dialog, Path.Combine(root, "artifacts/accessibility-preview/archive-contents-640.png"), 1);
                    dialog.Close();
                });
            }));
        try
        {
            await model.InitializeAsync(); window.DataContext = model;
            model.OutputPath = Path.Combine(dataDirectory, "browser-must-not-extract");
            var action = (Button)window.FindName("BrowseArchiveAction");
            foreach (var item in new[] { ("encrypted-header.7z", true), ("encrypted-header.7z", false), ("encrypted-aes-ascii.zip", false) })
            {
                model.Tasks.Clear(); model.AddFiles([Path.Combine(root, "tests/fixtures/generated", item.Item1)]);
                model.SelectedTask = model.Tasks.Single(); model.AutoTry = item.Item2;
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(action)!.GetPattern(PatternInterface.Invoke)).Invoke();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await model.WaitForIdleAsync();
                Check(item.Item1 + " automatic " + item.Item2 + " does not mark extracted", !model.Tasks.Single().Completed && model.IsIdle);
            }
            Check("three real content command flows", dialogsShown == 3 && passwordPrompts == 1);
            Check("browse shortcut available", window.InputBindings.OfType<KeyBinding>().Any(value => value.Key == Key.L && value.Modifiers == ModifierKeys.Control && ReferenceEquals(value.Command, model.BrowseArchiveCommand)));
            var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
            Check("no output or password vault writes", !Directory.Exists(model.OutputPath) && before.SequenceEqual(after));
        }
        finally
        {
            window.DataContext = original;
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts/desktop-browser-validation.json"), JsonSerializer.Serialize(new
            { passed = checks.Count == 13 && checks.Values.All(value => value), total = checks.Count, checks, syntheticData = true, humanDesktopAccepted = false }));
        }
        return checks.Count == 13 && checks.Values.All(value => value);
    }
}
