using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopCompressionContentsValidation
{
    internal static async Task RunAsync(MainWindow window,ThemeResources theme,string root,string data,string baseDirectory,
        Action<string,bool> check,Action<Window,string> capture)
    {
        data=Path.Combine(data,"compression-contents");Directory.CreateDirectory(data);
        var source=Path.Combine(data,"source");Directory.CreateDirectory(Path.Combine(source,"cache"));
        await File.WriteAllTextAsync(Path.Combine(source,"keep.txt"),"abc");await File.WriteAllTextAsync(Path.Combine(source,"skip.tmp"),"excluded");
        await File.WriteAllTextAsync(Path.Combine(source,"cache","hidden.txt"),"excluded");
        Exception? previewFailure=null;var previewShown=false;
        var previewDone=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs=new WindowDialogs(window,prepareCompressionContents:dialog=>
        {
            dialog.Opacity=0;dialog.ShowActivated=false;
            dialog.Loaded+=async(_,_)=>
            {
                try
                {
                    await dialog.Model.InitializeAsync();previewShown=true;
                    check("content dialog displays scan from actual preview button",dialog.Model.Result is {FileCount:1,SourceBytes:3,ExcludedCount:2} && dialog.ContentTable.Items.Count==4);
                    foreach(var mode in new[]{"light","dark","contrast"})foreach(var width in new[]{860d,512d})
                    {
                        theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");dialog.Width=width;dialog.Height=width==512?360:600;
                        await dialog.Dispatcher.InvokeAsync(dialog.UpdateLayout,DispatcherPriority.ApplicationIdle);
                        check($"content preview close action accessible {mode} {width}",dialog.ClosePreviewButton.IsVisible && dialog.ClosePreviewButton.IsEnabled && dialog.ClosePreviewButton.Focusable);
                        capture(dialog,$"next-content-preview-{mode}-{width}");
                    }
                }
                catch(Exception error){previewFailure=error;}
                finally {dialog.Close();theme.SetMode("light");theme.Apply(false);previewDone.TrySetResult();}
            };
        });
        var model=new MainViewModel(baseDirectory,data,new DpapiPasswordVault(Path.Combine(data,"synthetic.apv")),dialogs);
        await model.InitializeAsync();model.SelectedEngineVersion="26.04";model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();
        model.Page=3;window.DataContext=model;window.Width=1152;window.Height=768;
        model.CompressionFormat=1;model.CompressionOutput=Path.Combine(data,"output.zip");model.AddCompressionSources([source]);
        var view=window.CompressionPage;view.CompressionExclusionsExpander.IsExpanded=true;
        Task Layout()=>window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        await Layout();view.ExcludedDirectoriesBox.Text="bad/path";await Layout();
        check("content invalid folder rule disables create and preview",model.CompressionExclusionError.Length>0 && !view.CreateButton.IsEnabled && !view.PreviewCompressionContentsButton.IsEnabled);
        view.ExcludedDirectoriesBox.Text="cache";view.ExcludedExtensionsBox.Text="tmp";view.PreviewCompressionContentsButton.BringIntoView();await Layout();
        check("content real form sets exclusion rules",model.CompressionExcludedDirectories=="cache" && model.CompressionExcludedExtensions=="tmp" && model.CompressionSourceError=="");
        Invoke(view.PreviewCompressionContentsButton);await previewDone.Task.WaitAsync(TimeSpan.FromSeconds(30));if(previewFailure is not null)throw previewFailure;await Layout();
        check("content preview summary returns to compression footer",previewShown && model.CompressionContentSummary.StartsWith(UiText.Format("Compression.LastPreview","")));
        foreach(var mode in new[]{"light","dark","contrast"})foreach(var width in new[]{1152d,800d})
        {
            theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");window.Width=width;window.Height=width==800?600:768;
            view.PreviewCompressionContentsButton.BringIntoView();await Layout();capture(window,$"next-content-form-{mode}-{width}");
            check($"content form create action remains available {mode} {width}",view.CreateButton.IsVisible && view.CreateButton.IsEnabled);
        }
        theme.SetMode("light");theme.Apply(false);Invoke(view.CreateButton);await Layout();await model.WaitForIdleAsync();await Layout();
        var client=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines/26.04/7z.dll"));
        var listed=await client.ListAsync(model.CompressionOutput);
        check("content UI creation uses the preview exclusion policy",listed.IsSuccess && listed.Items.Where(row=>!row.Directory).Select(row=>row.Path.Replace('\\','/')).SequenceEqual(["source/keep.txt"]));
        model.Page=3;model.CompressionOutput=Path.Combine(data,"changed.zip");
        check("content preview summary invalidates after output changes",model.CompressionContentSummary==UiText.Format("Compression.RuleCount",2));
        model.CompressionExcludedDirectories="";model.CompressionExcludedExtensions="";
        check("content default rules restore include-all state",model.CompressionContentSummary=="" && model.CompressionExclusionError=="");
    }
}
