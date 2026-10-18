using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopCompressionValidation
{
    public static async Task<(bool Flow, bool Accessibility)> RunAsync(MainWindow window, MainViewModel model, string baseDirectory, string dataDirectory, string root)
    {
        model.Page = 3;
        model.Tasks.Add(new ArchiveTaskRow(Path.Combine(root, "tests/fixtures/generated/encrypted-header.7z")));
        model.AddCompressionSources([Path.Combine(root, "tests/fixtures/generated/中文示例.txt")]);
        model.CompressionOutput = Path.Combine(dataDirectory, "desktop-created.7z"); model.CompressionEncrypt = true;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var view = (CompressionView)window.FindName("CompressionPage");
        var format = (ComboBox)view.FindName("FormatBox");
        var output = (TextBox)view.FindName("ArchiveOutputBox");
        var button = (Button)view.FindName("CreateButton");
        var formatPeer = UIElementAutomationPeer.CreatePeerForElement(format)!;
        var outputPeer = UIElementAutomationPeer.CreatePeerForElement(output)!;
        var buttonPeer = UIElementAutomationPeer.CreatePeerForElement(button)!;
        var shortcut = window.InputBindings.OfType<KeyBinding>().Any(value => value.Key == Key.C && value.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && ReferenceEquals(value.Command, model.CreateArchiveCommand));
        var accessibility = formatPeer.GetName() == "压缩格式" && outputPeer.GetName() == UiText.Get("Redesign.SaveLocation")
            && new TextBoxAutomationPeer(view.ArchiveNameBox).GetName() == UiText.Get("Redesign.ArchiveName")
            && formatPeer.IsKeyboardFocusable() && outputPeer.IsKeyboardFocusable() && buttonPeer.IsKeyboardFocusable()
            && buttonPeer.GetPattern(PatternInterface.Invoke) is IInvokeProvider && shortcut && !model.ExtractCommand.CanExecute(null);
        if (buttonPeer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) return (false, accessibility);
        invoke.Invoke();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await model.WaitForIdleAsync();
        var client = new ArchiveEngineClient(Path.Combine(baseDirectory, "ArchiveEngineWorker.exe"), Path.Combine(baseDirectory, "engines/26.04/7z.dll"));
        var flow = File.Exists(model.CompressionOutput) && (await client.TestAsync(model.CompressionOutput, "测试密碼 ' $ & 空格")).IsSuccess
            && !(await client.ListAsync(model.CompressionOutput)).IsSuccess;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        model.Page = 0; model.Tasks.Clear(); model.OutputPath = Path.Combine(dataDirectory, "desktop-created-extracted");
        accessibility &= !model.CreateArchiveCommand.CanExecute(null);
        model.AddFiles([model.CompressionOutput]); model.ExtractCommand.Execute(null); await model.WaitForIdleAsync();
        flow &= model.Tasks.Single().Completed && File.ReadAllBytes(Path.Combine(model.OutputPath, "中文示例.txt"))
            .SequenceEqual(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/generated/中文示例.txt")));
        return (flow, accessibility);
    }
}
