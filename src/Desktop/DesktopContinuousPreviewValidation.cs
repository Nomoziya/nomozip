using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopContinuousPreviewValidation
{
    internal static async Task RunAsync(MainWindow window,ThemeResources theme,string root,string data,string baseDirectory,
        Action<string,bool> check,Action<Window,string> capture)
    {
        data=Path.GetFullPath(Path.Combine(data,"continuous-preview"));Directory.CreateDirectory(data);
        Task Layout(Window target)=>target.Dispatcher.InvokeAsync(target.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        var a=Path.Combine(data,"a.txt");var b=Path.Combine(data,"b.txt");
        await File.WriteAllTextAsync(a,"Alpha one\nalpha two\nlast line");await File.WriteAllTextAsync(b,"second entry");
        var image=Path.Combine(data,"c.png");var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(32,16,96,96,System.Windows.Media.PixelFormats.Bgra32,null,Enumerable.Repeat((byte)180,32*16*4).ToArray(),32*4)));
        using(var imageFile=File.Create(image))encoder.Save(imageFile);
        var archive=Path.Combine(data,"preview.7z");
        var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines/26.04/7z.dll"));
        var created=await client.CreateAsync(new([new(a,"a.txt",false),new(b,"b.txt",false),new(image,"c.png",false),new(b,"skip.exe",false)],"7z",5,true),archive,"continuous-secret");
        check("continuous encrypted fixture created",created.IsSuccess);
        var vault=new DpapiPasswordVault(Path.Combine(data,"synthetic.apv"));await vault.SaveAsync(new(Guid.NewGuid(),"synthetic","continuous-secret","",true,DateTimeOffset.UtcNow));
        var vaultBefore=await File.ReadAllBytesAsync(Path.Combine(data,"synthetic.apv"));var archiveBefore=await File.ReadAllBytesAsync(archive);
        var previewClosed=new TaskCompletionSource();Exception? failure=null;ArchiveContentsViewModel? browser=null;
        var dialogs=new WindowDialogs(window,prepareDialog:_=>throw new Exception("saved preview password should work"),prepareContents:dialog=>
        {
            dialog.Opacity=0;dialog.ShowActivated=false;dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
            {
                try
                {
                    browser=(ArchiveContentsViewModel)dialog.DataContext;browser.Rows.Single(row=>row.Path=="skip.exe").Selected=true;
                    browser.CurrentRow=browser.Rows.Single(row=>row.Path=="a.txt");await Layout(dialog);
                    Invoke(dialog.PreviewEntryButton);await previewClosed.Task;await browser.WaitForIdleAsync();
                    check("continuous preview preserves extraction checkboxes",browser.Rows.Single(row=>row.Path=="skip.exe").Selected && !browser.IsWorking);
                }
                catch(Exception error){failure=error;}
                finally{dialog.Close();}
            }));
        },preparePreview:dialog=>
        {
            dialog.Opacity=0;dialog.ShowActivated=false;dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
            {
                try
                {
                    var model=(ArchivePreviewViewModel)dialog.DataContext;
                    check("continuous preview uses actual supported browser sequence",model.HasNavigation && model.EntryPath=="a.txt" && model.NavigationPosition=="1 / 3" && !model.PreviousPreviewCommand.CanExecute(null));
                    Invoke(dialog.ShowFindButton);model.FindText="ALPHA";await Layout(dialog);
                    check("text find selects literal case-insensitive first match",dialog.PreviewText.SelectionStart==0 && dialog.PreviewText.SelectionLength==5);
                    model.FindNextCommand.Execute(null);await Layout(dialog);check("text find advances",dialog.PreviewText.SelectionStart==10);
                    model.FindNextCommand.Execute(null);await Layout(dialog);check("text find wraps",dialog.PreviewText.SelectionStart==0);
                    model.FindPreviousCommand.Execute(null);await Layout(dialog);check("text find reverses and wraps",dialog.PreviewText.SelectionStart==10);
                    model.FindText="[missing]";check("text find reports no match",model.FindStatus==UiText.Get("Preview.FindNoMatch"));
                    model.FindText="alpha";
                    foreach(var mode in new[]{"light","dark","high-contrast"})
                    {
                        theme.SetMode(mode=="dark"?"dark":"light");theme.Apply(mode=="high-contrast");dialog.Width=640;dialog.Height=480;await Layout(dialog);capture(dialog,"next-continuous-preview-"+mode);
                        check("continuous find controls fit "+mode,dialog.PreviewText.ActualHeight>80 && dialog.PreviewFindText.ActualWidth>100 && dialog.NextPreviewButton.IsVisible);
                    }
                    Invoke(dialog.NextPreviewButton);await Layout(dialog);await model.WaitForNavigationAsync();await Layout(dialog);
                    check($"next reads encrypted file and updates browser highlight (path={model.EntryPath}, loading={model.IsLoading}, error={model.HasError}, textMatches={model.Text=="second entry"}, browser={browser?.CurrentRow?.Path})",model.EntryPath=="b.txt" && model.Text=="second entry" && browser?.CurrentRow?.Path=="b.txt");
                    Invoke(dialog.NextPreviewButton);await Layout(dialog);await model.WaitForNavigationAsync();await Layout(dialog);
                    check("continuous image navigation replaces text and hides find",model.IsImage && model.Picture?.PixelWidth==32 && model.Text=="" && !model.ShowFind && !model.NextPreviewCommand.CanExecute(null));
                    capture(dialog,"next-continuous-image");
                    Invoke(dialog.PreviousPreviewButton);await Layout(dialog);await model.WaitForNavigationAsync();await Layout(dialog);
                    check("continuous image to text clears bitmap",model.IsText && model.Picture is null && model.Text=="second entry");
                    Invoke(dialog.PreviousPreviewButton);await Layout(dialog);await model.WaitForNavigationAsync();await Layout(dialog);
                    check("previous restores text and search",model.EntryPath=="a.txt" && model.Text.StartsWith("Alpha") && model.FindText=="alpha" && dialog.PreviewText.SelectionLength==5);
                    File.Move(archive,archive+".hold");
                    try
                    {
                        Invoke(dialog.NextPreviewButton);await Layout(dialog);await model.WaitForNavigationAsync();await Layout(dialog);
                        check("failed continuous read clears prior content and permits recovery",model.HasError && model.Text=="" && model.Picture is null && model.PreviousPreviewCommand.CanExecute(null));
                    }
                    finally {File.Move(archive+".hold",archive);}
                    Invoke(dialog.PreviousPreviewButton);await Layout(dialog);await model.WaitForNavigationAsync();await Layout(dialog);
                    check("continuous preview recovers after read error",model.IsText && model.Text.StartsWith("Alpha"));
                    check("preview shortcuts remain on embeddable content",dialog.PreviewLayout.InputBindings.OfType<System.Windows.Input.KeyBinding>().Any(binding=>binding.Key==System.Windows.Input.Key.PageDown));
                }
                catch(Exception error){failure=error;}
                finally{dialog.Close();previewClosed.TrySetResult();}
            }));
        });
        var main=new MainViewModel(baseDirectory,data,vault,dialogs);await main.InitializeAsync();window.DataContext=main;main.SelectedEngineVersion="26.04";main.ApplyEngineCommand.Execute(null);await main.WaitForIdleAsync();main.AddFiles([archive]);main.SelectedTask=main.Tasks.Single();main.BrowseArchiveCommand.Execute(null);await main.WaitForIdleAsync();
        if(failure is not null)throw failure;
        check("continuous preview leaves archive and vault unchanged",Enumerable.SequenceEqual(vaultBefore,await File.ReadAllBytesAsync(Path.Combine(data,"synthetic.apv"))) && Enumerable.SequenceEqual(archiveBefore,await File.ReadAllBytesAsync(archive)));
        var first=new ArchiveEntry(0,"first.txt",5,false,false);var second=new ArchiveEntry(1,"second.txt",6,false,false);
        var pending=await ArchivePreviewViewModel.CreateAsync(first,Encoding.UTF8.GetBytes("first"));var entered=new TaskCompletionSource();var wasCancelled=false;
        pending.ConfigureNavigation([first,second],first,async(entry,token)=>{entered.SetResult();try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){wasCancelled=true;throw;}return ArchivePreviewViewModel.Error(entry,"late");});
        pending.NextPreviewCommand.Execute(null);await entered.Task;
        check("pending continuous read disables repeated navigation",!pending.NextPreviewCommand.CanExecute(null) && !pending.PreviousPreviewCommand.CanExecute(null));
        pending.Clear();await pending.WaitForNavigationAsync();
        check("closing continuous preview cancels pending read and prevents late publication",wasCancelled && pending.Text=="" && pending.Message=="" && pending.Picture is null && !pending.CanInteract);
        IReadOnlyList<ArchiveEntry>? capturedSequence=null;
        var filtered=new ArchiveContentsViewModel("synthetic.zip",new[]{"alpha/a.txt","alpha/z.txt","beta/b.txt","alpha/skip.exe"}.Select((name,index)=>new ArchiveEntry((uint)index,name,1,false,false)).ToArray(),
            previewSequence:(_,_,entries,_)=>{capturedSequence=entries;return Task.CompletedTask;});
        filtered.Navigate("alpha");filtered.SearchScopeIndex=1;filtered.Search=".txt";
        var sorted=System.Windows.Data.CollectionViewSource.GetDefaultView(filtered.VisibleRows);
        sorted.SortDescriptions.Add(new(nameof(ArchiveContentRow.Path),System.ComponentModel.ListSortDirection.Descending));
        filtered.CurrentRow=filtered.VisibleRows.First();filtered.PreviewCommand.Execute(null);await filtered.WaitForIdleAsync();
        check("preview sequence follows current subtree filter and actual descending view sort",capturedSequence is not null && capturedSequence.Select(entry=>entry.Path).SequenceEqual(new[]{"alpha/z.txt","alpha/a.txt"}));
        theme.SetMode("light");theme.Apply(false);
    }
}
