using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopApplicationUpdateValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, ThemeResources theme, string root, string baseDirectory)
    {
        var data = Path.Combine(root,"artifacts/application-update-ui",Guid.NewGuid().ToString("N"));
        var checks = new Dictionary<string,bool>();
        void Check(string name,bool passed) => checks[name]=passed;
        async Task Layout() => await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        var mode = "new"; Uri? opened=null;
        try
        {
            var model = new MainViewModel(baseDirectory,data,new DpapiPasswordVault(Path.Combine(data,"passwords.apv")),new WindowDialogs(window));
            model.ConfigureApplicationUpdateValidation(() => new HttpClient(new Handler(async (_,token) =>
            {
                if (mode == "cancel") await Task.Delay(Timeout.Infinite,token);
                if (mode == "offline") throw new HttpRequestException();
                if (mode == "timeout") throw new TaskCanceledException();
                if (mode == "limited") return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                var version = mode == "old" ? "0.1.0" : "99.0.0";
                var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                var name = $"nomozip-{version}-light-setup-win-{arch}.exe";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new[] { new { tag_name="v"+version,draft=false,prerelease=false,assets=new[] { new { name,size=1024,browser_download_url=$"https://github.com/Nomoziya/nomozip/releases/download/v{version}/{name}" } } } })) };
            })),uri=>opened=uri);
            window.DataContext=model; await model.InitializeAsync(); model.Page=2; await Layout();
            var before = model.UpdateMessage; var engine=model.EngineSummary;
            Check("actual assembly version displayed",model.ApplicationVersion == "0.2.0-preview.3");
            Check("no software request during initialization",model.ApplicationUpdateMessage.StartsWith("仅检查"));
            Check("download disabled before check",!model.OpenApplicationDownloadCommand.CanExecute(null));
            foreach (var contrast in new[] {false,true})
            foreach (var width in new[] {1120,800})
            {
                theme.Apply(contrast); window.Width=width; window.Height=width==800?600:760; await Layout();
                var button=(Button)window.FindName("CheckApplicationUpdatesButton"); button.BringIntoView(); await Layout();
                var peer=UIElementAutomationPeer.CreatePeerForElement(button)!;
                Check($"check reachable named focusable {width} {contrast}",button.IsVisible && peer.IsKeyboardFocusable() && peer.GetName()=="检查软件更新");
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); await Layout(); await model.WaitForIdleAsync(); await Layout();
                var download=(Button)window.FindName("OpenApplicationDownloadButton"); download.BringIntoView(); await Layout();
                Check($"new update binding enables download {width} {contrast}",download.IsEnabled && model.ApplicationUpdateMessage.Contains("99.0.0"));
                var downloadPeer=UIElementAutomationPeer.CreatePeerForElement(download)!;
                ((IInvokeProvider)downloadPeer.GetPattern(PatternInterface.Invoke)).Invoke(); await Layout();
                Check($"opens canonical release page {width} {contrast}",opened?.AbsoluteUri=="https://github.com/Nomoziya/nomozip/releases/tag/v99.0.0");
                Check($"download text explains architecture {width} {contrast}",model.ApplicationUpdateMessage.Contains("win-x64") && model.ApplicationUpdateMessage.Contains("关闭软件"));
                Check($"status announced politely {width} {contrast}",AutomationProperties.GetLiveSetting((TextBlock)window.FindName("ApplicationUpdateStatus"))==AutomationLiveSetting.Polite);
                DesktopAccessibilityValidation.Render(window,Path.Combine(root,$"artifacts/application-update-{width}-{contrast}.png"),1.0);
            }
            foreach (var failure in new[] {"old","offline","limited","timeout"})
            {
                mode=failure; model.CheckApplicationUpdatesCommand.Execute(null); await model.WaitForIdleAsync();
                Check("download cleared after " + failure,!model.OpenApplicationDownloadCommand.CanExecute(null));
                Check("clear status after " + failure,model.ApplicationUpdateMessage.Contains(failure switch { "old"=>"无需降级","limited"=>"限制请求","timeout"=>"超时",_=>"检查网络" }));
            }
            mode="cancel"; model.CheckApplicationUpdatesCommand.Execute(null);
            Check("checking blocks other mutation commands",model.IsBusy && !model.ImportVaultCommand.CanExecute(null) && !model.CheckUpdatesCommand.CanExecute(null));
            model.CancelCommand.Execute(null); await model.WaitForIdleAsync();
            Check("cancel returns idle",model.IsIdle && model.ApplicationUpdateMessage.Contains("已取消"));
            Check("engine state and message independent",before==model.UpdateMessage && engine==model.EngineSummary);
            Check("no profile modification by software check",!File.Exists(Path.Combine(data,"passwords.apv")));
        }
        catch (Exception error) { checks["unexpected " + error]=false; }
        finally { theme.Apply(SystemParameters.HighContrast); }
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/application-update-ui-validation.json"),JsonSerializer.Serialize(new { passed=checks.Values.All(value=>value),total=checks.Count,checks,syntheticData=true }));
        return checks.Values.All(value=>value);
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>action(request,token); }
}
