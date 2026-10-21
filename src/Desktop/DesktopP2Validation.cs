using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopP2Validation
{
    internal static async Task RunAsync(MainWindow window,ThemeResources theme,string data,string baseDirectory,
        Action<string,bool> check,Action<Window,string> capture)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Isolated CI only.");
        data=Path.Combine(data,"p2");Directory.CreateDirectory(data);var original=window.DataContext;
        Task Layout(Window target)=>target.Dispatcher.InvokeAsync(target.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        async Task Execute(Button button,MainViewModel model)
        {
            await Layout(window);
            for(var attempt=0;attempt<40 && !button.IsEnabled;attempt++){await Task.Delay(25);await Layout(window);}
            if(!button.IsEnabled)throw new Exception("P2 command disabled: "+button.Name);
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
            await Layout(window);await model.WaitForIdleAsync();await Layout(window);
        }
        bool Fits(FrameworkElement item)
        {var bounds=item.TransformToAncestor(window).TransformBounds(new Rect(item.RenderSize));return item.IsVisible && bounds.Top>=0 && bounds.Left>=0 && bounds.Right<=window.ActualWidth+1 && bounds.Bottom<=window.ActualHeight+1;}
        try
        {
            var archivePath=Path.Combine(data,"preferences.zip");
            using(var archive=ZipFile.Open(archivePath,ZipArchiveMode.Create))
            {using(var writer=new StreamWriter(archive.CreateEntry("small.txt").Open()))writer.Write("a");using(var writer=new StreamWriter(archive.CreateEntry("large.txt").Open()))writer.Write(new string('b',120));}
            var profile=Path.Combine(data,"browser-state");var vault=new DpapiPasswordVault(Path.Combine(profile,"synthetic.apv"));
            var pass=0;double rememberedWidth=0;Exception? dialogFailure=null;
            var dialogs=new WindowDialogs(window,prepareContents:dialog=>
            {
                dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;dialog.Width=1152;dialog.Height=768;
                dialog.Loaded+=async(_,_)=>
                {
                    try
                    {
                        await Layout(dialog);var contents=(ArchiveContentsViewModel)dialog.DataContext;
                        if(pass++==0)
                        {
                            check("P2 old settings default to list and no saved sort",!contents.GridView && contents.BrowserPreferences.Sorts is null);
                            var headers=Descendants(dialog.EntryTable).OfType<DataGridColumnHeadersPresenter>().Single();
                            var sizePeer=UIElementAutomationPeer.CreatePeerForElement(headers)!.GetChildren().OfType<DataGridColumnHeaderItemAutomationPeer>()
                                .Single(peer=>ReferenceEquals(peer.Item,dialog.EntryTable.Columns[2].Header));
                            var invoke=(IInvokeProvider)sizePeer.GetPattern(PatternInterface.Invoke);invoke.Invoke();await Layout(dialog);invoke.Invoke();await Layout(dialog);
                            check("P2 actual size header stores descending sort",contents.BrowserPreferences.Sorts is [{Member:"Entry.Size",Direction:ListSortDirection.Descending}]);
                            var sizeHeader=Descendants(dialog.EntryTable).OfType<DataGridColumnHeader>().Single(header=>header.Column==dialog.EntryTable.Columns[2]);
                            var grip=(Thumb)sizeHeader.Template.FindName("PART_RightHeaderGripper",sizeHeader);var before=dialog.EntryTable.Columns[2].ActualWidth;
                            grip.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
                            grip.RaiseEvent(new DragDeltaEventArgs(52,0){RoutedEvent=Thumb.DragDeltaEvent});await Layout(dialog);
                            grip.RaiseEvent(new DragCompletedEventArgs(52,0,false){RoutedEvent=Thumb.DragCompletedEvent});await Layout(dialog);
                            rememberedWidth=dialog.EntryTable.Columns[2].ActualWidth;
                            check("P2 actual header drag records width",rememberedWidth>before+40 && contents.BrowserPreferences.Columns?.Any(column=>column.Column==2 && Math.Abs(column.Width-rememberedWidth)<1)==true);
                            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(dialog.GridViewAction)!.GetPattern(PatternInterface.Toggle)).Toggle();await Layout(dialog);
                            check("P2 accessible grid toggle changes actual view",contents.GridView && dialog.EntryTiles.IsVisible && dialog.GridViewAction.IsChecked==true && dialog.ListViewAction.IsChecked==false);
                            contents.Search="large";contents.VisibleRows.Single().Selected=true;
                            capture(dialog,"p2-browser-customized");
                        }
                        else
                        {
                            check("P2 restart restores grid view and sort",contents.GridView && contents.BrowserPreferences.Sorts is [{Member:"Entry.Size",Direction:ListSortDirection.Descending}]
                                && ((ListCollectionView)CollectionViewSource.GetDefaultView(contents.VisibleRows)).CustomSort is ArchiveContentComparer);
                            check("P2 restart restores custom column width",Math.Abs(dialog.EntryTable.Columns[2].Width.Value-rememberedWidth)<1);
                            check("P2 preferences never restore search or selection",contents.Search=="" && contents.SelectedEntries.Count==0 && contents.VisibleRows.Count==2);
                            dialog.EditContentsMenu.PlacementTarget=dialog.EditContentsButton;dialog.EditContentsMenu.IsOpen=true;await Layout(dialog);
                            ((IInvokeProvider)new MenuItemAutomationPeer(dialog.ResetBrowserLayoutAction).GetPattern(PatternInterface.Invoke)).Invoke();await Layout(dialog);
                            dialog.EditContentsMenu.IsOpen=false;await Layout(dialog);
                            check("P2 actual reset restores list automatic widths and original ordering",!contents.GridView && dialog.EntryTable.IsVisible && contents.BrowserPreferences.Columns is null
                                && contents.BrowserPreferences.Sorts is null && ((ListCollectionView)CollectionViewSource.GetDefaultView(contents.VisibleRows)).CustomSort is null);
                            capture(dialog,"p2-browser-reset");
                        }
                    }
                    catch(Exception failure){dialogFailure=failure;}
                    finally{dialog.Close();}
                };
            });
            async Task<MainViewModel> BrowserModel()
            {var model=new MainViewModel(baseDirectory,profile,vault,dialogs);await model.InitializeAsync();model.AddFiles([archivePath]);model.SelectedTask=model.Tasks.Single();window.DataContext=model;return model;}
            var browser=await BrowserModel();browser.BrowseArchiveCommand.Execute(null);await browser.WaitForIdleAsync();if(dialogFailure is not null)throw dialogFailure;
            var settingsPath=Path.Combine(profile,"settings.json");var settings=JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!;
            check("P2 persisted preferences contain presentation fields only",settings["BrowserPreferences"]!.AsObject().Select(item=>item.Key).Order().SequenceEqual(new[]{"Columns","GridView","Sorts"}));
            browser=await BrowserModel();browser.BrowseArchiveCommand.Execute(null);await browser.WaitForIdleAsync();if(dialogFailure is not null)throw dialogFailure;
            browser=await BrowserModel();check("P2 reset survives a third model restart",!browser.BrowserPreferences.GridView && browser.BrowserPreferences.Sorts is null && browser.BrowserPreferences.Columns is null);
            settings=JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!;settings.AsObject().Remove("BrowserPreferences");await File.WriteAllTextAsync(settingsPath,settings.ToJsonString());
            browser=await BrowserModel();check("P2 settings without optional preferences remain compatible",!browser.BrowserPreferences.GridView);

            var inputs=Path.Combine(data,"inputs");Directory.CreateDirectory(inputs);
            var first=Path.Combine(inputs,"a","notes.txt");var second=Path.Combine(inputs,"b","notes.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(first)!);Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            await File.WriteAllTextAsync(first,"first source");await File.WriteAllTextAsync(second,"second source");
            var folder=Path.Combine(inputs,"folder.v1");Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"inside.txt"),"folder source");
            var passwordPrompts=0;
            async Task<MainViewModel> CompressionModel(string name,bool save=false)
            {
                var state=Path.Combine(data,name);var passwordVault=new DpapiPasswordVault(Path.Combine(state,"synthetic.apv"));
                var model=new MainViewModel(baseDirectory,state,passwordVault,new WindowDialogs(window,prepareDialog:dialog=>
                {
                    dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;
                    dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                    {passwordPrompts++;dialog.SecretBox.Password="Batch-test-123";dialog.ConfirmationBox.Password=dialog.SecretBox.Password;dialog.SavePassword.IsChecked=save;dialog.NameBox.Text="Batch fixture";dialog.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));});
                }));
                await model.InitializeAsync();model.SelectedEngineVersion="26.04";model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();
                model.CompressionOutput=Path.Combine(data,"outputs",name,"combined.7z");model.Page=3;window.DataContext=model;await Layout(window);return model;
            }
            var model=await CompressionModel("separate");model.AddCompressionSources([first,second,folder]);model.CompressionFormat=1;
            var view=window.CompressionPage;view.SeparateCompressionBox.BringIntoView();await Layout(window);
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(view.SeparateCompressionBox)!.GetPattern(PatternInterface.Toggle)).Toggle();await Layout(window);
            check("P2 actual separate checkbox updates scope name and primary action",model.CompressionSeparate && !view.ArchiveNameBox.IsEnabled && view.ArchiveNameBox.Text==UiText.Get("Compression.AutomaticNames")
                && (string)view.CreateButton.Content==UiText.Format("Compression.SeparateCreate",3));
            Directory.CreateDirectory(model.CompressionOutputDirectory);var keep=Path.Combine(model.CompressionOutputDirectory,"notes.zip");await File.WriteAllTextAsync(keep,"keep existing");
            foreach(var mode in new[]{"light","dark","contrast"})foreach(var width in new[]{1152d,800d})
            {
                theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");window.MinWidth=width;window.Width=width;window.Height=width==800?600:768;
                view.SeparateCompressionBox.BringIntoView();await Layout(window);
                check($"P2 separate option and primary action reachable {mode} {width}",Fits(view.SeparateCompressionBox) && Fits(view.CreateButton));
                capture(window,$"p2-separate-{mode}-{width}");
            }
            await Execute(view.CreateButton,model);
            check("P2 separate compression creates one successful task per source",model.WorkspaceTasks.Count==3 && model.WorkspaceTasks.All(row=>row.IsCompression && row.Completed));
            foreach(var (name,content) in new[]{("notes (2).zip","first source"),("notes (3).zip","second source")})
            {
                using var archive=ZipFile.OpenRead(Path.Combine(model.CompressionOutputDirectory,name));using var reader=new StreamReader(archive.GetEntry("notes.txt")!.Open());
                check("P2 independent source bytes "+name,archive.Entries.Count==1 && await reader.ReadToEndAsync()==content);
            }
            using(var archive=ZipFile.OpenRead(Path.Combine(model.CompressionOutputDirectory,"folder.v1.zip")))
            {using var reader=new StreamReader(archive.GetEntry("folder.v1/inside.txt")!.Open());check("P2 directory output retains its own contents",await reader.ReadToEndAsync()=="folder source");}
            check("P2 separate compression preserves existing outputs and sources",await File.ReadAllTextAsync(keep)=="keep existing" && await File.ReadAllTextAsync(first)=="first source" && await File.ReadAllTextAsync(second)=="second source");
            capture(window,"p2-separate-results");

            model=await CompressionModel("failure");var missing=Path.Combine(inputs,"missing.txt");await File.WriteAllTextAsync(missing,"remove before creation");
            model.AddCompressionSources([missing,first]);model.CompressionFormat=1;model.CompressionSeparate=true;File.Delete(missing);await Execute(view.CreateButton,model);
            check("P2 failed source retains failure and later source completes",model.WorkspaceTasks.Count==2 && model.WorkspaceTasks[0].Failure is not null && model.WorkspaceTasks[1].Completed);

            model=await CompressionModel("cancel");model.AddCompressionSources([first,second]);model.CompressionFormat=1;model.CompressionSeparate=true;
            model.WorkspaceTasks.CollectionChanged+=(_,args)=>
            {if(args.NewItems is not null)foreach(ArchiveTaskRow row in args.NewItems)row.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(row.Completed) && row.Completed)model.CancelCommand.Execute(null);};};
            await Execute(view.CreateButton,model);
            check("P2 cancellation keeps committed output and stops later sources",model.WorkspaceTasks.Count==1 && model.WorkspaceTasks[0].Completed
                && File.Exists(Path.Combine(model.CompressionOutputDirectory,"notes.zip")) && !File.Exists(Path.Combine(model.CompressionOutputDirectory,"notes (2).zip"))
                && model.CompressionMessage==UiText.Format("Compression.SeparateCancelled",1,2,0));

            model=await CompressionModel("encrypted",true);model.AddCompressionSources([first,second]);model.CompressionSeparate=true;model.CompressionEncrypt=true;model.CompressionSfx=true;
            await Execute(view.CreateButton,model);
            var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines/26.04/7z.dll"));
            check("P2 encrypted SFX batch prompts once and all tasks complete",model.CompressionSfx && passwordPrompts==1 && model.WorkspaceTasks.Count==2 && model.WorkspaceTasks.All(row=>row.Completed)
                && Directory.GetFiles(model.CompressionOutputDirectory,"*.exe").Length==2);
            foreach(var output in Directory.GetFiles(model.CompressionOutputDirectory,"*.exe"))check("P2 encrypted SFX validates without execution "+Path.GetFileName(output),(await client.TestAsync(output,"Batch-test-123")).IsSuccess && !(await client.TestAsync(output)).IsSuccess);
            var saved=await new DpapiPasswordVault(Path.Combine(data,"encrypted","synthetic.apv")).ReadAsync();
            check("P2 one saved password reused across independent outputs",saved.Count==1 && model.CompressionPasswordStatus==UiText.Get("T_5d8111695438"));

            model=await CompressionModel("single-files");model.AddCompressionSources([first,second]);model.CompressionFormat=3;model.CompressionSeparate=true;await Execute(view.CreateButton,model);
            check("P2 single-file format permits multiple independent file outputs",model.WorkspaceTasks.Count==2 && model.WorkspaceTasks.All(row=>row.Completed) && Directory.GetFiles(model.CompressionOutputDirectory,"*.gz").Length==2);
            model.Page=3;model.AddCompressionSources([folder]);await Layout(window);
            check("P2 single-file format still rejects directories",model.CompressionSourceError==UiText.Get("Compression.SeparateFilesOnly") && !model.CreateArchiveCommand.CanExecute(null));
            model.CompressionFormat=1;model.CompressionOutputDirectory=folder;await Layout(window);
            check("P2 output inside source is explained and creation disabled",model.CompressionSourceError==UiText.Get("Compression.SeparateOutsideSources") && !model.CreateArchiveCommand.CanExecute(null));

            model=await CompressionModel("volumes");model.AddCompressionSources([first,second]);model.CompressionFormat=1;model.CompressionSeparate=true;model.CompressionVolumeCustom="0.001";model.CompressionVolumeOption=4;
            await Execute(view.CreateButton,model);
            check("P2 separate split archives produce independent volume sets",model.WorkspaceTasks.Count==2 && model.WorkspaceTasks.All(row=>row.Completed)
                && File.Exists(Path.Combine(model.CompressionOutputDirectory,"notes.zip.001")) && File.Exists(Path.Combine(model.CompressionOutputDirectory,"notes (2).zip.001")));
        }
        finally {window.DataContext=original;window.MinWidth=1152;window.Width=1152;window.Height=768;theme.SetMode("light");theme.Apply(false);}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
}
