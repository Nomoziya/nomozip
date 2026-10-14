using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopNotificationValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, ThemeResources theme, string root, string baseDirectory)
    {
        var data = Path.Combine(root, "artifacts/notification-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        var checks = new Dictionary<string, bool>();
        var taskbarAvailable = FindWindowW("Shell_TrayWnd", null) != IntPtr.Zero;
        var nativeRegistrationTested = false;
        var nativeRegistrationAttempted = false;
        var nativeShellUnavailable = false;
        NativeNotificationResult? nativeSdkProbe = null;
        void Check(string name, bool passed) => checks.Add(name, passed);
        try
        {
            foreach (var version in new[] { "26.03", "26.04" })
            {
                var folder = Path.Combine(data, version); Directory.CreateDirectory(folder);
                var oldSettings = JsonSerializer.Serialize(new { Version = 1, OutputPath = Path.Combine(folder, "old-output"), AutoTry = true, ConflictPolicy = 1, EngineVersion = version });
                await File.WriteAllTextAsync(Path.Combine(folder, "settings.json"), oldSettings);
                var vault = new DpapiPasswordVault(Path.Combine(folder, "passwords.apv"));
                var model = new MainViewModel(baseDirectory, folder, vault, new WindowDialogs(window));
                window.DataContext = model; await model.InitializeAsync();
                var notices = new List<int>(); var opens = new List<IReadOnlyList<string>>();
                model.ConfigureNotificationValidation(count => { notices.Add(count); return true; });
                model.ConfigureResultDirectoryValidation(paths => opens.Add(paths));
                model.SelectedEngineVersion = version; model.ApplyEngineCommand.Execute(null); await model.WaitForIdleAsync();
                Check(version + " old settings without notification field default off and remain intact", !model.NotifyExtractionSuccess && File.ReadAllText(Path.Combine(folder, "settings.json")) == oldSettings);
                model.Page = 2; await Layout(window);
                var checkboxPeer = new CheckBoxAutomationPeer(window.NotifyExtractionChoice);
                Check(version + " accessible checkbox name", checkboxPeer.GetName() == "整批解压成功后发送系统通知");
                ((IToggleProvider)checkboxPeer.GetPattern(PatternInterface.Toggle)!).Toggle();
                Invoke(window.SaveNotificationAction); await Layout(window); await model.WaitForIdleAsync(); await Layout(window);
                Check(version + " actual UI toggles and saves", model.NotifyExtractionSuccess && model.NotificationMessage == "已保存通知设置。");
                var restarted = new MainViewModel(baseDirectory, folder, vault, new WindowDialogs(window)); await restarted.InitializeAsync();
                Check(version + " setting survives restart", restarted.NotifyExtractionSuccess);
                model.Page = 0; model.OutputPath = Path.Combine(folder, "output"); model.ConflictPolicy = 1; model.QueueConcurrency = 2;
                var source = Path.Combine(root, "tests/fixtures/generated/plain.zip");
                var first = Path.Combine(folder, "private-name-first.zip"); var second = Path.Combine(folder, "private-name-second.zip");
                File.Copy(source, first); File.Copy(source, second); model.AddFiles([first, second]);
                foreach (var row in model.Tasks) row.ShellOutputPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(row.Path));
                model.ApplyDirectoryRules(model.DirectoryRules with { OpenAfterSuccess = true }, model.OutputPath);
                foreach (var row in model.Tasks) row.ShellOutputPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(row.Path));
                model.ExtractCommand.Execute(null); await model.WaitForIdleAsync(); await Layout(window);
                Check(version + " two real archives yield one batch notification", model.Tasks.All(row => row.Completed) && notices.SequenceEqual(new[] { 2 }));
                Check(version + " completion action preserves distinct result picker", opens.Count == 1 && opens[0].Count == 2 && opens[0].All(Directory.Exists));
                Check(version + " notification does not alter output or source", File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(source)) && model.Tasks.All(row => File.ReadAllBytes(Path.Combine(row.ResultOutputPath!, "中文示例.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/generated/中文示例.txt")))));
                Check(version + " empty pending batch cannot send again", !model.ExtractCommand.CanExecute(null));
                model.Tasks.Clear(); var good = Path.Combine(folder, "good.zip"); var missing = Path.Combine(folder, "missing.zip");
                File.Copy(source, good); File.Copy(source, missing); model.AddFiles([good, missing]); File.Delete(missing);
                foreach (var row in model.Tasks) row.ShellOutputPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(row.Path));
                model.ExtractCommand.Execute(null); await model.WaitForIdleAsync(); await Layout(window);
                Check(version + " partial failure sends no success notification", notices.Count == 1 && model.Tasks.Count(row => row.Completed) == 1 && model.Tasks.Any(row => row.Failure is not null));
                Check(version + " partial failure does not open result directories", opens.Count == 1);
                File.Copy(source, missing); model.SelectedTask = model.Tasks.Single(row => !row.Completed);
                model.ExtractSelectedTaskCommand.Execute(null); await model.WaitForIdleAsync(); await Layout(window);
                Check(version + " selected retry produces its own one-item notice", model.Tasks.All(row => row.Completed) && notices.SequenceEqual(new[] { 2, 1 }) && opens.Count == 2 && opens.Last().Count == 1);
                model.Tasks.Clear(); var cancelled = Path.Combine(folder, "cancelled.zip"); File.Copy(source, cancelled); model.AddFiles([cancelled]);
                model.ExtractCommand.Execute(null);
                Check(version + " notification save disabled while extraction is busy", model.IsBusy && !model.SaveNotificationSettingsCommand.CanExecute(null));
                model.CancelCommand.Execute(null); await model.WaitForIdleAsync(); await Layout(window);
                Check(version + " cancel sends no notification or result action", notices.Count == 2 && opens.Count == 2 && !model.Tasks[0].Completed && model.IsIdle);
                Check(version + " notification controls recover after cancellation", window.NotifyExtractionChoice.IsEnabled && model.SaveNotificationSettingsCommand.CanExecute(null));
                model.NotifyExtractionSuccess = false; model.ApplyDirectoryRules(model.DirectoryRules with { OpenAfterSuccess = false }, model.OutputPath);
                model.ExtractCommand.Execute(null); await model.WaitForIdleAsync(); await Layout(window);
                Check(version + " disabled notification does not send on success", model.Tasks[0].Completed && notices.Count == 2);
                model.NotifyExtractionSuccess = true;
                model.ConfigureNotificationValidation(_ => false);
                model.Tasks.Clear(); var unavailable = Path.Combine(folder, "unavailable.zip"); File.Copy(source, unavailable); model.AddFiles([unavailable]);
                model.ExtractCommand.Execute(null); await model.WaitForIdleAsync(); await Layout(window);
                Check(version + " unavailable Shell preserves successful result", model.Tasks[0].Completed && model.IsIdle && model.NotificationMessage.Contains("系统未接受通知"));
                model.Page = 2; await Layout(window);
                foreach (var contrast in new[] { false, true })
                foreach (var width in new[] { 1120.0, 800.0 })
                {
                    theme.Apply(contrast); window.Width = width; window.Height = 760; await Layout(window);
                    window.NotifyExtractionChoice.BringIntoView(); await Layout(window);
                    var bounds = window.NotifyExtractionChoice.TransformToAncestor(window).TransformBounds(new Rect(window.NotifyExtractionChoice.RenderSize));
                    Check(version + $" checkbox visible {contrast} {width}", bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Top >= 0 && bounds.Bottom <= window.ActualHeight && window.NotifyExtractionChoice.ActualWidth > 300);
                    var preview = Path.Combine(root, "artifacts/notification-preview"); Directory.CreateDirectory(preview);
                    // Render only the owned off-screen window; never show a native balloon during validation.
                    var opacity = window.Opacity; window.Opacity = 1;
                    DesktopAccessibilityValidation.Render(window, Path.Combine(preview, $"settings-{contrast}-{width}.png"), 1);
                    window.Opacity = opacity;
                }
                var openCount = opens.Count; model.Page = 2; window.WindowState = WindowState.Minimized;
                window.ReturnToExtractionResults(); await Layout(window);
                Check(version + " result activation restores extraction page without opening files", model.Page == 0 && window.WindowState == WindowState.Normal && opens.Count == openCount);
                model.NotifyExtractionSuccess = false; model.SaveNotificationSettingsCommand.Execute(null); await model.WaitForIdleAsync();
                var disabled = new MainViewModel(baseDirectory, folder, vault, new WindowDialogs(window)); await disabled.InitializeAsync();
                Check(version + " disabled setting survives restart", !disabled.NotifyExtractionSuccess);
                var unknownFolder = Path.Combine(folder, "future-settings"); Directory.CreateDirectory(unknownFolder);
                var unknownText = "{\"Version\":99,\"NotifyExtractionSuccess\":true}"; await File.WriteAllTextAsync(Path.Combine(unknownFolder, "settings.json"), unknownText);
                var unknown = new MainViewModel(baseDirectory, unknownFolder, vault, new WindowDialogs(window)); await unknown.InitializeAsync();
                unknown.NotifyExtractionSuccess = true; unknown.SaveNotificationSettingsCommand.Execute(null); await unknown.WaitForIdleAsync();
                Check(version + " notification save preserves unknown future settings", File.ReadAllText(Path.Combine(unknownFolder, "settings.json")) == unknownText && unknown.NotificationMessage.Contains("原设置文件保持不变"));
            }
            var calls = new List<(uint Operation, CompletionNotification.IconData Data)>(); var activations = 0;
            bool Capture(uint operation, ref CompletionNotification.IconData icon) { calls.Add((operation, icon)); return true; }
            using (var native = new CompletionNotification(window, () => activations++, Capture))
            {
                Check("native structure matches Windows 64-bit layout", Marshal.SizeOf<CompletionNotification.IconData>() == 976 && Marshal.OffsetOf<CompletionNotification.IconData>(nameof(CompletionNotification.IconData.Info)).ToInt32() == 304);
                Check("invalid count never registers icon", !native.Show(0) && calls.Count == 0);
                Check("single native registration and submit", native.Show(2) && calls.Select(call => call.Operation).SequenceEqual(new uint[] { 0, 1 }));
                var notice = calls.Last().Data;
                Check("registration never displays a balloon", (calls[0].Data.Flags & 0x10) == 0);
                Check("notification no sound quiet-time and no stale queue", notice.InfoFlags == 0x91 && notice.Flags == 0x50);
                Check("system text contains only generic count and action", notice.Info == CompletionNotification.Body(2) && !notice.Info.Contains("private-name") && !notice.Info.Contains(data) && notice.Title == "nomozip · 解压完成");
                native.HandleMessage(CompletionNotification.CallbackMessage, new IntPtr(99), new IntPtr(0x405));
                Check("foreign icon callback ignored", activations == 0 && calls.Count == 2);
                SendMessageW(new System.Windows.Interop.WindowInteropHelper(window).Handle, CompletionNotification.CallbackMessage, new IntPtr(1), new IntPtr(0x405));
                Check("actual HWND callback activates once and deletes icon", activations == 1 && calls.Last().Operation == 2);
                native.HandleMessage(CompletionNotification.CallbackMessage, new IntPtr(1), new IntPtr(0x405));
                Check("duplicate click cannot activate again", activations == 1);
                native.Show(1); native.HandleMessage(CompletionNotification.CallbackMessage, new IntPtr(1), new IntPtr(0x404));
                Check("expiry cleanup never activates", calls.Last().Operation == 2 && activations == 1);
                native.Show(1); var restartCalls = calls.Count;
                SendMessageW(new System.Windows.Interop.WindowInteropHelper(window).Handle, (int)RegisterWindowMessageW("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
                Check("Explorer restart discards stale icon without replay", calls.Count == restartCalls);
                native.Show(1); Check("fresh notification re-registers after Explorer restart", calls[^2].Operation == 0 && calls.Last().Operation == 1);
                native.HandleMessage(CompletionNotification.CallbackMessage, new IntPtr(1), new IntPtr(0x401));
                Check("keyboard icon activation returns to results", activations == 2 && calls.Last().Operation == 2);
                native.Show(1); native.Dispose(); var count = calls.Count;
                Check("dispose deletes active icon and rejects later sends", calls.Last().Operation == 2 && !native.Show(1) && calls.Count == count);
            }
            bool RejectAdd(uint operation, ref CompletionNotification.IconData _) => false;
            using (var rejected = new CompletionNotification(window, () => activations++, RejectAdd)) Check("Shell registration rejection returned", !rejected.Show(1));
            var rejectCalls = new List<uint>();
            bool RejectModify(uint operation, ref CompletionNotification.IconData _) { rejectCalls.Add(operation); return operation != 1; }
            using (var rejected = new CompletionNotification(window, () => activations++, RejectModify)) Check("Shell send rejection cleans up icon", !rejected.Show(1) && rejectCalls.SequenceEqual(new uint[] { 0, 1, 2 }));
            var closingWindow = new Window { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, Width = 300, Height = 300, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            closingWindow.Show(); var closingHandle = new System.Windows.Interop.WindowInteropHelper(closingWindow).Handle;
            var closingCalls = new List<(uint Operation, IntPtr Window)>();
            bool ClosingShell(uint operation, ref CompletionNotification.IconData icon) { closingCalls.Add((operation, icon.Window)); return true; }
            using (var closing = new CompletionNotification(closingWindow, () => activations++, ClosingShell))
            {
                closing.Show(1); closingWindow.Closed += (_, _) => closing.Dispose(); closingWindow.Close();
                Check("actual Window Closed cleanup retains original HWND", closingCalls.Last() == (2u, closingHandle) && closingHandle != IntPtr.Zero);
            }
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted")
            {
                using var real = new CompletionNotification(window, () => activations++);
                if (File.Exists(Path.Combine(baseDirectory,"NativeNotificationProbe.dll")))
                {
                    ProbeNotification(new System.Windows.Interop.WindowInteropHelper(window).Handle,out var result);
                    nativeSdkProbe = result;
                }
                nativeRegistrationAttempted = true;
                // The independent SDK probe uses a fresh Win32 window and stock icon.
                // Record environment unavailability only when both native calls reject
                // valid inputs with the same E_FAIL; this never establishes delivery.
                nativeShellUnavailable = nativeSdkProbe is { Size: 976, InfoOffset: 304,
                    ValidWindow: 1, ValidIcon: 1, Accepted: 0, FreshAccepted: 0,
                    Error: 0x80004005, FreshError: 0x80004005 };
                if (taskbarAvailable && !nativeShellUnavailable)
                {
                    nativeRegistrationTested = true;
                    Check("isolated Windows Shell accepts native icon without balloon", real.EnsureIcon());
                    real.RemoveIcon(); Check("isolated Windows Shell icon can register after cleanup", real.EnsureIcon());
                }
                else
                {
                    Check("unavailable native Windows Shell rejects icon without claiming delivery", !real.EnsureIcon());
                    real.RemoveIcon(); Check("unavailable native Windows Shell cleanup and retry remain safe", !real.EnsureIcon());
                }
            }
        }
        catch (Exception error) { checks["exception " + error] = false; }
        finally { theme.Apply(SystemParameters.HighContrast); }
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts/notification-ui-validation.json"), JsonSerializer.Serialize(new
        { passed = checks.Count(pair => pair.Value), total = checks.Count, cases = checks.Select(pair => new { name = pair.Key, passed = pair.Value }),
            syntheticData = true, actualVisibleNotificationDeliveryTested = false,
            taskbarAvailable, nativeSdkProbe, nativeShellRegistrationAttempted = nativeRegistrationAttempted,
            nativeShellUnavailable, nativeShellRegistrationOnIsolatedRunner = nativeRegistrationTested }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return checks.Count > 0 && checks.Values.All(value => value);
    }
    private static async Task Layout(Window window) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    private static void Invoke(System.Windows.Controls.Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr SendMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string className, string? title);
    [StructLayout(LayoutKind.Sequential)] private struct NativeNotificationResult { public uint Size,InfoOffset,Accepted,Error,ValidWindow,ValidIcon,FreshAccepted,FreshError; }
    [DllImport("NativeNotificationProbe.dll", ExactSpelling=true,CallingConvention=CallingConvention.Cdecl)] private static extern void ProbeNotification(IntPtr window,out NativeNotificationResult result);
}
