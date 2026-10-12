using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArchivePassword.Infrastructure;
using ArchivePassword.Core;
using System.Text.Json;

namespace ArchivePassword.Desktop;

public partial class App : Application
{
    private ThemeResources? theme;
    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.BeginInvoke(() => theme?.Apply(SystemParameters.HighContrast));
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        base.OnExit(e);
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        theme = new ThemeResources(Resources);
        theme.Apply(SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        var baseDirectory = AppContext.BaseDirectory;
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArchivePassword");
        var previewIndex = Array.IndexOf(e.Args, "--preview");
        var validationIndex = Array.IndexOf(e.Args, "--validate");
        var onlineValidationIndex = Array.IndexOf(e.Args, "--validate-online");
        var installedValidationIndex = Array.IndexOf(e.Args, "--validate-installed");
        if (validationIndex >= 0)
        {
            var reportPath = Path.Combine(Path.GetFullPath(e.Args[validationIndex + 1]), "artifacts/desktop-validation.json");
            File.WriteAllText(reportPath, "{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException += (_, failure) =>
            {
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = false, exceptionType = failure.Exception.GetType().Name, stack = failure.Exception.StackTrace }));
                failure.Handled = true; Shutdown(1);
            };
        }
        var dataIndex = Array.IndexOf(e.Args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < e.Args.Length) dataDirectory = Path.GetFullPath(e.Args[dataIndex + 1]);
        if (validationIndex >= 0) dataDirectory = Path.Combine(Path.GetFullPath(e.Args[validationIndex + 1]), "artifacts", "desktop-validation", Guid.NewGuid().ToString("N"));
        if (onlineValidationIndex >= 0) dataDirectory = Path.Combine(Path.GetFullPath(e.Args[onlineValidationIndex + 1]), "artifacts", "online-validation", Guid.NewGuid().ToString("N"));
        if (installedValidationIndex >= 0)
        {
            var validationRoot = Path.GetFullPath(e.Args[installedValidationIndex + 1]);
            if (dataIndex < 0) dataDirectory = Path.Combine(validationRoot, "artifacts", "installed-validation", Guid.NewGuid().ToString("N"));
            else if (!dataDirectory.StartsWith(Path.Combine(validationRoot, "artifacts", "installer-validation") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { Shutdown(2); return; }
        }
        var vault = new DpapiPasswordVault(Path.Combine(dataDirectory, "passwords.apv"));
        var window = new MainWindow();
        var model = new MainViewModel(baseDirectory, dataDirectory, vault, new WindowDialogs(window, validationIndex >= 0 ? PrepareValidationDialog : null,
            validationIndex >= 0 ? () => Path.Combine(Path.GetFullPath(e.Args[validationIndex + 1]), "artifacts/update-test-assets/ArchivePassword-engine-26.03-win-x64.zip") : null,
            validationIndex >= 0 || onlineValidationIndex >= 0 ? (_, _) => true : null));
        window.DataContext = model;
        MainWindow = window;
        if (installedValidationIndex >= 0)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var result = await DesktopInstalledValidation.RunAsync(model, vault, baseDirectory, Path.GetFullPath(e.Args[installedValidationIndex + 1]), e.Args.Contains("--shared-runtime"));
            Shutdown(result ? 0 : 1); return;
        }
        if (onlineValidationIndex >= 0)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var result = await DesktopOnlineValidation.RunAsync(window, model, vault, baseDirectory, dataDirectory, Path.GetFullPath(e.Args[onlineValidationIndex + 1]));
            Shutdown(result ? 0 : 1); return;
        }
        if (validationIndex >= 0)
        {
            var validationRoot = Path.GetFullPath(e.Args[validationIndex + 1]);
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            Directory.CreateDirectory(dataDirectory);
            try
            {
                var fixture = Path.Combine(validationRoot, "tests/fixtures/generated/encrypted-header.7z");
                await vault.SaveAsync(new SavedPassword(Guid.NewGuid(), "synthetic desktop sample", "测试密碼 ' $ & 空格", "", true, DateTimeOffset.UtcNow));
                await model.InitializeAsync();
                model.OutputPath = Path.Combine(dataDirectory, "output");
                model.AddFiles([fixture]);
                model.ExtractCommand.Execute(null);
                await model.WaitForIdleAsync();
                var extracted = Path.Combine(model.OutputPath, "中文示例.txt");
                var expected = Path.Combine(validationRoot, "tests/fixtures/generated/中文示例.txt");
                var success = model.Tasks.Single().Completed && File.Exists(extracted) && File.ReadAllBytes(extracted).SequenceEqual(File.ReadAllBytes(expected));
                foreach (var saved in await vault.ReadAsync()) await vault.DeleteAsync(saved.Id);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                model.Tasks.Clear(); model.OutputPath = Path.Combine(dataDirectory, "manual-output"); model.AddFiles([fixture]);
                model.ExtractCommand.Execute(null); await model.WaitForIdleAsync();
                var manualFile = Path.Combine(model.OutputPath, "中文示例.txt");
                var manualSuccess = model.Tasks.Single().Completed && File.Exists(manualFile) && File.ReadAllBytes(manualFile).SequenceEqual(File.ReadAllBytes(expected)) && (await vault.ReadAsync()).Any(value => value.Secret == "测试密碼 ' $ & 空格");
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var vaultBeforeUpdate = await File.ReadAllBytesAsync(Path.Combine(dataDirectory, "passwords.apv"));
                model.ImportUpdateCommand.Execute(null); await model.WaitForIdleAsync();
                var updateSuccess = model.EngineSummary.Contains("26.03") && Enumerable.SequenceEqual(vaultBeforeUpdate, await File.ReadAllBytesAsync(Path.Combine(dataDirectory, "passwords.apv")));
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                model.Tasks.Clear(); model.OutputPath = Path.Combine(dataDirectory, "updated-engine-output"); model.AddFiles([fixture]);
                model.ExtractCommand.Execute(null); await model.WaitForIdleAsync();
                updateSuccess &= model.Tasks.Single().Completed && File.ReadAllBytes(Path.Combine(model.OutputPath, "中文示例.txt")).SequenceEqual(File.ReadAllBytes(expected));
                model.RollbackEngineCommand.Execute(null); await model.WaitForIdleAsync();
                updateSuccess &= model.EngineSummary.Contains("26.04");
                var restartedModel = new MainViewModel(baseDirectory, dataDirectory, vault, new WindowDialogs(window));
                await restartedModel.InitializeAsync(); updateSuccess &= restartedModel.EngineSummary.Contains("26.04");
                var compression = await DesktopCompressionValidation.RunAsync(window, model, baseDirectory, dataDirectory, validationRoot);
                var browser = await DesktopBrowserValidation.RunAsync(window, baseDirectory, dataDirectory, validationRoot);
                var failureDetails = await DesktopFailureValidation.RunAsync(window, baseDirectory, dataDirectory, validationRoot);
                var accessibility = await DesktopAccessibilityValidation.RunAsync(window, model, theme, validationRoot);
                var passed = success && manualSuccess && updateSuccess && compression.Flow && compression.Accessibility && browser && failureDetails && accessibility;
                await File.WriteAllTextAsync(Path.Combine(validationRoot, "artifacts/desktop-validation.json"), JsonSerializer.Serialize(new { passed, automaticFlowPassed = success, passwordDialogAndSavePassed = manualSuccess, engineUpdateAndRollbackPassed = updateSuccess,
                    compressionAndPasswordReusePassed = compression.Flow, compressionAutomationLabelsAndShortcutPassed = compression.Accessibility,
                    archiveBrowserPassed = browser, extractionFailureDetailsPassed = failureDetails, accessibilityPassed = accessibility, engines = model.EngineVersions.ToArray() }));
                Shutdown(passed ? 0 : 1);
            }
            catch
            {
                await File.WriteAllTextAsync(Path.Combine(validationRoot, "artifacts/desktop-validation.json"), "{\"passed\":false,\"error\":\"desktop_validation_failed\"}");
                Shutdown(1);
            }
            return;
        }
        if (previewIndex >= 0 && previewIndex + 1 < e.Args.Length)
        {
            model.LoadPreview();
            var pageIndex = Array.IndexOf(e.Args, "--preview-page");
            if (pageIndex >= 0 && pageIndex + 1 < e.Args.Length) model.Page = int.Parse(e.Args[pageIndex + 1]);
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.ShowInTaskbar = false; window.ShowActivated = false; window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
            var widthIndex = Array.IndexOf(e.Args, "--preview-width");
            var previewWidth = widthIndex >= 0 && widthIndex + 1 < e.Args.Length ? int.Parse(e.Args[widthIndex + 1]) : 1120;
            window.Width = previewWidth; window.Height = 760;
            window.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var content = (FrameworkElement)window.Content;
            content.UpdateLayout();
            var target = new RenderTargetBitmap(previewWidth, 760, 96, 96, PixelFormats.Pbgra32);
            target.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = File.Create(e.Args[previewIndex + 1]); encoder.Save(stream);
            Shutdown(); return;
        }
        WindowPlacement.FitWorkArea(window, SystemParameters.WorkArea);
        window.Show();
        _ = model.InitializeAsync();
        foreach (var argument in e.Args.Where(value => File.Exists(value))) model.AddFiles([argument]);
    }

    private static void PrepareValidationDialog(PasswordDialog dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000;
        dialog.ShowActivated = false;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
        {
            ((System.Windows.Controls.PasswordBox)dialog.FindName("SecretBox")).Password = "测试密碼 ' $ & 空格";
            ((System.Windows.Controls.CheckBox)dialog.FindName("SavePassword")).IsChecked = true;
            ((System.Windows.Controls.Button)dialog.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        });
    }
}
