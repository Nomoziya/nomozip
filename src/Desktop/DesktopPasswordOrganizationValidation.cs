using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopPasswordOrganizationValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, ThemeResources theme, string root, string baseDirectory)
    {
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool passed) => checks[name] = passed;
        var data = Path.Combine(root, "artifacts/password-organization-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        var vaultPath = Path.Combine(data, "synthetic.apv"); var vault = new DpapiPasswordVault(vaultPath);
        var item = new SavedPassword(Guid.NewGuid(), "合成旧记录", "synthetic shared secret", "备注示例", true, DateTimeOffset.UtcNow);
        var editorCancel = false; var allowDuplicate = false; var allowMerge = false; var confirmation = "";
        MainViewModel? model = null;
        try
        {
            await vault.SaveAsync(item);
            var other = item with { Id = Guid.NewGuid(), Name = "合成另一记录", Secret = "another synthetic secret", Group = "其它分组", Source = "other.example" };
            await vault.SaveAsync(other);
            var dialogs = new WindowDialogs(window, prepareDialog: dialog =>
            {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000; dialog.ShowActivated = false;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (editorCancel) { dialog.Close(); return; }
                        var secret = (PasswordBox)dialog.FindName("SecretBox");
                        Check("organization editor still masks password", UIElementAutomationPeer.CreatePeerForElement(secret)!.IsPassword());
                        secret.Password = item.Secret;
                        foreach (var (name, text) in new[] { ("NameBox", "合成整理记录"), ("GroupBox", "照片"), ("SourceBox", "download.example"), ("MatchBox", "Trip") })
                        {
                            var input = (TextBox)dialog.FindName(name);
                            Check("editor field has accessible label " + name, UIElementAutomationPeer.CreatePeerForElement(input)!.GetName().Length > 0);
                            input.Text = text;
                        }
                        Check("optional rule limits text and is disclosed progressively", ((TextBox)dialog.FindName("MatchBox")).MaxLength == 256 && !((TextBox)dialog.FindName("MatchBox")).IsVisible);
                        Check("editor save action fits work area", Fits(dialog, (Button)dialog.FindName("ContinueButton")));
                        Invoke((Button)dialog.FindName("ContinueButton"));
                    }
                    catch (Exception exception) { Check("editor exception " + exception.GetType().Name, false); dialog.Close(); }
                });
            }, confirmPasswordOrganization: (kind, count) => { confirmation = kind + ":" + count; return kind == "duplicate" ? allowDuplicate : allowMerge; });
            model = new MainViewModel(baseDirectory, data, vault, dialogs);
            window.DataContext = model; await model.InitializeAsync(); model.Page = 1;
            async Task Execute(System.Windows.Input.ICommand command)
            {
                await Layout(window); command.Execute(null); await model.WaitForIdleAsync();
                await Layout(window);
            }
            model.SelectedPassword = model.PasswordRows.Single(value => value.Id == item.Id);
            await Execute(model.EditPasswordCommand);
            var edited = (await vault.ReadAsync()).Single(value => value.Id == item.Id);
            Check("actual edit saves metadata and preserves identity", edited.Id == item.Id && edited.Group == "照片" && edited.Source == "download.example" && edited.MatchText == "Trip" && edited.CreatedAt == item.CreatedAt);
            model.SelectedPassword = model.PasswordRows.First(value => value.Id == item.Id);
            var before = await File.ReadAllBytesAsync(vaultPath); editorCancel = true;
            await Execute(model.EditPasswordCommand); editorCancel = false;
            Check("cancel editor preserves original encrypted bytes", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(vaultPath)));
            model.Search = "DOWNLOAD.EXAMPLE"; await Layout(window);
            Check("actual search textbox binding finds source", model.PasswordRows.Count == 1 && ((TextBox)window.FindName("PasswordSearch")).Text == model.Search);
            model.Search = ""; model.SelectedPasswordGroup = model.PasswordGroups.Single(value => value.Value == "照片"); await Layout(window);
            Check("actual group filter excludes unrelated records", model.PasswordRows.Count == 1 && Equals(((ComboBox)window.FindName("PasswordGroupBox")).SelectedItem, model.SelectedPasswordGroup));
            model.SelectedPasswordGroup = model.PasswordGroups.Single(value => value.Value is null); model.Search = item.Secret;
            Check("search cannot expose password through secret matches", model.PasswordRows.Count == 0);
            model.Search = "";
            confirmation = ""; await Execute(model.AddPasswordCommand);
            Check("duplicate reminder prevents accidental save when declined", confirmation == "duplicate:1" && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(vaultPath)));
            allowDuplicate = true; await Execute(model.AddPasswordCommand);
            Check("explicit duplicate save refreshes library", (await vault.ReadAsync()).Count == 3 && model.PasswordRows.Count == 3);
            var duplicate = (await vault.ReadAsync()).Single(value => value.Id != item.Id && value.Secret == item.Secret);
            await vault.RecordSuccessAsync(duplicate.Id, Path.Combine(data, "association.zip"));
            model.SelectedPassword = model.PasswordRows.Single(value => value.Id == item.Id); await Layout(window);
            Check("duplicate summary and merge button available", model.DuplicateSummary.Contains("1") && ((Button)window.FindName("MergePasswordsButton")).IsEnabled);
            before = await File.ReadAllBytesAsync(vaultPath);
            Invoke((Button)window.FindName("MergePasswordsButton")); await Layout(window);
            Check("declined actual merge leaves vault unchanged", confirmation == "merge:1" && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(vaultPath)));
            allowMerge = true; await Layout(window); Invoke((Button)window.FindName("MergePasswordsButton")); await Layout(window); await model.WaitForIdleAsync(); await Layout(window);
            Check("actual merge button removes duplicate and retains association", (await vault.ReadAsync()).Count == 2 && await vault.GetAssociatedPasswordAsync(Path.Combine(data, "association.zip")) == item.Id);
            Check("merge refreshes list and disables action with no duplicates", model.PasswordRows.Count == 2 && model.DuplicateSummary == "" && !model.MergePasswordsCommand.CanExecute(null));
            for (var index = 0; index < 16; index++) await vault.SaveAsync(other with { Id = Guid.NewGuid(), Name = "合成列表记录 " + index, Secret = "synthetic " + index });
            model = new MainViewModel(baseDirectory, data, vault, dialogs); window.DataContext = model; await model.InitializeAsync(); model.Page = 1;
            var screenshots = Path.Combine(root, "artifacts/password-organization-preview"); Directory.CreateDirectory(screenshots);
            foreach (var contrast in new[] { false, true })
            {
                theme.Apply(contrast);
                foreach (var width in new[] { 1120, 800 })
                {
                    window.Width = width; window.Height = width == 800 ? 600 : 760; await Layout(window);
                    var table = (DataGrid)window.FindName("PasswordTable");
                    Check($"search group and merge fit width {width} contrast {contrast}", new[] { "PasswordSearch", "PasswordGroupBox", "MergePasswordsButton" }.All(name => Fits(window, (FrameworkElement)window.FindName(name))));
                    Check($"populated table remains compact width {width} contrast {contrast}", table.ActualHeight >= 90 && ((DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0)).ActualHeight == 48);
                    var summary = (TextBlock)window.FindName("DuplicateSummaryText");
                    Check($"summary does not overlay table width {width} contrast {contrast}", summary.TranslatePoint(new Point(), window).Y >= table.TranslatePoint(new Point(), window).Y + table.ActualHeight);
                    DesktopAccessibilityValidation.Render(window, Path.Combine(screenshots, $"vault-{width}-{contrast}.png"), 1.0);
                }
            }
        }
        catch (Exception exception) { Check("validation exception " + exception.GetType().Name, false); }
        finally { theme.Apply(false); }
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts/desktop-password-organization-validation.json"), JsonSerializer.Serialize(new { passed = checks.Count > 0 && checks.Values.All(value => value), total = checks.Count, checks, syntheticData = true }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Count > 0 && checks.Values.All(value => value);
    }
    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    private static bool Fits(Window window, FrameworkElement element)
    {
        var content = (FrameworkElement)window.Content; var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
        return element.IsVisible && bounds.Top >= 0 && bounds.Left >= 0 && bounds.Bottom <= content.ActualHeight + 1 && bounds.Right <= content.ActualWidth + 1;
    }
}
