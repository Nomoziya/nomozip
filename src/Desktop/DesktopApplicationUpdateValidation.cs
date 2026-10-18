using System.IO;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopApplicationUpdateValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, ThemeResources theme, string root, string baseDirectory)
    {
        var data = Path.Combine(root,"artifacts/application-update-ui",Guid.NewGuid().ToString("N"));
        var checks = new Dictionary<string,bool>();
        var language=UiText.LanguageCode;var originalMode=theme.Mode;
        void Check(string name,bool passed) => checks[name]=passed;
        async Task Layout() => await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        var mode = "new"; Uri? opened=null; var requests=0; var payload=Enumerable.Range(0,100000).Select(v=>(byte)(v%251)).ToArray();
        var sha=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();var confirm=true;var exits=0;ProcessStartInfo? installed=null;var launchFailure=false;
        try
        {
            var model = new MainViewModel(baseDirectory,data,new DpapiPasswordVault(Path.Combine(data,"passwords.apv")),new WindowDialogs(window,confirmApplicationInstall:_=>confirm));
            model.ConfigureApplicationUpdateValidation(() => new HttpClient(new Handler(async (request,token) =>
            {
                requests++;
                if(request.RequestUri!.Host=="github.com")
                {
                    if(mode=="download-cancel") await Task.Delay(Timeout.Infinite,token);
                    if(mode=="download-offline") throw new HttpRequestException();
                    if(mode=="download-timeout") throw new TaskCanceledException();
                    return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(mode=="bad-download"?[1,2,3]:payload)};
                }
                Check("software repository fixed independently of engine setting",request.RequestUri!.AbsoluteUri.StartsWith("https://api.github.com/repos/Nomoziya/nomozip/releases?",StringComparison.Ordinal));
                if (mode == "cancel") await Task.Delay(Timeout.Infinite,token);
                if (mode == "offline") throw new HttpRequestException();
                if (mode == "timeout") throw new TaskCanceledException();
                if (mode == "limited") return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                var version = mode == "old" ? "0.1.0" : "99.0.0";
                var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                var name = $"nomozip-{version}-light-setup-win-{arch}.exe";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new[] { new { tag_name="v"+version,draft=false,prerelease=false,assets=new[] { new { name,size=payload.Length,digest=mode=="no-digest"?null:"sha256:"+sha,browser_download_url=$"https://github.com/Nomoziya/nomozip/releases/download/v{version}/{name}" } } } })) };
            })),uri=>opened=uri);
            model.ConfigureApplicationInstallValidation(true,start=>{if(launchFailure)throw new System.ComponentModel.Win32Exception();installed=start;},()=>exits++);
            window.DataContext=model; await model.InitializeAsync(); model.Page=2; model.SettingsSection=4; window.ApplicationUpdateActions.IsExpanded=true; await Layout();
            model.UpdateRepository="other/engine-test";
            var before = model.UpdateMessage; var engine=model.EngineSummary;
            var expectedVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(App).Assembly.Location).ProductVersion!.Split('+')[0];
            Check("actual assembly version displayed",((TextBlock)window.FindName("ApplicationVersionText")).Text.Contains(expectedVersion));
            Check("no software request during initialization",requests==0);
            Check("download disabled before check",!model.OpenApplicationDownloadCommand.CanExecute(null));
            foreach (var contrast in new[] {false,true})
            foreach (var width in new[] {1120,800})
            {
                theme.Apply(contrast); window.Width=width; window.Height=width==800?600:760; await Layout();
                var button=(Button)window.FindName("CheckApplicationUpdatesButton"); button.BringIntoView(); await Layout();
                var peer=UIElementAutomationPeer.CreatePeerForElement(button)!;
                Check($"check reachable named focusable {width} {contrast}",button.IsVisible && peer.IsKeyboardFocusable() && peer.GetName()==UiText.Get("T_e64da2a40b53"));
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); await Layout(); await model.WaitForIdleAsync(); await Layout();
                var download=(Button)window.FindName("OpenApplicationDownloadButton"); download.BringIntoView(); await Layout();
                Check($"new update binding enables download {width} {contrast}",download.IsEnabled && model.ApplicationUpdateMessage.Contains("99.0.0"));
                var downloadPeer=UIElementAutomationPeer.CreatePeerForElement(download)!;
                ((IInvokeProvider)downloadPeer.GetPattern(PatternInterface.Invoke)).Invoke(); await Layout();
                Check($"opens canonical release page {width} {contrast}",opened?.AbsoluteUri=="https://github.com/Nomoziya/nomozip/releases/tag/v99.0.0");
                Check($"download text explains architecture {width} {contrast}",model.ApplicationUpdateMessage==UiText.Format("T_b5836abfbd39","99.0.0",EngineArchitecture.Current));
                Check($"status announced politely {width} {contrast}",AutomationProperties.GetLiveSetting((TextBlock)window.FindName("ApplicationUpdateStatus"))==AutomationLiveSetting.Polite);
                DesktopAccessibilityValidation.Render(window,Path.Combine(root,$"artifacts/application-update-{language}-{width}-{contrast}.png"),1.0);
            }
            foreach (var failure in new[] {"old","offline","limited","timeout"})
            {
                mode=failure; model.CheckApplicationUpdatesCommand.Execute(null); await model.WaitForIdleAsync();
                Check("download cleared after " + failure,!model.OpenApplicationDownloadCommand.CanExecute(null));
                Check("clear status after " + failure,model.ApplicationUpdateMessage==(failure=="old"?UiText.Format(expectedVersion.Contains('-')?"T_313a3f58a787":"T_e9e70eaf87d7","0.1.0"):UiText.Get(failure switch {"limited"=>"T_4796f4576711","timeout"=>"T_dbf2ce470c29",_=>"T_0be3e9aaa9fa"})));
            }
            mode="cancel"; model.CheckApplicationUpdatesCommand.Execute(null);
            Check("checking blocks other mutation commands",model.IsBusy && !model.ImportVaultCommand.CanExecute(null) && !model.CheckUpdatesCommand.CanExecute(null));
            model.CancelCommand.Execute(null); await model.WaitForIdleAsync();
            Check("cancel returns idle",model.IsIdle && model.ApplicationUpdateMessage==UiText.Get("T_cd62c4ff0804"));
            Check("engine state and message independent",before==model.UpdateMessage && engine==model.EngineSummary);
            Check("no profile modification by software check",!File.Exists(Path.Combine(data,"passwords.apv")));
            var vault=Path.Combine(data,"passwords.apv");await File.WriteAllBytesAsync(vault,[9,8,7]);
            var settings=Path.Combine(data,"settings.json");await File.WriteAllTextAsync(settings,"{\"synthetic\":\"keep\"}");
            mode="new";model.CheckApplicationUpdatesCommand.Execute(null);await model.WaitForIdleAsync();
            model.ConfigureApplicationInstallValidation(false,_=>throw new InvalidOperationException(),()=>exits++);
            Check("portable copy keeps release page but cannot auto install",model.OpenApplicationDownloadCommand.CanExecute(null) && !model.DownloadApplicationUpdateCommand.CanExecute(null) && !model.InstallApplicationUpdateCommand.CanExecute(null));
            model.ConfigureApplicationInstallValidation(true,start=>{if(launchFailure)throw new System.ComponentModel.Win32Exception();installed=start;},()=>exits++);
            mode="no-digest";model.CheckApplicationUpdatesCommand.Execute(null);await model.WaitForIdleAsync();
            Check("missing digest leaves page fallback and clear message",model.OpenApplicationDownloadCommand.CanExecute(null) && !model.DownloadApplicationUpdateCommand.CanExecute(null) && model.ApplicationUpdateMessage.Contains(UiText.Get("SoftwareUpdate.NoDigest")));
            mode="new";model.CheckApplicationUpdatesCommand.Execute(null);await model.WaitForIdleAsync();
            foreach(var failure in new[]{"bad-download","download-offline","download-timeout","download-cancel"})
            {
                mode=failure;model.DownloadApplicationUpdateCommand.Execute(null);
                if(failure=="download-cancel")
                {
                    Check("download blocks archive and import mutation",model.IsBusy && !model.ImportVaultCommand.CanExecute(null) && !model.CheckUpdatesCommand.CanExecute(null));
                    Check("download cancel and progress bindings reachable",((Button)window.FindName("DownloadApplicationUpdateButton")).Command==model.DownloadApplicationUpdateCommand && ((System.Windows.Controls.Primitives.RangeBase)window.FindName("ApplicationDownloadProgressBar")).Maximum==100);
                    model.CancelCommand.Execute(null);
                }
                await model.WaitForIdleAsync();await Layout();
                Check("failed download stays open with retry "+failure,model.IsIdle && exits==0 && !model.InstallApplicationUpdateCommand.CanExecute(null) && model.DownloadApplicationUpdateCommand.CanExecute(null));
                if(failure=="download-timeout") Check("download timeout distinguished from user cancellation",model.ApplicationUpdateMessage==UiText.Get("SoftwareUpdate.DownloadTimeout"));
                Check("failed download has no partial "+failure,!Directory.Exists(Path.Combine(data,"application-updates")) || !Directory.EnumerateFiles(Path.Combine(data,"application-updates")).Any());
            }
            mode="new";
            async Task Download(){model.DownloadApplicationUpdateCommand.Execute(null);await model.WaitForIdleAsync();await Layout();}
            await Download();
            Check("download ready exact progress and install binding",model.ApplicationDownloadProgress==100 && model.InstallApplicationUpdateCommand.CanExecute(null) && !model.DownloadApplicationUpdateCommand.CanExecute(null));
            var installButton=(Button)window.FindName("InstallApplicationUpdateButton");installButton.BringIntoView();await Layout();
            var installPeer=UIElementAutomationPeer.CreatePeerForElement(installButton)!;
            Check("install button named and keyboard focusable",installPeer.GetName()==UiText.Get("SoftwareUpdate.Install") && installPeer.IsKeyboardFocusable());
            var samples=Path.Combine(root,"artifacts/software-update-preview");Directory.CreateDirectory(samples);
            foreach(var modeName in new[]{"light","dark","contrast"})
            foreach(var width in new[]{1120,800})
            {
                theme.SetMode(modeName=="contrast"?"light":modeName);theme.Apply(modeName=="contrast");window.Width=width;window.Height=width==800?600:760;
                await Layout();((Border)window.FindName("SoftwareUpdateCard")).BringIntoView();await Layout();
                foreach(var name in new[]{"CheckApplicationUpdatesButton","OpenApplicationDownloadButton","DownloadApplicationUpdateButton","InstallApplicationUpdateButton"})
                {
                    var b=(Button)window.FindName(name);b.BringIntoView();await Layout();var box=b.TransformToAncestor(window).TransformBounds(new Rect(b.RenderSize));
                    Check($"ready action fully visible {name} {modeName} {width}",box.Left>=0 && box.Right<=window.ActualWidth && box.Top>=0 && box.Bottom<=window.ActualHeight && b.ActualHeight>=36);
                }
                DesktopAccessibilityValidation.Render(window,Path.Combine(samples,$"{language}-{modeName}-{width}-ready.png"),1,clientOnly:true);
            }
            confirm=false;model.InstallApplicationUpdateCommand.Execute(null);await Layout();
            Check("declined install keeps ready file and running app",exits==0 && installed is null && model.InstallApplicationUpdateCommand.CanExecute(null));confirm=true;
            var ready=Directory.EnumerateFiles(Path.Combine(data,"application-updates"),"*.exe").Single();await File.WriteAllBytesAsync(ready,[1]);
            model.InstallApplicationUpdateCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("tampered prepared installer never launches or exits",installed is null && exits==0 && model.DownloadApplicationUpdateCommand.CanExecute(null));
            await Download();launchFailure=true;model.InstallApplicationUpdateCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("failed process launch keeps app usable",model.IsIdle && exits==0 && model.DownloadApplicationUpdateCommand.CanExecute(null));launchFailure=false;
            await Download();
            ((IInvokeProvider)installPeer.GetPattern(PatternInterface.Invoke)).Invoke();await Layout();await model.WaitForIdleAsync();await Layout();
            Check("UIA install launches exact explicit setup and exits once",exits==1 && installed is not null && installed.Arguments=="/S /UPDATE /D="+Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar) && !installed.UseShellExecute);
            Check("download launch do not modify vault settings",(await File.ReadAllBytesAsync(vault)).SequenceEqual(new byte[]{9,8,7}) && await File.ReadAllTextAsync(settings)=="{\"synthetic\":\"keep\"}");
            model.CheckApplicationUpdatesCommand.Execute(null);await model.WaitForIdleAsync();
            Check("next explicit check clears prepared installer",!Directory.EnumerateFiles(Path.Combine(data,"application-updates")).Any() && !model.InstallApplicationUpdateCommand.CanExecute(null));
        }
        catch (Exception error) { checks["unexpected " + error]=false; }
        finally { theme.SetMode(originalMode);theme.Apply(SystemParameters.HighContrast); }
        await File.WriteAllTextAsync(Path.Combine(root,language=="en"?"artifacts/application-update-en-ui-validation.json":"artifacts/application-update-ui-validation.json"),JsonSerializer.Serialize(new { passed=checks.Values.All(value=>value),total=checks.Count,checks,language,syntheticData=true,actualInstallationChanged=false,actualVisibleNarratorTested=false }));
        return checks.Values.All(value=>value);
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>action(request,token); }
}
