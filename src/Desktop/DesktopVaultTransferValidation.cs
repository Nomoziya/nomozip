using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopVaultTransferValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, ThemeResources theme, string root, string baseDirectory, string dataDirectory)
    {
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool passed) { checks[name] = passed; }
        var screenshots = Path.Combine(root, "artifacts/vault-transfer-preview"); Directory.CreateDirectory(screenshots);
        var backup = Path.Combine(dataDirectory, "acceptance.nvault");
        var vaultPath = Path.Combine(dataDirectory, "passwords.apv");
        var passphrase = "合成测试备份密码 2026";
        var source = new DpapiPasswordVault(vaultPath);
        var item = new SavedPassword(Guid.NewGuid(), "合成迁移密码", "测试密碼 ' $ & 空格", "只用于自动验收，非真实密码", true, DateTimeOffset.UtcNow);
        var cancelPreview = false; var keepBoth = false; var wrongPassword = false; var cancelPassword = false;
        var protectedDestination = false;
        try
        {
            await source.SaveAsync(item);
            var dialogs = new WindowDialogs(window, selectVaultBackup: exporting => protectedDestination ? vaultPath : backup,
                prepareVaultBackup: dialog => Prepare(dialog, async () =>
                {
                    if (cancelPassword) { dialog.Close(); return; }
                    var secret = (PasswordBox)dialog.FindName("SecretBox");
                    var confirmation = (PasswordBox)dialog.FindName("ConfirmationBox");
                    var action = (Button)dialog.FindName("ContinueButton");
                    var peer = UIElementAutomationPeer.CreatePeerForElement(secret)!;
                    Check("backup password remains masked and keyboard reachable", peer.IsPassword() && peer.GetName() == "备份密码" && peer.IsKeyboardFocusable());
                    if (confirmation.IsVisible)
                    {
                        secret.Password = "short"; Invoke(action); await Layout(dialog);
                        Check("short export password shows accessible inline error", !string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorLabel")).Text) && dialog.IsVisible);
                        secret.Password = passphrase; confirmation.Password = "mismatch"; Invoke(action); await Layout(dialog);
                        Check("password confirmation mismatch stops export", ((TextBlock)dialog.FindName("ErrorLabel")).Text.Contains("不一致") && dialog.IsVisible);
                        confirmation.Password = passphrase;
                        Screenshot(dialog, Path.Combine(screenshots, "export-dialog.png"));
                    }
                    else secret.Password = wrongPassword ? "synthetic wrong password" : passphrase;
                    Invoke(action);
                }), prepareVaultImport: dialog => Prepare(dialog, async () =>
                {
                    var table = (DataGrid)dialog.FindName("PreviewTable");
                    Check("import preview omits secret column", table.Columns.Count == 3 && table.Columns.All(column => !column.Header.ToString()!.Contains("密码")));
                    var combo = (ComboBox)dialog.FindName("ModeBox");
                    Check("preview defaults to keeping local duplicates", combo.SelectedIndex == 0 && dialog.Mode == PasswordImportMode.SkipDuplicates);
                    if (keepBoth) combo.SelectedIndex = 1;
                    await Layout(dialog);
                    var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
                    Check("preview row accessibility names the record and action", UIElementAutomationPeer.CreatePeerForElement(row)!.GetName().Contains(item.Name));
                    var cells = Descendants(row).OfType<DataGridCell>().ToArray();
                    Check("preview cells expose record names instead of generic cell labels", cells.Length == 3
                        && cells.All(cell => UIElementAutomationPeer.CreatePeerForElement(cell)!.GetName().Contains(item.Name)));
                    Screenshot(dialog, Path.Combine(screenshots, "import-preview.png"));
                    if (cancelPreview) dialog.Close(); else Invoke((Button)dialog.FindName("ImportButton"));
                }));
            var model = new MainViewModel(baseDirectory, dataDirectory, source, dialogs);
            window.DataContext = model; await model.InitializeAsync(); model.Page = 1;
            await Layout(window);
            async Task Execute(string button)
            {
                await Layout(window);
                Invoke((Button)window.FindName(button));
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await model.WaitForIdleAsync(); await Layout(window);
            }
            var before = await File.ReadAllBytesAsync(vaultPath);
            await Execute("ExportVaultButton");
            Check("actual export button creates encrypted backup without vault change", File.Exists(backup) && model.Banner.Contains("已导出")
                && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(vaultPath)));
            protectedDestination = true; await Execute("ExportVaultButton"); protectedDestination = false;
            Check("export cannot overwrite local vault", model.Banner.Contains("不能覆盖") && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(vaultPath)));
            cancelPassword = true; await Execute("ImportVaultButton"); cancelPassword = false;
            Check("cancel password dialog keeps original bytes", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(vaultPath)) && !model.IsBusy);
            wrongPassword = true; await Execute("ImportVaultButton"); wrongPassword = false;
            Check("wrong password command reports no import", model.Banner.Contains("不正确") && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(vaultPath)));
            cancelPreview = true; await Execute("ImportVaultButton"); cancelPreview = false;
            Check("cancel import preview keeps original bytes", model.Banner.Contains("已取消") && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(vaultPath)));
            keepBoth = true; await Execute("ImportVaultButton"); keepBoth = false;
            Check("actual import button keeps both and refreshes list", (await source.ReadAsync()).Count == 2 && model.PasswordRows.Count == 2 && model.Banner.Contains("新增 1"));
            await source.DeleteAsync(item.Id);
            foreach (var saved in await source.ReadAsync()) await source.DeleteAsync(saved.Id);
            model = new MainViewModel(baseDirectory, dataDirectory, source, dialogs); window.DataContext = model; await model.InitializeAsync(); model.Page = 1;
            Check("empty vault enables import and disables export", model.ImportVaultCommand.CanExecute(null) && !model.ExportVaultCommand.CanExecute(null));
            await Execute("ImportVaultButton");
            Check("import into empty vault survives reopen", (await new DpapiPasswordVault(vaultPath).ReadAsync()).Single() == item);
            model.Tasks.Clear(); model.OutputPath = Path.Combine(dataDirectory, "migrated-output"); model.Page = 0;
            model.AddFiles([Path.Combine(root, "tests/fixtures/generated/encrypted-header.7z")]);
            model.ExtractCommand.Execute(null); await model.WaitForIdleAsync();
            Check("desktop extraction automatically uses imported password", model.Tasks.Single().Completed && File.Exists(Path.Combine(model.OutputPath, "中文示例.txt")));
            foreach (var highContrast in new[] { false, true })
            {
                theme.Apply(highContrast);
                foreach (var width in new[] { 1120, 800 })
                {
                    model.Page = 1; window.Width = width; window.Height = width == 800 ? 500 : 760;
                    await Layout(window);
                    var first = (Button)window.FindName("ImportVaultButton"); var last = (Button)window.FindName("ExportVaultButton");
                    Check($"vault actions visible width {width} contrast {highContrast}", Fits(window, first) && Fits(window, last));
                    Screenshot(window, Path.Combine(screenshots, $"vault-{width}-{highContrast}.png"));
                }
                var small = new VaultImportDialog([item], [item]) { Owner = window, ShowActivated = false, ShowInTaskbar = false };
                small.WindowStartupLocation = WindowStartupLocation.Manual; small.Left = -32000; small.Top = -32000;
                small.Width = 420; small.Height = 360; small.MinHeight = 300; small.Show(); await Layout(small);
                Check($"small import preview keeps actions visible contrast {highContrast}", Fits(small, (Button)small.FindName("ImportButton")));
                var smallTable = (DataGrid)small.FindName("PreviewTable"); smallTable.BringIntoView(); await Layout(small);
                Check($"small preview keeps record rows reachable contrast {highContrast}", smallTable.ActualHeight >= 84 && smallTable.IsVisible);
                Screenshot(small, Path.Combine(screenshots, $"small-preview-{highContrast}.png")); small.Close();
            }
            theme.Apply(SystemParameters.HighContrast);
        }
        catch (Exception exception) { checks["unexpected failure " + exception.GetType().Name] = false; }
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts/desktop-vault-transfer-validation.json"), JsonSerializer.Serialize(new
        { passed = checks.Count > 0 && checks.Values.All(value => value), total = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Count > 0 && checks.Values.All(value => value);
    }
    private static void Prepare(Window dialog, Func<Task> action)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000; dialog.ShowActivated = false;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(async () => await action());
    }
    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    private static bool Fits(Window window, FrameworkElement element)
    {
        var content = (FrameworkElement)window.Content;
        var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
        return element.IsVisible && bounds.Top >= 0 && bounds.Left >= 0 && bounds.Bottom <= content.ActualHeight + 1 && bounds.Right <= content.ActualWidth + 1;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Screenshot(Window window, string path)
    {
        window.UpdateLayout();
        DesktopAccessibilityValidation.Render(window, path, 1.0);
    }
}
