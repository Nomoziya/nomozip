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
    private string applicationUpdateMessage = "仅检查正式版。点击检查后连接 GitHub，不会自动安装。";
    private AvailableApplicationRelease? applicationRelease;
    private bool checkingApplicationUpdate;
    public bool IsCheckingApplicationUpdate { get => checkingApplicationUpdate; private set => Set(ref checkingApplicationUpdate, value); }
    public string ApplicationVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    public string ApplicationVersionDescription => $"当前版本：{ApplicationVersion}（{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()} 轻量版）";
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
                ApplicationUpdateMessage = $"已打开 {applicationRelease.Version} 下载页。请选择 win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()} 轻量安装包，关闭软件后运行安装器。";
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException)
            { ApplicationUpdateMessage = "无法打开浏览器。请访问 https://github.com/Nomoziya/nomozip/releases 查看软件正式版。"; }
        }, () => IsIdle && applicationRelease is not null);
    }

    private async Task CheckApplicationUpdatesAsync()
    {
        IsCheckingApplicationUpdate = true; IsBusy = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource();
        applicationRelease = null; CommandManager.InvalidateRequerySuggested();
        ApplicationUpdateMessage = "正在检查软件正式版…";
        try
        {
            using var client = applicationUpdateClient();
            var found = await new GitHubApplicationChannel(client).CheckAsync(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), cancellation.Token);
            if (found is null) ApplicationUpdateMessage = "尚无适用于当前架构的正式版轻量安装包。";
            else if (found.IsNewerThan(ApplicationVersion))
            { applicationRelease = found; ApplicationUpdateMessage = $"发现软件正式版 {found.Version}。可打开下载页查看说明并下载安装。"; }
            else ApplicationUpdateMessage = ApplicationVersion.Contains('-')
                ? $"最新正式版：{found.Version}。当前为较新的开发预览，无需降级。"
                : $"最新正式版：{found.Version}。当前软件已是最新版本。";
        }
        catch (OperationCanceledException) { ApplicationUpdateMessage = cancellation.IsCancellationRequested ? "已取消软件版本检查。" : "软件版本检查超时，请稍后重试。"; }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        { ApplicationUpdateMessage = "GitHub 暂时限制请求，请稍后再检查软件更新。"; }
        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { ApplicationUpdateMessage = "无法检查软件正式版，请检查网络后重试，或访问 GitHub 发布页。"; }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; IsCheckingApplicationUpdate = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
    }

    internal void ConfigureApplicationUpdateValidation(Func<HttpClient> client, Action<Uri> open)
    { applicationUpdateClient = client; openApplicationRelease = open; }
}
