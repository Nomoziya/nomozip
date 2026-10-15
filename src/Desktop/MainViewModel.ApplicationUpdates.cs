using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private Func<HttpClient> applicationUpdateClient = () => new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private Action<Uri> openApplicationRelease = uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    private string applicationUpdateMessage = UiText.Get("T_1ad4833d7451");
    private AvailableApplicationRelease? applicationRelease;
    private bool checkingApplicationUpdate;
    public bool IsCheckingApplicationUpdate { get => checkingApplicationUpdate; private set => Set(ref checkingApplicationUpdate, value); }
    public string ApplicationVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    public string ApplicationVersionDescription => UiText.Format("T_24dcae0d9315", ApplicationVersion, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
    public string ApplicationUpdateMessage { get => applicationUpdateMessage; private set => Set(ref applicationUpdateMessage, value); }
    public ICommand CheckApplicationUpdatesCommand { get; private set; } = null!;
    public ICommand OpenApplicationDownloadCommand { get; private set; } = null!;

    private void InitializeApplicationUpdateCommands()
    {
        CheckApplicationUpdatesCommand = new AsyncCommand(CheckApplicationUpdatesAsync, () => IsIdle);
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
        ApplicationUpdateMessage = UiText.Get("T_56bba533e12e");
        try
        {
            using var client = applicationUpdateClient();
            var found = await new GitHubApplicationChannel(client).CheckAsync(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), cancellation.Token);
            if (found is null) ApplicationUpdateMessage = UiText.Get("T_3dcae718826e");
            else if (found.IsNewerThan(ApplicationVersion))
            { applicationRelease = found; ApplicationUpdateMessage = UiText.Format("T_4927a97ee0e8", found.Version); }
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
}
