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
    private ShellCommandServer? shellServer;
    public App()
    {
        // Configure before InitializeComponent loads application-level XAML resources.
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var redesign = Array.IndexOf(arguments, "--validate-redesign");
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && redesign >= 0 && redesign + 2 < arguments.Length)
        {
            // A template failure can occur before OnStartup reaches the test
            // runner. Preserve that actual failure instead of leaving no report.
            var reportFolder = Path.Combine(Path.GetFullPath(arguments[redesign + 1]), "artifacts/ui-redesign", arguments[redesign + 2] == "en" ? "en" : "zh-CN");
            Directory.CreateDirectory(reportFolder);
            var report = Path.Combine(reportFolder, "report.json");
            File.WriteAllText(report, "{\"passed\":false,\"state\":\"starting\"}");
            AppDomain.CurrentDomain.UnhandledException += (_, failure) => File.WriteAllText(report, JsonSerializer.Serialize(new {passed=false,error=failure.ExceptionObject.ToString()}));
            DispatcherUnhandledException += (_, failure) =>
            {
                File.WriteAllText(report, JsonSerializer.Serialize(new {passed=false,error=failure.Exception.ToString()}));
                failure.Handled=true;Shutdown(1);
            };
        }
        var localeIndex = Array.IndexOf(arguments, "--validate-localization");
        if (localeIndex < 0) localeIndex = Array.IndexOf(arguments, "--validate-language-restart");
        if (localeIndex < 0) localeIndex = Array.IndexOf(arguments, "--validate-theme");
        if (localeIndex < 0) localeIndex = Array.IndexOf(arguments, "--validate-visual-refresh");
        if (localeIndex < 0) localeIndex = Array.IndexOf(arguments, "--validate-redesign");
        if (localeIndex < 0) localeIndex = Array.IndexOf(arguments, "--validate-theme-restart");
        if (localeIndex < 0) localeIndex = Array.IndexOf(arguments, "--validate-application-updates");
        var isolated = arguments.Any(value => value.StartsWith("--validate", StringComparison.Ordinal) || value == "--preview");
        var directoryIndex = Array.IndexOf(arguments, "--data-dir");
        var directory = directoryIndex >= 0 && directoryIndex + 1 < arguments.Length ? Path.GetFullPath(arguments[directoryIndex + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArchivePassword");
        var language = isolated ? "zh-CN" : LanguagePreferenceStore.Read(directory).Language;
        if (localeIndex >= 0 && localeIndex + 2 < arguments.Length)
        {
            var requested = arguments[localeIndex + 2];
            var scope = Path.Combine(Path.GetFullPath(arguments[localeIndex + 1]), "artifacts") + Path.DirectorySeparatorChar;
            language = requested == "stored" && directory.StartsWith(scope, StringComparison.OrdinalIgnoreCase)
                ? LanguagePreferenceStore.Read(directory).Language : requested;
        }
        UiText.Configure(language);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag)));
    }
    internal void ApplyAppearanceMode(string mode) => theme?.SetMode(mode);
    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.BeginInvoke(() => theme?.Apply(SystemParameters.HighContrast));
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        shellServer?.Dispose();
        PreviewImageFiles.CleanupCurrent();
        base.OnExit(e);
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        var languageValidationIndex = Array.IndexOf(e.Args, "--validate-localization");
        var languageRestartIndex = Array.IndexOf(e.Args, "--validate-language-restart");
        base.OnStartup(e);
        WindowPlacement.ValidationMode = e.Args.Any(value => value.StartsWith("--validate", StringComparison.Ordinal) || value == "--preview");
        theme = new ThemeResources(Resources);
        theme.Apply(SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        var baseDirectory = AppContext.BaseDirectory;
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArchivePassword");
        if (WindowPlacement.ValidationMode) dataDirectory = Path.Combine(baseDirectory, "validation-startup", Guid.NewGuid().ToString("N"));
        var previewIndex = Array.IndexOf(e.Args, "--preview");
        var validationIndex = Array.IndexOf(e.Args, "--validate");
        var onlineValidationIndex = Array.IndexOf(e.Args, "--validate-online");
        var installedValidationIndex = Array.IndexOf(e.Args, "--validate-installed");
        var accessibilityValidationIndex = Array.IndexOf(e.Args, "--validate-accessibility");
        var vaultTransferValidationIndex = Array.IndexOf(e.Args, "--validate-vault-transfer");
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
        if (accessibilityValidationIndex >= 0) dataDirectory = Path.Combine(Path.GetFullPath(e.Args[accessibilityValidationIndex + 1]), "artifacts", "accessibility-validation", Guid.NewGuid().ToString("N"));
        if (vaultTransferValidationIndex >= 0) dataDirectory = Path.Combine(Path.GetFullPath(e.Args[vaultTransferValidationIndex + 1]), "artifacts", "vault-transfer-desktop", Guid.NewGuid().ToString("N"));
        if (installedValidationIndex >= 0)
        {
            var validationRoot = Path.GetFullPath(e.Args[installedValidationIndex + 1]);
            if (dataIndex < 0) dataDirectory = Path.Combine(validationRoot, "artifacts", "installed-validation", Guid.NewGuid().ToString("N"));
            else if (!dataDirectory.StartsWith(Path.Combine(validationRoot, "artifacts", "installer-validation") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { Shutdown(2); return; }
        }
        var themeValidationIndex = Array.IndexOf(e.Args, "--validate-theme");
        var themeRestartIndex = Array.IndexOf(e.Args, "--validate-theme-restart");
        if (!WindowPlacement.ValidationMode) theme.SetMode(AppearancePreferenceStore.Read(dataDirectory).Mode);
        else if (themeValidationIndex >= 0 || themeRestartIndex >= 0)
        {
            var index = themeValidationIndex >= 0 ? themeValidationIndex : themeRestartIndex;
            var scope = Path.Combine(Path.GetFullPath(e.Args[index + 1]), "artifacts") + Path.DirectorySeparatorChar;
            if (!dataDirectory.StartsWith(scope, StringComparison.OrdinalIgnoreCase)) { Shutdown(2); return; }
            theme.SetMode(AppearancePreferenceStore.Read(dataDirectory).Mode);
        }
        if (e.Args.Contains("--shell-server"))
        {
            var testIndex = Array.IndexOf(e.Args, "--validate-shell-server");
            var testRoot = testIndex >= 0 ? Path.GetFullPath(e.Args[testIndex + 1]) : null;
            if (testRoot is not null) dataDirectory = Path.Combine(testRoot, "artifacts/shell-desktop", Guid.NewGuid().ToString("N"));
            var shellDataDirectory = dataDirectory;
            shellServer = new ShellCommandServer(Dispatcher, async selection =>
            {
                var shellWindow = new MainWindow();
                var shellVault = new DpapiPasswordVault(Path.Combine(shellDataDirectory, "passwords.apv"));
                var shellModel = new MainViewModel(baseDirectory, shellDataDirectory, shellVault, new WindowDialogs(shellWindow));
                shellWindow.DataContext = shellModel; MainWindow = shellWindow;
                if (testRoot is not null)
                {
                    shellWindow.WindowStartupLocation = WindowStartupLocation.Manual; shellWindow.Left = -32000; shellWindow.Top = -32000;
                    shellWindow.ShowActivated = false; shellWindow.ShowInTaskbar = false;
                }
                shellWindow.Show();
                if (testRoot is not null) await shellVault.SaveAsync(new SavedPassword(Guid.NewGuid(), "synthetic shell acceptance", "测试密碼 ' $ & 空格", "", true, DateTimeOffset.UtcNow));
                await shellModel.InitializeAsync();
                await shellModel.ApplyShellSelectionAsync(selection);
                if (testRoot is not null)
                {
                    await Dispatcher.InvokeAsync(shellWindow.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    DesktopAccessibilityValidation.Render(shellWindow, Path.Combine(testRoot, "artifacts/shell-view-" + selection.Action + ".png"), 1.0);
                    await File.WriteAllTextAsync(Path.Combine(testRoot, "artifacts/shell-server-" + selection.Action + "-validation.json"), JsonSerializer.Serialize(new
                    { passed = selection.Action == ShellAction.Compress ? shellModel.Page == 3 && shellModel.CompressionSources.Count == selection.Paths.Count && !shellModel.CompressionRunning
                        : shellModel.Tasks.Count == selection.Paths.Count && shellModel.Tasks.All(task => task.Completed),
                        action = selection.Action.ToString(), selected = selection.Paths.Count, tasks = shellModel.Tasks.Select(task => new { task.Path, output = task.ShellOutputPath, task.Completed }),
                        compressionSources = shellModel.CompressionSources.Select(source => source.Path), compressionOutput = shellModel.CompressionOutput, syntheticData = true }));
                    Shutdown();
                }
            }, () => Shutdown());
            if (testRoot is not null) await File.WriteAllTextAsync(Path.Combine(testRoot, "artifacts/shell-server-ready-" + Environment.ProcessId + ".json"), "{\"ready\":true}");
            return;
        }
        var vault = new DpapiPasswordVault(Path.Combine(dataDirectory, "passwords.apv"));
        var window = new MainWindow();
        if (themeValidationIndex >= 0 || themeRestartIndex >= 0)
        {
            var index = themeValidationIndex >= 0 ? themeValidationIndex : themeRestartIndex;
            var root = Path.GetFullPath(e.Args[index + 1]);
            window.SuppressExitConfirmationForValidation = true;
            if (themeRestartIndex >= 0)
            {
                var passed = AppearancePreferenceStore.Read(dataDirectory).Mode == theme.Mode && theme.Mode == "dark";
                await File.WriteAllTextAsync(Path.Combine(dataDirectory,"theme-restart-result.json"),JsonSerializer.Serialize(new{passed,mode=theme.Mode,dark=theme.IsDark,highContrast=theme.IsHighContrast}));
                Shutdown(passed?0:1);return;
            }
            var report=Path.Combine(root,"artifacts/theme-"+UiText.LanguageCode+"-settings-validation.json");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,error=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var success=await DesktopThemeValidation.RunAsync(window,theme,root,baseDirectory,dataDirectory);Shutdown(success?0:1);return;
        }
        if (languageValidationIndex >= 0 || languageRestartIndex >= 0)
        {
            var index = languageValidationIndex >= 0 ? languageValidationIndex : languageRestartIndex;
            var root = Path.GetFullPath(e.Args[index + 1]);
            window.SuppressExitConfirmationForValidation = true;
            if (languageRestartIndex >= 0)
            {
                var passed = LanguagePreferenceStore.Read(dataDirectory).Language == UiText.LanguageCode;
                await File.WriteAllTextAsync(Path.Combine(dataDirectory,"restart-result.json"), JsonSerializer.Serialize(new { passed, culture = UiText.LanguageCode }));
                Shutdown(passed ? 0 : 1); return;
            }
            var report = Path.Combine(root,"artifacts/localization-"+UiText.LanguageCode+"-ui-validation.json");
            DispatcherUnhandledException += (_,failure) => { File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,error=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1); };
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var success=await DesktopLocalizationValidation.RunAsync(window,theme!,root,baseDirectory,dataDirectory);
            Shutdown(success?0:1);return;
        }
        var model = new MainViewModel(baseDirectory, dataDirectory, vault, new WindowDialogs(window, validationIndex >= 0 ? PrepareValidationDialog : null,
            validationIndex >= 0 ? () => Path.Combine(Path.GetFullPath(e.Args[validationIndex + 1]), $"artifacts/update-test-assets/ArchivePassword-engine-26.03-win-{EngineArchitecture.Current}.zip") : null,
            validationIndex >= 0 || onlineValidationIndex >= 0 ? (_, _) => true : null));
        window.DataContext = model;
        MainWindow = window;
        var redesignIndex=Array.IndexOf(e.Args,"--validate-redesign");
        if(redesignIndex>=0)
        {
            if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true") {Shutdown(2);return;}
            var root=Path.GetFullPath(e.Args[redesignIndex+1]);var reports=Path.Combine(root,"artifacts/ui-redesign",UiText.LanguageCode);Directory.CreateDirectory(reports);
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(Path.Combine(reports,"report.json"),JsonSerializer.Serialize(new{passed=false,error=failure.Exception.ToString()}));File.WriteAllText(Path.Combine(reports,"dispatcher-error.json"),JsonSerializer.Serialize(new{error=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.SuppressExitConfirmationForValidation=true;window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopRedesignValidation.RunAsync(window,model,theme,root);Shutdown(passed?0:1);return;
        }
        var formatsIndex=Array.IndexOf(e.Args,"--validate-formats");
        if(formatsIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;var report=Path.Combine(Path.GetFullPath(e.Args[formatsIndex+1]),"artifacts/format-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopFormatValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[formatsIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var sfxIndex=Array.IndexOf(e.Args,"--validate-sfx");
        if(sfxIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;var report=Path.Combine(Path.GetFullPath(e.Args[sfxIndex+1]),"artifacts/sfx-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopSfxValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[sfxIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var volumesIndex=Array.IndexOf(e.Args,"--validate-volumes");
        if(volumesIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[volumesIndex+1]),"artifacts/volume-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopVolumeValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[volumesIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var checkpointIndex=Array.IndexOf(e.Args,"--validate-checkpoints");
        if(checkpointIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var checkpointRoot=Path.GetFullPath(e.Args[checkpointIndex+1]);
            var report=Path.Combine(checkpointRoot,"artifacts/checkpoint-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopCheckpointValidation.RunAsync(window,theme!,checkpointRoot,baseDirectory);Shutdown(passed?0:1);return;
        }
        var dragIndex=Array.IndexOf(e.Args,"--validate-drag-out");
        if(dragIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[dragIndex+1]),"artifacts/drag-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopDragValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[dragIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var contentPreviewIndex=Array.IndexOf(e.Args,"--validate-content-preview");
        if(contentPreviewIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[contentPreviewIndex+1]),"artifacts/preview-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopPreviewValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[contentPreviewIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var conversionIndex=Array.IndexOf(e.Args,"--validate-conversion");
        if(conversionIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[conversionIndex+1]),"artifacts/conversion-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopConversionValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[conversionIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var editingIndex=Array.IndexOf(e.Args,"--validate-archive-editing");
        if(editingIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[editingIndex+1]),"artifacts/archive-editing-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopArchiveEditingValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[editingIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var masterIndex=Array.IndexOf(e.Args,"--validate-master-vault");
        if(masterIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[masterIndex+1]),"artifacts/master-vault-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,exceptionType=failure.Exception.GetType().Name,stack=failure.Exception.StackTrace}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopMasterVaultValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[masterIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var notificationIndex=Array.IndexOf(e.Args,"--validate-notifications");
        if(notificationIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[notificationIndex+1]),"artifacts/notification-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new {passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopNotificationValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[notificationIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var pauseIndex=Array.IndexOf(e.Args,"--validate-pause");
        if(pauseIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[pauseIndex+1]),"artifacts/pause-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new {passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopPauseValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[pauseIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var concurrencyIndex=Array.IndexOf(e.Args,"--validate-concurrency");
        if(concurrencyIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[concurrencyIndex+1]),"artifacts/concurrency-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new {passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopConcurrencyValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[concurrencyIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var queueIndex=Array.IndexOf(e.Args,"--validate-queue");
        if(queueIndex>=0)
        {
            window.SuppressExitConfirmationForValidation=true;
            var report=Path.Combine(Path.GetFullPath(e.Args[queueIndex+1]),"artifacts/queue-ui-validation.json");File.WriteAllText(report,"{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException+=(_,failure)=>{File.WriteAllText(report,JsonSerializer.Serialize(new {passed=false,exception=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1);};
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopQueueValidation.RunAsync(window,theme!,Path.GetFullPath(e.Args[queueIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var dpiIndex = Array.IndexOf(e.Args, "--validate-dpi");
        if (dpiIndex >= 0)
        {
            window.SuppressExitConfirmationForValidation = true;
            var dpiReport = Path.Combine(Path.GetFullPath(e.Args[dpiIndex + 1]), "artifacts/dpi-ui-validation.json");
            File.WriteAllText(dpiReport, "{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException += (_, failure) =>
            {
                File.WriteAllText(dpiReport, JsonSerializer.Serialize(new { passed = false, exceptionType = failure.Exception.GetType().Name, message = failure.Exception.Message, stack = failure.Exception.StackTrace }));
                failure.Handled = true; Shutdown(1);
            };
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000; window.Opacity = 0; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var passed = await DesktopDpiValidation.RunAsync(window, Path.GetFullPath(e.Args[dpiIndex + 1]), baseDirectory); Shutdown(passed ? 0 : 1); return;
        }
        var originIndex = Array.IndexOf(e.Args, "--validate-origin");
        if (originIndex >= 0)
        {
            window.SuppressExitConfirmationForValidation = true;
            var originReport = Path.Combine(Path.GetFullPath(e.Args[originIndex + 1]), "artifacts/origin-ui-validation.json");
            File.WriteAllText(originReport, "{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException += (_, failure) =>
            {
                File.WriteAllText(originReport, JsonSerializer.Serialize(new { passed = false, exceptionType = failure.Exception.GetType().Name, message = failure.Exception.Message, stack = failure.Exception.StackTrace }));
                failure.Handled = true; Shutdown(1);
            };
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var passed = await DesktopOriginValidation.RunAsync(window, Path.GetFullPath(e.Args[originIndex + 1]), baseDirectory); Shutdown(passed ? 0 : 1); return;
        }
        var encodingIndex = Array.IndexOf(e.Args, "--validate-encoding");
        if (encodingIndex >= 0)
        {
            window.SuppressExitConfirmationForValidation = true;
            var encodingReport = Path.Combine(Path.GetFullPath(e.Args[encodingIndex + 1]), "artifacts/encoding-ui-validation.json");
            File.WriteAllText(encodingReport, "{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException += (_, failure) =>
            {
                File.WriteAllText(encodingReport, JsonSerializer.Serialize(new { passed = false, exceptionType = failure.Exception.GetType().Name, message = failure.Exception.Message, stack = failure.Exception.StackTrace }));
                failure.Handled = true; Shutdown(1);
            };
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var passed = await DesktopEncodingValidation.RunAsync(window, theme, Path.GetFullPath(e.Args[encodingIndex + 1]), baseDirectory); Shutdown(passed ? 0 : 1); return;
        }
        var fileHashIndex = Array.IndexOf(e.Args, "--validate-file-hashes");
        if (fileHashIndex >= 0)
        {
            window.SuppressExitConfirmationForValidation = true;
            var hashReport = Path.Combine(Path.GetFullPath(e.Args[fileHashIndex + 1]), "artifacts/file-hash-ui-validation.json");
            File.WriteAllText(hashReport, "{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException += (_, failure) =>
            {
                File.WriteAllText(hashReport, JsonSerializer.Serialize(new { passed = false, exceptionType = failure.Exception.GetType().Name, message = failure.Exception.Message, stack = failure.Exception.StackTrace }));
                failure.Handled = true; Shutdown(1);
            };
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var passed = await DesktopFileHashValidation.RunAsync(window, theme, Path.GetFullPath(e.Args[fileHashIndex + 1]), baseDirectory); Shutdown(passed ? 0 : 1); return;
        }
        var organizationIndex = Array.IndexOf(e.Args, "--validate-password-organization");
        if (organizationIndex >= 0)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var passed = await DesktopPasswordOrganizationValidation.RunAsync(window, theme, Path.GetFullPath(e.Args[organizationIndex + 1]), baseDirectory); Shutdown(passed ? 0 : 1); return;
        }
        var selectionIndex=Array.IndexOf(e.Args,"--validate-selection");
        if(selectionIndex>=0)
        {
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopSelectionValidation.RunAsync(window,theme,Path.GetFullPath(e.Args[selectionIndex+1]),baseDirectory);Shutdown(passed?0:1);return;
        }
        var extractionToolsIndex=Array.IndexOf(e.Args,"--validate-extraction-tools");
        if(extractionToolsIndex>=0)
        {
            window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;
            window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            var passed=await DesktopExtractionToolsValidation.RunAsync(window,theme,Path.GetFullPath(e.Args[extractionToolsIndex+1]),baseDirectory);
            Shutdown(passed?0:1);return;
        }
        var applicationUiIndex = Array.IndexOf(e.Args, "--validate-application-updates");
        if (applicationUiIndex >= 0)
        {
            var report = Path.Combine(Path.GetFullPath(e.Args[applicationUiIndex+1]), UiText.LanguageCode=="en"?"artifacts/application-update-en-ui-validation.json":"artifacts/application-update-ui-validation.json");
            File.WriteAllText(report, "{\"passed\":false,\"state\":\"running\"}");
            DispatcherUnhandledException += (_, failure) => { File.WriteAllText(report,JsonSerializer.Serialize(new {passed=false,error=failure.Exception.ToString()}));failure.Handled=true;Shutdown(1); };
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var passed = await DesktopApplicationUpdateValidation.RunAsync(window, theme, Path.GetFullPath(e.Args[applicationUiIndex+1]), baseDirectory);
            Shutdown(passed ? 0 : 1); return;
        }
        var shellUiIndex = Array.IndexOf(e.Args, "--validate-shell-ui");
        if (shellUiIndex >= 0)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var passed = await DesktopShellUiValidation.RunAsync(window, theme, Path.GetFullPath(e.Args[shellUiIndex+1]), baseDirectory);
            Shutdown(passed ? 0 : 1); return;
        }
        if (vaultTransferValidationIndex >= 0)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var result = await DesktopVaultTransferValidation.RunAsync(window, theme, Path.GetFullPath(e.Args[vaultTransferValidationIndex + 1]), baseDirectory, dataDirectory);
            Shutdown(result ? 0 : 1); return;
        }
        if (accessibilityValidationIndex >= 0)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var result = await DesktopAccessibilityValidation.RunAsync(window, model, theme, Path.GetFullPath(e.Args[accessibilityValidationIndex + 1]));
            result &= await DesktopDisplayValidation.RunAsync(window, model, Path.GetFullPath(e.Args[accessibilityValidationIndex + 1]));
            Shutdown(result ? 0 : 1); return;
        }
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
                var multiplePasswords = await DesktopMultiPasswordValidation.RunAsync(window, baseDirectory, dataDirectory, validationRoot);
                var accessibility = await DesktopAccessibilityValidation.RunAsync(window, model, theme, validationRoot);
                var displays = await DesktopDisplayValidation.RunAsync(window, model, validationRoot);
                var passed = success && manualSuccess && updateSuccess && compression.Flow && compression.Accessibility && browser && failureDetails && multiplePasswords && accessibility && displays;
                await File.WriteAllTextAsync(Path.Combine(validationRoot, "artifacts/desktop-validation.json"), JsonSerializer.Serialize(new { passed, automaticFlowPassed = success, passwordDialogAndSavePassed = manualSuccess, engineUpdateAndRollbackPassed = updateSuccess,
                    compressionAndPasswordReusePassed = compression.Flow, compressionAutomationLabelsAndShortcutPassed = compression.Accessibility,
                    archiveBrowserPassed = browser, extractionFailureDetailsPassed = failureDetails, multiplePasswordsPassed = multiplePasswords, accessibilityPassed = accessibility, displayLayoutAndNativeDpiPassed = displays, engines = model.EngineVersions.ToArray() }));
                Shutdown(passed ? 0 : 1);
            }
            catch (Exception validationError)
            {
                await File.WriteAllTextAsync(Path.Combine(validationRoot, "artifacts/desktop-validation.json"), JsonSerializer.Serialize(new {passed=false,error=validationError.ToString()}));
                Shutdown(1);
            }
            return;
        }
        var visualIndex = Array.IndexOf(e.Args, "--validate-visual-refresh");
        if (visualIndex >= 0 && visualIndex + 1 < e.Args.Length)
        {
            window.SuppressExitConfirmationForValidation = true;
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
            window.Opacity = 0; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
            var passed = await DesktopVisualRefreshValidation.RunAsync(window, model, theme, Path.GetFullPath(e.Args[visualIndex + 1]));
            Shutdown(passed ? 0 : 1); return;
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
        model.Page = 4;
        window.Show();
        await model.InitializeAsync();
        var startupArchives=e.Args.Where(File.Exists).ToArray();
        if(startupArchives.Length>0) await model.OpenWorkspaceArchiveAsync(startupArchives);
    }

    private static void PrepareValidationDialog(PasswordDialog dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000;
        dialog.ShowActivated = false;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
        {
            ((System.Windows.Controls.PasswordBox)dialog.FindName("SecretBox")).Password = "测试密碼 ' $ & 空格";
            if(dialog.ConfirmationPanel.Visibility==Visibility.Visible)dialog.ConfirmationBox.Password=dialog.SecretBox.Password;
            ((System.Windows.Controls.CheckBox)dialog.FindName("SavePassword")).IsChecked = true;
            ((System.Windows.Controls.Button)dialog.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        });
    }
}
