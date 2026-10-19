using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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

internal static class DesktopOfficialEngineValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Requires isolated CI.");
        var language=UiText.LanguageCode;var folder=Path.Combine(root,"artifacts/official-engine-preview",language);Directory.CreateDirectory(folder);
        var data=Path.Combine(root,"artifacts/official-engine-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        var checks=new Dictionary<string,bool>();var sizes=new List<object>();string? error=null;
        void Check(string name,bool passed){checks.Add(name,passed);if(!passed)throw new InvalidOperationException(name);}
        Task Layout()=>window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        async Task Click(Button button,MainViewModel model)
        {button.BringIntoView();await Layout();Check("enabled "+button.Name+" "+checks.Count,button.IsEnabled && button.IsVisible);Invoke(button);await Layout();await model.WaitForIdleAsync();await Layout();}
        async Task Source(int index)
        {
            var combo=window.EngineSourceChoice;combo.BringIntoView();await Layout();combo.IsDropDownOpen=true;await Layout();
            var item=(ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(index);
            ((ISelectionItemProvider)UIElementAutomationPeer.CreatePeerForElement(item)!.GetPattern(PatternInterface.SelectionItem)).Select();combo.IsDropDownOpen=false;await Layout();
        }
        bool Fits(FrameworkElement element)
        {var bounds=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));return element.IsVisible && bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=window.ActualWidth+1 && bounds.Bottom<=window.ActualHeight+1;}
        void Capture(string name)
        {
            var client=(FrameworkElement)window.Content;window.Opacity=1;DesktopAccessibilityValidation.Render(window,Path.Combine(folder,name+".png"),1,clientOnly:true);window.Opacity=0;
            sizes.Add(new{name,width=window.ActualWidth,height=window.ActualHeight,clientWidth=client.ActualWidth,clientHeight=client.ActualHeight});
            Check(name+" requested dimensions",Math.Abs(window.ActualWidth-window.Width)<1 && Math.Abs(window.ActualHeight-window.Height)<1 && Math.Abs(client.ActualWidth+2-window.Width)<1 && Math.Abs(client.ActualHeight+2-window.Height)<1);
        }
        var confirm=false;var mode="normal";var downloads=0;
        try
        {
            var vault=new DpapiPasswordVault(Path.Combine(data,"passwords.apv"));
            await vault.SaveAsync(new SavedPassword(Guid.NewGuid(),"synthetic official update sample","测试密碼 ' $ & 空格","",true,DateTimeOffset.UtcNow));
            var vaultBytes=await File.ReadAllBytesAsync(Path.Combine(data,"passwords.apv"));
            var dialogs=new WindowDialogs(window,confirmEngineUpdate:(_,_)=>confirm);
            var model=new MainViewModel(baseDirectory,data,vault,dialogs);window.DataContext=model;await model.InitializeAsync();
            model.Page=2;model.SettingsSection=4;await Layout();
            Check("default remains nomozip verified source",model.EngineSourceIndex==0 && model.EngineSourceChoices.Count==2);
            model.SelectedEngineVersion="26.03";model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();
            Check("old engine active before update",model.EngineSummary.Contains("26.03"));
            var installer=await File.ReadAllBytesAsync(Path.Combine(root,$".tools/7z2604-{EngineArchitecture.Current}.exe"));
            var sha=Convert.ToHexString(SHA256.HashData(installer));
            var metadata=JsonSerializer.Serialize(new{tag_name="26.04",draft=false,prerelease=false,assets=new[]{new{name=$"7z2604-{EngineArchitecture.Current}.exe",size=installer.LongLength,digest="sha256:"+sha,browser_download_url=OfficialEngineChannel.DownloadUrl("26.04",EngineArchitecture.Current).AbsoluteUri}}});
            model.EngineUpdateClientFactory=()=>new HttpClient(new UiHandler(()=>mode,metadata,installer,()=>downloads++));
            await Source(1);Check("source selection uses binding and disables custom repository",model.EngineSourceIndex==1 && !window.UpdateRepositoryBox.IsEnabled && !model.InstallUpdateCommand.CanExecute(null));
            await Click(window.CheckEngineUpdatesButton,model);
            Check("official update offered with version and source",model.InstallUpdateCommand.CanExecute(null) && model.UpdateMessage.Contains("26.04") && window.EngineSourceHint.Text.Contains("7-zip.org"));
            window.EngineUpdateManagement.IsExpanded=true;await Layout();
            await Click(window.InstallEngineUpdateButton,model);
            Check("declining confirmation performs no download",downloads==0 && model.EngineSummary.Contains("26.03"));
            confirm=true;mode="tampered";await Click(window.InstallEngineUpdateButton,model);
            Check("invalid download preserves engine and allows retry",model.EngineSummary.Contains("26.03") && model.InstallUpdateCommand.CanExecute(null));
            Check("failed download leaves no installer",!Directory.GetFiles(Path.Combine(data,"downloads")).Any());
            window.EngineUpdateManagement.IsExpanded=false;await Layout();window.EngineSourceChoice.BringIntoView();await Layout();Capture("official-failure");
            mode="normal";window.EngineUpdateManagement.IsExpanded=true;await Layout();await Click(window.InstallEngineUpdateButton,model);
            Check("official engine activated through install button",model.EngineSummary.Contains("26.04") && model.PreviousEngineSummary.Contains("26.03") && model.UpdateMessage==UiText.Get("EngineSource.Installed"));
            Check("successful update removes temporary installer",!Directory.GetFiles(Path.Combine(data,"downloads")).Any());
            Check("official receipt exists without nomozip signature",File.Exists(Path.Combine(data,EngineArchitecture.InstalledDirectoryName+"-official","26.04",OfficialEngineReceipt.FileName)));
            var afterUpdate=await File.ReadAllBytesAsync(Path.Combine(data,"passwords.apv"));Check("updating preserves synthetic vault bytes",vaultBytes.SequenceEqual(afterUpdate));
            model.OutputPath=Path.Combine(data,"output");model.Page=0;model.AddFiles([Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z")]);
            using(DesktopExtractionConfirmationValidation.Accept(window)){model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();}
            Check("updated official engine extracts using saved password",model.Tasks.Single().Completed && File.Exists(Path.Combine(model.OutputPath,"中文示例.txt")));
            model.Page=2;model.SettingsSection=4;window.EngineUpdateManagement.IsExpanded=false;await Layout();
            foreach(var themeName in new[]{"light","dark"})foreach(var width in new[]{1152d,800d})
            {
                theme.SetMode(themeName);theme.Apply(false);window.MinWidth=width;window.Width=width;window.Height=width==800?600:768;await Layout();
                window.EngineSourceChoice.BringIntoView();await Layout();
                Check($"source and status fit {themeName} {width}",Fits(window.EngineSourceChoice) && Fits(window.EngineSourceHint) && Fits(window.EngineUpdateStatus));
                Check($"compact source width {themeName} {width}",window.EngineSourceChoice.ActualWidth<280 && window.EngineSourceChoice.ActualHeight>=32);
                Capture($"official-success-{themeName}-{width}");
                await Source(0);Check($"source switch invalidates cached offer {themeName} {width}",!model.InstallUpdateCommand.CanExecute(null));
                window.EngineSourceChoice.BringIntoView();await Layout();Capture($"verified-{themeName}-{width}");await Source(1);
            }
            await Click(window.CheckEngineUpdatesButton,model);Check("current official engine not offered again",!model.InstallUpdateCommand.CanExecute(null));
            var reopened=new MainViewModel(baseDirectory,data,vault,dialogs);await reopened.InitializeAsync();
            Check("official source and active engine persist",reopened.EngineSourceIndex==1 && reopened.EngineSummary.Contains("26.04"));
            reopened.RollbackEngineCommand.Execute(null);await reopened.WaitForIdleAsync();Check("rollback remains available after reopen",reopened.EngineSummary.Contains("26.03"));
            Check("no plaintext credentials in official receipt",!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(data,EngineArchitecture.InstalledDirectoryName+"-official","26.04",OfficialEngineReceipt.FileName))).Contains("测试密碼"));
        }
        catch(Exception exception){error=exception.GetType().Name+": "+exception.Message;checks["completed without exception"]=false;}
        var passed=checks.Count>=30 && checks.Values.All(value=>value);
        await File.WriteAllTextAsync(Path.Combine(root,$"artifacts/official-engine-ui-{language}-validation.json"),JsonSerializer.Serialize(new{passed,language,architecture=EngineArchitecture.Current,uiAutomationProviders=true,actualMouseInput=false,officialPayloadWithFixtureHttp=true,realNetworkCoveredBy="OfficialEngineValidation --integration",checks,sizes,error},new JsonSerializerOptions{WriteIndented=true}));
        return passed;
    }
    private sealed class UiHandler(Func<string> mode,string metadata,byte[] installer,Action downloaded):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();HttpContent content;
            if(request.RequestUri!.Host=="api.github.com")content=new StringContent(metadata,Encoding.UTF8,"application/json");
            else{downloaded();var bytes=installer.ToArray();if(mode()=="tampered")bytes[^1]^=1;content=new ByteArrayContent(bytes);}
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=content});
        }
    }
}
