using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using System.IO;
using System.Windows;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private Func<HttpClient> applicationUpdateClient = () => new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private Action<Uri> openApplicationRelease = uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    private string applicationUpdateMessage = UiText.Get("T_1ad4833d7451");
    private AvailableApplicationRelease? applicationRelease;
    private bool checkingApplicationUpdate;
    private string? preparedApplicationInstaller;
    private bool registeredApplicationInstallation;
    private Action<ProcessStartInfo> startApplicationInstaller = start => { using var process = Process.Start(start) ?? throw new InvalidOperationException("Installer did not start."); };
    private Action exitForApplicationUpdate = () => Application.Current.Shutdown();
    private int applicationDownloadProgress;
    private bool downloadingApplicationUpdate;
    public bool IsDownloadingApplicationUpdate { get => downloadingApplicationUpdate; private set => Set(ref downloadingApplicationUpdate, value); }
    public int ApplicationDownloadProgress { get => applicationDownloadProgress; private set => Set(ref applicationDownloadProgress, value); }
    public bool CanAutomaticallyInstall => registeredApplicationInstallation;
    public string ApplicationInstallationHint => UiText.Get(registeredApplicationInstallation ? "SoftwareUpdate.InstalledHint" : "SoftwareUpdate.PortableHint");
    public bool IsCheckingApplicationUpdate { get => checkingApplicationUpdate; private set => Set(ref checkingApplicationUpdate, value); }
    public string ApplicationVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    public string ApplicationVersionDescription => UiText.Format("T_24dcae0d9315", ApplicationVersion, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
    public string ApplicationUpdateMessage { get => applicationUpdateMessage; private set => Set(ref applicationUpdateMessage, value); }
    public ICommand CheckApplicationUpdatesCommand { get; private set; } = null!;
    public ICommand OpenApplicationDownloadCommand { get; private set; } = null!;
    public ICommand DownloadApplicationUpdateCommand { get; private set; } = null!;
    public ICommand InstallApplicationUpdateCommand { get; private set; } = null!;
    public ICommand CleanApplicationInstallersCommand { get; private set; } = null!;
    public ICommand SaveApplicationUpdateSettingsCommand { get; private set; } = null!;
    private bool autoCleanApplicationInstallers = true;
    public bool AutoCleanApplicationInstallers { get => autoCleanApplicationInstallers; set => Set(ref autoCleanApplicationInstallers, value); }
    private InstallerCacheSummary? applicationCache;
    private async Task CompleteApplicationUpdateAsync()
    {
        try
        {
            var result = await Task.Run(() => ApplicationUpdateReceipt.CompleteAsync(ApplicationCacheDirectory, ApplicationVersion, baseDirectory, AutoCleanApplicationInstallers));
            if (result is not null) ApplicationUpdateMessage = UiText.Format("SoftwareUpdate.CacheCleaned", result.Deleted, result.Bytes / 1048576.0, result.Skipped);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { ApplicationUpdateMessage = UiText.Get("SoftwareUpdate.CacheCleanFailed"); }
        finally { RefreshApplicationCache(); }
    }
    public string ApplicationCacheSummary => applicationCache is null ? UiText.Get("SoftwareUpdate.CacheUnavailable") :
        UiText.Format("SoftwareUpdate.CacheSummary", applicationCache.Files, applicationCache.Bytes / 1048576.0);
    private string ApplicationCacheDirectory => Path.Combine(dataDirectory, "application-updates");
    private void RefreshApplicationCache()
    {
        try { applicationCache = ApplicationInstallerCache.Inspect(ApplicationCacheDirectory); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { applicationCache = null; }
        Changed(nameof(ApplicationCacheSummary));
    }
    private async Task CleanApplicationInstallersAsync()
    {
        IsBusy = true; idle = new TaskCompletionSource();
        ApplicationUpdateMessage = UiText.Get("SoftwareUpdate.CacheCleaning");
        try
        {
            var result = await Task.Run(() => ApplicationInstallerCache.Clean(ApplicationCacheDirectory, preparedApplicationInstaller));
            ApplicationUpdateMessage = UiText.Format("SoftwareUpdate.CacheCleaned", result.Deleted, result.Bytes / 1048576.0, result.Skipped);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ApplicationUpdateMessage = UiText.Get("SoftwareUpdate.CacheCleanFailed"); }
        finally { RefreshApplicationCache(); IsBusy = false; idle.TrySetResult(); }
    }

    private void InitializeApplicationUpdateCommands()
    {
        SaveApplicationUpdateSettingsCommand = new AsyncCommand(async () =>
        {
            IsBusy = true; idle = new TaskCompletionSource();
            try { await SaveSettingsAsync(); ApplicationUpdateMessage = UiText.Get("SoftwareUpdate.SettingsSaved"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ApplicationUpdateMessage = UiText.Get("Download.AccessDenied"); }
            finally { IsBusy = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
        }, () => IsIdle && settingsWritable);
        CleanApplicationInstallersCommand = new AsyncCommand(CleanApplicationInstallersAsync, () => IsIdle);
        RefreshApplicationCache();
        try { registeredApplicationInstallation = ApplicationInstallation.IsRegistered(Path.Combine(baseDirectory, "ArchivePassword.exe")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { registeredApplicationInstallation = false; }
        CheckApplicationUpdatesCommand = new AsyncCommand(CheckApplicationUpdatesAsync, () => IsIdle);
        DownloadApplicationUpdateCommand = new AsyncCommand(DownloadApplicationUpdateAsync,
            () => IsIdle && registeredApplicationInstallation && applicationRelease?.CanDownload == true && preparedApplicationInstaller is null);
        InstallApplicationUpdateCommand = new AsyncCommand(InstallApplicationUpdateAsync,
            () => IsIdle && registeredApplicationInstallation && applicationRelease is not null && preparedApplicationInstaller is not null);
        OpenApplicationDownloadCommand = new RelayCommand(() =>
        {
            try
            {
                openApplicationRelease(applicationRelease!.ReleaseUrl);
                ApplicationUpdateMessage = UiText.Format("T_b5836abfbd39", applicationRelease.Version, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException)
            { ApplicationUpdateMessage = UiText.Get("T_3e0c4202739b"); }
        }, () => IsIdle && applicationRelease is not null);
    }

    private async Task CheckApplicationUpdatesAsync()
    {
        IsCheckingApplicationUpdate = true; IsBusy = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource();
        applicationRelease = null; CommandManager.InvalidateRequerySuggested();
        DiscardApplicationInstaller(); ApplicationDownloadProgress = 0;
        ApplicationUpdateMessage = UiText.Get("T_56bba533e12e");
        try
        {
            using var client = applicationUpdateClient();
            var found = await new GitHubApplicationChannel(client).CheckAsync(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), cancellation.Token);
            if (found is null) ApplicationUpdateMessage = UiText.Get("T_3dcae718826e");
            else if (found.IsNewerThan(ApplicationVersion))
            { applicationRelease = found; ApplicationUpdateMessage = UiText.Format("T_4927a97ee0e8", found.Version) +
                (found.CanDownload ? "" : "\n" + UiText.Get("SoftwareUpdate.NoDigest")); }
            else ApplicationUpdateMessage = ApplicationVersion.Contains('-')
                ? UiText.Format("T_313a3f58a787", found.Version)
                : UiText.Format("T_e9e70eaf87d7", found.Version);
        }
        catch (OperationCanceledException) { ApplicationUpdateMessage = cancellation.IsCancellationRequested ? UiText.Get("T_cd62c4ff0804") : UiText.Get("T_dbf2ce470c29"); }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        { ApplicationUpdateMessage = UiText.Get("T_4796f4576711"); }
        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { ApplicationUpdateMessage = UiText.Get("T_0be3e9aaa9fa"); }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; IsCheckingApplicationUpdate = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
    }

    internal void ConfigureApplicationUpdateValidation(Func<HttpClient> client, Action<Uri> open)
    { applicationUpdateClient = client; openApplicationRelease = open; }

    internal void ConfigureApplicationInstallValidation(bool registered, Action<ProcessStartInfo> start, Action exit)
    {
        registeredApplicationInstallation = registered; startApplicationInstaller = start; exitForApplicationUpdate = exit;
        Changed(nameof(CanAutomaticallyInstall)); Changed(nameof(ApplicationInstallationHint));
        CommandManager.InvalidateRequerySuggested();
    }

    private void DiscardApplicationInstaller()
    {
        var path = preparedApplicationInstaller; preparedApplicationInstaller = null;
        if (path is not null) try { File.Delete(path); ApplicationUpdateReceipt.Discard(ApplicationCacheDirectory); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* A running installer retains its own file. */ }
        RefreshApplicationCache();
    }

    private async Task DownloadApplicationUpdateAsync()
    {
        var release = applicationRelease!;
        IsDownloadingApplicationUpdate = true;
        IsCheckingApplicationUpdate = true; IsBusy = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource();
        ApplicationDownloadProgress = 0; ApplicationUpdateMessage = UiText.Get("SoftwareUpdate.Downloading");
        try
        {
            using var client = applicationUpdateClient(); client.Timeout = TimeSpan.FromMinutes(10);
            var progress = new Progress<int>(value => ApplicationDownloadProgress = value);
            var details = new Progress<DownloadProgress>(value =>
            { if (IsDownloadingApplicationUpdate) ApplicationUpdateMessage = value.Description; });
            preparedApplicationInstaller = await new ApplicationInstallerDownload(client).DownloadAsync(release,
                RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), Path.Combine(dataDirectory, "application-updates"), progress, cancellation.Token, details);
            ApplicationDownloadProgress = 100; ApplicationUpdateMessage = UiText.Format("SoftwareUpdate.Ready", release.Version);
        }
        catch (OperationCanceledException) { ApplicationUpdateMessage = UiText.Get(cancellation.IsCancellationRequested ? "SoftwareUpdate.Cancelled" : "SoftwareUpdate.DownloadTimeout"); }
        catch (InvalidDataException) { ApplicationUpdateMessage = UiText.Get("SoftwareUpdate.InvalidDownload"); }
        catch (HttpRequestException) { ApplicationUpdateMessage = UiText.Get("Download.NetworkFailed"); }
        catch (IOException) { ApplicationUpdateMessage = UiText.Get("Download.ReadWriteFailed"); }
        catch (UnauthorizedAccessException) { ApplicationUpdateMessage = UiText.Get("Download.AccessDenied"); }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; IsCheckingApplicationUpdate = false; IsDownloadingApplicationUpdate = false; RefreshApplicationCache(); idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
    }

    private async Task InstallApplicationUpdateAsync()
    {
        if (!dialogs.ConfirmApplicationInstall(applicationRelease!.Version)) return;
        IsBusy = true; idle = new TaskCompletionSource(); var started = false;
        try
        {
            using var locked = await Task.Run(() => ApplicationInstallerDownload.OpenVerified(preparedApplicationInstaller!, applicationRelease));
            await new ApplicationUpdateReceipt(ApplicationVersion, applicationRelease.Version, baseDirectory).SaveAsync(ApplicationCacheDirectory);
            startApplicationInstaller(ApplicationInstallation.CreateStart(preparedApplicationInstaller!, baseDirectory));
            started = true; ApplicationUpdateMessage = UiText.Get("SoftwareUpdate.Started");
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or ArgumentException)
        {
            try { ApplicationUpdateReceipt.Discard(ApplicationCacheDirectory); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            DiscardApplicationInstaller(); ApplicationUpdateMessage = UiText.Get("SoftwareUpdate.InstallFailed");
        }
        finally { IsBusy = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
        if (started) exitForApplicationUpdate();
    }
}
