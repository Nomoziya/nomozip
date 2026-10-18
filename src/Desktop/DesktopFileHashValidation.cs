using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopFileHashValidation
{
    internal static async Task<bool> RunAsync(MainWindow owner, ThemeResources theme, string root, string baseDirectory)
    {
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool passed) => checks[name] = passed;
        var data = Path.GetFullPath(Path.Combine(root, "artifacts/file-hash-ui", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(data);
        var abc = Path.Combine(data, "中文文件 abc.txt"); var empty = Path.Combine(data, "empty.txt"); var missing = Path.Combine(data, "missing.txt");
        var abcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        var emptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        await File.WriteAllTextAsync(abc, "abc", new UTF8Encoding(false)); await File.WriteAllBytesAsync(empty, []);
        FileHashDialog? dialog = null;
        var cancelParent = false;
        try
        {
            var main = new MainViewModel(baseDirectory, data, new DpapiPasswordVault(Path.Combine(data, "synthetic.apv")), new WindowDialogs(owner, prepareHashes: window =>
            {
                window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
                window.Closed += (_, _) => Check("settings hash window closed event observed", true);
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
                {
                    Check("actual settings button opens owned hash window with selected file", window.Owner == owner && window.Model.Rows.Single().Path == abc && window.Model.Rows.Single().Digest == "");
                    Check("parent remains busy until hash window closes", ((MainViewModel)owner.DataContext).IsBusy);
                    var close = (Button)window.FindName("HashCloseButton");
                    close.Click += (_, _) => Check("settings close click event observed", true);
                    Check("settings hash window is visible and close is enabled", window.IsVisible && close.IsEnabled);
                    if (cancelParent) ((MainViewModel)owner.DataContext).CancelCommand.Execute(null); else Invoke(close);
                });
            }));
            owner.DataContext = main; await main.InitializeAsync(); main.AddFiles([abc]); main.SelectedTask = main.Tasks.Single(); main.Page = 2; await Layout(owner);
            var mainCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsIdle) && main.IsIdle) mainCompleted.TrySetResult();
            };
            var entry = (Button)owner.FindName("OpenFileHashesButton"); entry.BringIntoView(); await Layout(owner); Invoke(entry); await mainCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10)); await main.WaitForIdleAsync(); await Layout(owner);
            Check("parent returns idle without changing archive completion", main.IsIdle && !main.Tasks.Single().Completed);
            mainCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); cancelParent = true;
            Invoke(entry); await mainCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10)); await main.WaitForIdleAsync(); await Layout(owner); cancelParent = false;
            Check("parent cancellation closes hash window and restores idle", main.IsIdle && !main.Tasks.Single().Completed);
            var calculator = new GateCalculator();
            string? input = null; string? output = null; var copied = "";
            dialog = new FileHashDialog(create: window => new FileHashViewModel(calculator, new FileHashDialogs(window, () => [abc, empty, abc], exporting => exporting ? output : input, text => copied = text)))
            { Owner = owner, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            dialog.Show(); await Layout(dialog); var model = dialog.Model;
            Check("hash test window is actually shown before interaction", dialog.IsVisible && dialog.IsLoaded && dialog.ActualWidth > 0);
            async Task Execute(string name)
            {
                await Layout(dialog); Invoke((Button)dialog.FindName(name)); await Layout(dialog); await model.WaitForIdleAsync(); await Layout(dialog);
            }
            Check("empty window disables calculate export and copy", !model.CalculateCommand.CanExecute(null) && !model.ExportCommand.CanExecute(null) && !model.CopyCommand.CanExecute(null));
            foreach (var name in new[] { "HashAddButton", "HashImportButton", "HashCalculateButton", "HashCancelButton", "HashCloseButton", "HashExpectedBox", "HashDigestBox" })
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement((UIElement)dialog.FindName(name))!;
                Check("hash control has accessible name " + name, peer.GetName().Length > 0);
            }
            await Execute("HashAddButton");
            Check("actual add button deduplicates files and selects first", model.Rows.Count == 2 && model.Selected?.Path == abc);
            var expected = (TextBox)dialog.FindName("HashExpectedBox"); expected.Text = "wrong format"; await Layout(dialog);
            Check("invalid pasted hash shows accessible inline error and blocks calculation", model.Selected!.ExpectedError.Length > 0 && ((TextBlock)dialog.FindName("HashExpectedError")).IsVisible && !model.CalculateCommand.CanExecute(null));
            expected.Text = ""; await Execute("HashCalculateButton");
            Check("actual calculation reads source bytes and shows computed without comparison", model.Rows[0].Digest == abcHash && model.Rows[1].Digest == emptyHash && model.Rows.All(value => value.Status == "已计算（未比较）"));
            Check("digest textbox is readonly and shows selected hash", ((TextBox)dialog.FindName("HashDigestBox")).IsReadOnly && ((TextBox)dialog.FindName("HashDigestBox")).Text == abcHash);
            expected.Text = " " + abcHash.ToUpperInvariant() + " "; await Layout(dialog);
            Check("uppercase pasted expectation compares case-insensitively", model.Rows[0].Status == "一致");
            expected.Text = emptyHash; await Layout(dialog);
            Check("wrong expectation visibly reports mismatch", model.Rows[0].Status == "不一致");
            await Execute("HashCopyButton");
            Check("copy command exports actual digest through clipboard boundary", copied == abcHash && model.Message.Contains("已复制"));
            var manifest = Path.Combine(data, "SHA256SUMS.txt");
            await File.WriteAllTextAsync(manifest, Sha256Manifest.Format([new(Path.GetFileName(abc), abcHash), new(Path.GetFileName(empty), abcHash), new(Path.GetFileName(missing), abcHash)]), new UTF8Encoding(false));
            input = manifest; await Execute("HashImportButton");
            Check("manifest import previews file paths and expectations without calculating added files", model.Rows.Count == 3 && model.Rows.Single(value => value.Path == missing).Digest == "" && model.Rows[0].ExpectedHash == abcHash);
            var original = model.Rows.Select(value => (value.Path, value.ExpectedHash, value.Digest)).ToArray();
            input = null; await Execute("HashImportButton");
            Check("cancel manifest picker preserves original rows", original.SequenceEqual(model.Rows.Select(value => (value.Path, value.ExpectedHash, value.Digest))));
            var broken = Path.Combine(data, "broken.txt"); await File.WriteAllTextAsync(broken, abcHash + " *good.txt\ninvalid second line"); input = broken;
            await Execute("HashImportButton");
            Check("malformed manifest reports failing line without partial import", model.Message.Contains("第 2 行") && original.SequenceEqual(model.Rows.Select(value => (value.Path, value.ExpectedHash, value.Digest))));
            await Execute("HashCalculateButton");
            Check("actual batch distinguishes match mismatch and unreadable file", model.Rows[0].Status == "一致" && model.Rows[1].Status == "不一致" && model.Rows[2].Status == "无法读取" && model.Rows[2].Digest == "");
            output = Path.Combine(data, "exported.txt"); await Execute("HashExportButton");
            var exported = await FileHashManifest.ReadAsync(output);
            Check("actual export writes computed rather than mismatched expected digest", exported.Count == 2 && exported.Single(value => value.FileName == "empty.txt").Digest == emptyHash && model.Message.Contains("2 项实际计算值"));
            var prior = await File.ReadAllBytesAsync(output); output = null; await Execute("HashExportButton");
            Check("cancel export picker does not rewrite old manifest", Enumerable.SequenceEqual(prior, await File.ReadAllBytesAsync(Path.Combine(data, "exported.txt"))));
            output = abc; await Execute("HashExportButton");
            Check("actual export cannot overwrite selected source", model.Message.Contains("不能覆盖") && await File.ReadAllTextAsync(abc) == "abc");
            output = missing; await Execute("HashExportButton");
            Check("export also protects unreadable source rows", model.Message.Contains("不能覆盖") && !File.Exists(missing));
            await Execute("HashRemoveButton");
            Check("actual remove button removes only selected row", model.Rows.Count == 2 && model.Rows.All(value => value.Path != abc));
            model.AddFiles([abc]);
            for (var index = 0; index < 12; index++) model.AddFiles([Path.Combine(data, "合成列表文件 " + index + ".bin")]);
            var screenshots = Path.Combine(root, "artifacts/file-hash-preview"); Directory.CreateDirectory(screenshots);
            foreach (var contrast in new[] { false, true })
            {
                theme.Apply(contrast);
                foreach (var dimensions in new[] { (820, 680), (600, 500) })
                {
                    dialog.Width = dimensions.Item1; dialog.Height = dimensions.Item2; await Layout(dialog);
                    Check($"all primary actions fit {dimensions} contrast {contrast}", new[] { "HashAddButton", "HashExpectedBox", "HashCalculateButton", "HashExportButton", "HashCloseButton" }.All(name => Fits(dialog, (FrameworkElement)dialog.FindName(name))));
                    var table = (DataGrid)dialog.FindName("HashTable"); table.ScrollIntoView(table.Items[0]); await Layout(dialog); var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
                    Check($"first full row remains visible {dimensions} contrast {contrast}", table.ActualHeight >= 84 && row.ActualHeight == 40 && row.TranslatePoint(new Point(0, row.ActualHeight), table).Y <= table.ActualHeight);
                    DesktopAccessibilityValidation.Render(dialog, Path.Combine(screenshots, $"hash-{dimensions.Item1}-{contrast}.png"), 1.0);
                }
            }
            calculator.Block = true; await Layout(dialog); Invoke((Button)dialog.FindName("HashCalculateButton")); await calculator.Started.Task; await Layout(dialog);
            Check("calculation blocks changes while cancellation stays reachable", model.IsBusy && !model.ImportCommand.CanExecute(null) && !((TextBox)dialog.FindName("HashExpectedBox")).IsEnabled && ((Button)dialog.FindName("HashCancelButton")).IsEnabled);
            Invoke((Button)dialog.FindName("HashCancelButton")); await model.WaitForIdleAsync(); await Layout(dialog);
            Check("actual cancel action clears unfinished digests and restores idle", model.IsIdle && model.Rows[0].Status == "已取消" && model.Rows.All(value => value.Digest == "") && !model.ExportCommand.CanExecute(null) && model.Message.Contains("已取消"));
            calculator.Started = new TaskCompletionSource(); await Layout(dialog); Invoke((Button)dialog.FindName("HashCalculateButton")); await calculator.Started.Task;
            Invoke((Button)dialog.FindName("HashCloseButton")); await model.WaitForIdleAsync(); await Layout(dialog);
            Check("closing busy window cancels and awaits operation", !dialog.IsVisible && model.IsIdle && model.Rows[0].Digest == "");
        }
        catch (Exception error) { Check("validation exception " + error.ToString(), false); }
        finally { if (dialog is { IsVisible: true }) { dialog.Model.Cancel(); await dialog.Model.WaitForIdleAsync(); dialog.Close(); } theme.Apply(false); }
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts/file-hash-ui-validation.json"), JsonSerializer.Serialize(new { passed = checks.Count > 0 && checks.Values.All(value => value), total = checks.Count, checks, syntheticFiles = true, clipboardStub = true }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Count > 0 && checks.Values.All(value => value);
    }
    private sealed class GateCalculator : IFileHashCalculator
    {
        public bool Block { get; set; }
        public TaskCompletionSource Started { get; set; } = new();
        public async Task<FileHashResult> ComputeAsync(string path, IProgress<FileHashProgress>? progress = null, CancellationToken token = default)
        {
            if (Block) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return await new FileHashCalculator().ComputeAsync(path, progress, token);
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    private static bool Fits(Window window, FrameworkElement element)
    {
        var content = (FrameworkElement)window.Content; var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
        return element.IsVisible && bounds.Top >= 0 && bounds.Left >= 0 && bounds.Bottom <= content.ActualHeight + 1 && bounds.Right <= content.ActualWidth + 1;
    }
}
