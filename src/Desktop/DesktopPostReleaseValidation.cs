using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopPostReleaseValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, ThemeResources theme, string root, string baseDirectory)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Requires isolated CI.");
        var language = UiText.LanguageCode;
        var folder = Path.Combine(root,"artifacts/post032-preview",language); Directory.CreateDirectory(folder);
        var data = Path.Combine(root,"artifacts/post032-ui",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        var checks = new Dictionary<string,bool>(); string? error = null;
        var captures = new List<object>();
        var performance = new List<object>();
        var descendingPerformance = new List<object>();
        var gridPerformance = new List<object>();
        void Check(string name, bool passed) { checks.Add(name,passed); if(!passed)throw new Exception(name); }
        Task Layout(Window target) => target.Dispatcher.InvokeAsync(target.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        void Toggle(CheckBox box) => ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(box)!.GetPattern(PatternInterface.Toggle)).Toggle();
        bool Fits(FrameworkElement element, Window target)
        { var rect=element.TransformToAncestor(target).TransformBounds(new Rect(element.RenderSize));return element.IsVisible && rect.Left>=0 && rect.Top>=0 && rect.Right<=target.ActualWidth+1 && rect.Bottom<=target.ActualHeight+1; }
        void Capture(Window target,string name)
        {
            target.UpdateLayout();target.Opacity=1;
            DesktopAccessibilityValidation.Render(target,Path.Combine(folder,name+".png"),1,clientOnly:true);target.Opacity=0;
            captures.Add(new {name,width=target.ActualWidth,height=target.ActualHeight});
        }
        var vault=new DpapiPasswordVault(Path.Combine(data,"synthetic.apv"));
        var password=new SavedPassword(Guid.NewGuid(),"Synthetic folder rule","synthetic-only","",true,DateTimeOffset.UtcNow,MatchDirectory:Path.Combine(data,"source"));
        ArchiveContentsDialog? browser=null;
        try
        {
            await vault.SaveAsync(password);
            var model=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window));window.DataContext=model;await model.InitializeAsync();
            model.Page=2;model.SettingsSection=4;await Layout(window);
            Toggle(window.AutoCleanApplicationInstallersBox);Invoke(window.SaveApplicationUpdateSettingsButton);await Layout(window);await model.WaitForIdleAsync();
            var restarted=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window));await restarted.InitializeAsync();
            Check("auto cleanup switch persists after restart",!restarted.AutoCleanApplicationInstallers);
            var cache=Path.Combine(data,"application-updates");Directory.CreateDirectory(cache);var stale=Path.Combine(cache,Guid.NewGuid().ToString("N")+".exe");await File.WriteAllTextAsync(stale,"old installer");
            await new ApplicationUpdateReceipt("0.3.1",model.ApplicationVersion,baseDirectory).SaveAsync(cache);
            await restarted.InitializeAsync();Check("disabled startup cleanup keeps installer",File.Exists(stale));
            Toggle(window.AutoCleanApplicationInstallersBox);Invoke(window.SaveApplicationUpdateSettingsButton);await Layout(window);await model.WaitForIdleAsync();
            await new ApplicationUpdateReceipt("0.3.1",model.ApplicationVersion,baseDirectory).SaveAsync(cache);
            var upgraded=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window));await upgraded.InitializeAsync();
            Check("completed initialization consumes upgrade and clears old installer",!File.Exists(stale) && !File.Exists(Path.Combine(cache,"pending-update.json")) && upgraded.ApplicationUpdateMessage.Contains("1"));
            foreach(var mode in new[]{"light","dark"})foreach(var width in new[]{1152d,800d})
            {
                theme.SetMode(mode);theme.Apply(false);window.MinWidth=width;window.Width=width;window.Height=width==800?600:768;await Layout(window);
                window.AutoCleanApplicationInstallersBox.BringIntoView();await Layout(window);
                Check($"auto cleanup renders its label {mode} {width}",Descendants(window.AutoCleanApplicationInstallersBox).OfType<AccessText>().Any(text=>text.Text==UiText.Get("SoftwareUpdate.AutoClean")));
                Check($"auto cleanup control fits {mode} {width}",Fits(window.AutoCleanApplicationInstallersBox,window) && Fits(window.SaveApplicationUpdateSettingsButton,window));
                Check($"auto cleanup has keyboard and accessible name {mode} {width}",window.AutoCleanApplicationInstallersBox.Focusable && UIElementAutomationPeer.CreatePeerForElement(window.AutoCleanApplicationInstallersBox)!.GetName()==UiText.Get("SoftwareUpdate.AutoClean"));
                Capture(window,$"updates-{mode}-{width}");
                model.Page=1;model.SelectedPassword=model.PasswordRows.Single();await Layout(window);window.VaultDetails.RulesExpander.IsExpanded=true;
                window.VaultDetails.DirectoryMatchBox.BringIntoView();await Layout(window);
                Check($"directory rule reachable {mode} {width}",Fits(window.VaultDetails.DirectoryMatchBox,window) && window.VaultDetails.DirectoryMatchBox.Text==password.MatchDirectory);
                Capture(window,$"directory-{mode}-{width}");model.Page=2;model.SettingsSection=4;
            }
            model.Page=1;model.SelectedPassword=model.PasswordRows.Single();await Layout(window);
            window.VaultDetails.DirectoryMatchBox.Text="relative";Invoke(window.VaultDetails.SaveButton);await Layout(window);
            Check("invalid directory is visible and not persisted",window.VaultDetails.Error.Text==UiText.Get("Password.DirectoryInvalid") && (await vault.ReadAsync()).Single().MatchDirectory==password.MatchDirectory);
            window.VaultDetails.DirectoryMatchBox.Text=Path.Combine(data,"changed-source");Invoke(window.VaultDetails.SaveButton);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
            Check("actual detail save persists source rule",(await vault.ReadAsync()).Single().MatchDirectory==Path.Combine(data,"changed-source"));

            var archive=Path.Combine(data,"content.zip");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
                foreach(var name in new[]{"folder/a.txt","folder/b.jpg","other/c.txt"}) {using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(name);}
            var dll=Directory.EnumerateFiles(Path.Combine(baseDirectory,"engines"),"7z.dll",SearchOption.AllDirectories).First();
            var engine=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),dll);
            var inventory=await engine.ListAsync(archive);Check("real archive inventory available",inventory.IsSuccess && inventory.Items.Count==3);
            var output=Path.Combine(data,"output");EngineResult? selectionResult=null;
            var view=new ArchiveContentsViewModel(archive,inventory.Items,async(selected,progress)=>
            {
                var result=await engine.ExtractSelectionAsync(archive,output,selected,null,new Dictionary<uint,string>());selectionResult=result;
                return result.IsSuccess?output:null;
            });
            browser=new ArchiveContentsDialog(view){Owner=window,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000,Opacity=0};browser.Show();
            browser.AdvancedFilters.IsExpanded=true;await Layout(browser);
            view.Search="b.jpg";view.Rows.Single().Selected=true;
            browser.FilterExtensionsBox.Text="txt";browser.FilterExtensionsBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();view.Search="";await Layout(browser);
            Check("filter textbox uses real binding",view.FilterExtensions=="txt" && view.VisibleRows.Count==2);
            Check("hidden checked entry retained with accurate count",view.SelectedEntries.Single().Path.Replace('\\','/')=="folder/b.jpg" && view.CompactSelectionSummary.Contains("1"));
            Check("hidden selection is explicitly counted",view.CompactSelectionSummary==UiText.Format("Browser.HiddenSelection",1,1));
            Toggle(browser.SelectedOnlyBox);await Layout(browser);
            Check("selected only reveals hidden entries without changing extraction scope",view.ShowSelectedOnly && view.FilterExtensions=="" && view.VisibleRows.Single().Path.Replace('\\','/')=="folder/b.jpg" && view.SelectedEntries.Count==1);
            Toggle(browser.SelectedOnlyBox);view.FilterExtensions="txt";await Layout(browser);
            Invoke(browser.ExtractSelectedButton);await Layout(browser);await view.WaitForIdleAsync();await Layout(browser);
            Check("actual hidden selection engine result "+selectionResult?.Code,selectionResult?.IsSuccess==true);
            Check("filtered selection extracts actual hidden entry only",Directory.Exists(output) && Directory.GetFiles(output,"*",SearchOption.AllDirectories).Length==1 && File.ReadAllText(Path.Combine(output,"folder/b.jpg"))=="folder/b.jpg");
            browser.FilterMinimumBox.Text="2";browser.FilterMinimumBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            browser.FilterMaximumBox.Text="1";browser.FilterMaximumBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();await Layout(browser);
            Check("invalid range reports error and hides results",view.FilterError==UiText.Get("Browser.FilterError") && view.VisibleRows.Count==0);
            Invoke(browser.ClearFiltersButton);await Layout(browser);Check("clear resets all filters preserving checks",view.FilterError=="" && view.FilterExtensions=="" && view.FilterKindIndex==0 && view.SelectedEntries.Count==1);
            view.FilterKindIndex=2;Check("folders only includes virtual folders",view.VisibleRows.Count==2 && view.VisibleRows.All(row=>row.Entry.Directory));
            view.Navigate("folder");Check("folder navigation clears global filters",view.CurrentFolder=="folder" && view.FilterKindIndex==0 && view.VisibleRows.Count==2);
            view.FilterExtensions="txt";
            var collection=CollectionViewSource.GetDefaultView(view.VisibleRows);collection.SortDescriptions.Add(new(nameof(ArchiveContentRow.Path),System.ComponentModel.ListSortDirection.Descending));
            view.Search="a.txt";view.Search="";Check("collection reset preserves user sorting",collection.SortDescriptions.Count==1 && view.VisibleRows.Count==2);
            foreach(var mode in new[]{"light","dark","contrast"})foreach(var width in new[]{1152d,800d})
            {
                theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");browser.MinWidth=width;browser.Width=width;browser.Height=width==800?600:768;await Layout(browser);
                Check($"filter controls fit {mode} {width}",Fits(browser.FilterExtensionsBox,browser) && Fits(browser.FilterMinimumBox,browser) && Fits(browser.FilterMaximumBox,browser) && Fits(browser.FilterKindBox,browser) && Fits(browser.ClearFiltersButton,browser));
                Check($"filter focus and name {mode} {width}",UIElementAutomationPeer.CreatePeerForElement(browser.FilterExtensionsBox)!.GetName()==UiText.Get("Browser.FilterType") && browser.FilterExtensionsBox.Focusable);
                Capture(browser,$"filters-{mode}-{width}");
            }
            browser.Close();browser=null;
            await DesktopUsabilityValidation.RunAsync(window,theme,data,baseDirectory,Check,Capture);
            await DesktopP2Validation.RunAsync(window,theme,root,data,baseDirectory,Check,Capture);
            await DesktopNextUsabilityValidation.RunAsync(window,theme,data,Check,Capture);
            theme.SetMode("light");theme.Apply(false);
            foreach(var count in new[]{10000,100000})
            {
                var entries=Enumerable.Range(0,count).Select(i=>new ArchiveEntry((uint)i,$"folder-{i%100:D3}/file-{i:D6}.txt",(ulong)i,false,false)).ToArray();
                var gaps=new List<double>();var phaseGaps=new List<object>();var phase="model construction";var previous=System.Diagnostics.Stopwatch.GetTimestamp();
                var operationStarts=new Dictionary<DispatcherOperation,(long start,string phase)>();var operations=new List<object>();var resets=new List<object>();var inputGaps=new List<double>();var inputPrevious=previous;var maxOperationMs=0d;
                void Started(object? sender,DispatcherHookEventArgs args)=>operationStarts[args.Operation]=(System.Diagnostics.Stopwatch.GetTimestamp(),phase);
                void Finished(object? sender,DispatcherHookEventArgs args){if(operationStarts.Remove(args.Operation,out var start)){var ms=System.Diagnostics.Stopwatch.GetElapsedTime(start.start).TotalMilliseconds;maxOperationMs=Math.Max(maxOperationMs,ms);if(ms>10)operations.Add(new{phase=start.phase,durationMs=ms,priority=args.Operation.Priority.ToString()});}}
                window.Dispatcher.Hooks.OperationStarted+=Started;window.Dispatcher.Hooks.OperationCompleted+=Finished;
                var inputHeartbeat=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(16)};
                inputHeartbeat.Tick+=(_,_)=>{var now=System.Diagnostics.Stopwatch.GetTimestamp();inputGaps.Add(System.Diagnostics.Stopwatch.GetElapsedTime(inputPrevious,now).TotalMilliseconds);inputPrevious=now;};inputHeartbeat.Start();
                var heartbeat=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(16)};
                heartbeat.Tick+=(_,_)=>{var now=System.Diagnostics.Stopwatch.GetTimestamp();var gap=System.Diagnostics.Stopwatch.GetElapsedTime(previous,now).TotalMilliseconds;gaps.Add(gap);phaseGaps.Add(new{phase,gapMs=gap});previous=now;};heartbeat.Start();
                var watch=System.Diagnostics.Stopwatch.StartNew();
                var large=await Task.Run(()=>new ArchiveContentsViewModel(Path.Combine(data,"large-inventory.zip"),entries));
                long resetStarted=0;large.VisibleRows.CollectionChanged+=(_,_)=>resetStarted=System.Diagnostics.Stopwatch.GetTimestamp();
                var constructionMs=watch.Elapsed.TotalMilliseconds;phase="window construction/show/layout";var windowWatch=System.Diagnostics.Stopwatch.StartNew();
                browser=new ArchiveContentsDialog(large){Owner=window,MinWidth=1152,Width=1152,Height=768,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000,Opacity=0};var windowConstructMs=windowWatch.Elapsed.TotalMilliseconds;
                Check($"large browser defers initial rows until shell is loaded {count}",large.IsRefreshing && large.VisibleRows.Count==0);
                browser.Show();var windowShowMs=windowWatch.Elapsed.TotalMilliseconds-windowConstructMs;await Layout(browser);await large.WaitForIdleAsync();await Layout(browser);
                var openMs=watch.Elapsed.TotalMilliseconds;
                large.VisibleRows.CollectionChanged+=(_,_)=>resets.Add(new{phase,durationMs=System.Diagnostics.Stopwatch.GetElapsedTime(resetStarted).TotalMilliseconds});
                phase="search";watch.Restart();browser.EntrySearch.Text=".txt";browser.EntrySearch.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();await large.WaitForIdleAsync();await Layout(browser);var searchMs=watch.Elapsed.TotalMilliseconds;
                Check($"large inventory bound search completes {count}",large.VisibleRows.Count==count);
                var realized=Descendants(browser.EntryTable).OfType<DataGridRow>().Count();
                Check($"large list realizes a bounded number of rows {count}",realized>0 && realized<200);
                phase="header sort";watch.Restart();
                var headers=Descendants(browser.EntryTable).OfType<System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter>().Single();
                var headerPeer=UIElementAutomationPeer.CreatePeerForElement(headers)!.GetChildren()
                    .OfType<DataGridColumnHeaderItemAutomationPeer>().Single(peer=>ReferenceEquals(peer.Item,browser.EntryTable.Columns[1].Header));
                ((IInvokeProvider)headerPeer.GetPattern(PatternInterface.Invoke)).Invoke();
                await Layout(browser);await large.WaitForIdleAsync();await Layout(browser);var sortMs=watch.Elapsed.TotalMilliseconds;
                await Task.Delay(40);heartbeat.Stop();inputHeartbeat.Stop();window.Dispatcher.Hooks.OperationStarted-=Started;window.Dispatcher.Hooks.OperationCompleted-=Finished;
                performance.Add(new{count,openMs,constructionMs,windowConstructMs,windowShowMs,searchMs,sortMs,phaseGaps=phaseGaps.ToArray(),operations=operations.ToArray(),resets=resets.ToArray(),maxDispatcherOperationMs=maxOperationMs,maxInputGapMs=inputGaps.DefaultIfEmpty(0).Max(),maxDispatcherGapMs=gaps.DefaultIfEmpty(0).Max(),peakWorkingSet=System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64});
                // The reference sort below is a correctness oracle, not a product operation.
                // Exclude its synchronous CPU work (and screenshot rendering) from UI response measurements.
                Check($"actual column header uses background typed sorting {count}",large.UsesBackgroundView && !large.IsRefreshing && ((ListCollectionView)CollectionViewSource.GetDefaultView(large.VisibleRows)).CustomSort is null
                    && large.BrowserPreferences.Sorts is [{Member:"DisplayName",Direction:System.ComponentModel.ListSortDirection.Ascending}]
                    && large.VisibleRows.Select(row=>row.Path).SequenceEqual(large.VisibleRows.Select(row=>row.Path).OrderBy(path=>path,StringComparer.CurrentCulture)));
                Check($"large list dispatcher continues during background work {count}",gaps.Count>0);
                // Background-priority timers include queue starvation; keep that diagnostic, but measure response at input priority.
                Check($"large list input delay and single UI operation stay below 200ms {count}",inputGaps.Count>0 && inputGaps.Max()<200 && maxOperationMs<200);
                // Exercise a real order change too: the first ascending sort can match the initial inventory order.
                gaps.Clear();inputGaps.Clear();operations.Clear();operationStarts.Clear();resets.Clear();phaseGaps.Clear();maxOperationMs=0;phase="descending header sort";
                previous=System.Diagnostics.Stopwatch.GetTimestamp();inputPrevious=previous;
                window.Dispatcher.Hooks.OperationStarted+=Started;window.Dispatcher.Hooks.OperationCompleted+=Finished;heartbeat.Start();inputHeartbeat.Start();watch.Restart();
                ((IInvokeProvider)headerPeer.GetPattern(PatternInterface.Invoke)).Invoke();await Layout(browser);await large.WaitForIdleAsync();await Layout(browser);var descendingSortMs=watch.Elapsed.TotalMilliseconds;
                await Task.Delay(40);heartbeat.Stop();inputHeartbeat.Stop();window.Dispatcher.Hooks.OperationStarted-=Started;window.Dispatcher.Hooks.OperationCompleted-=Finished;
                descendingPerformance.Add(new{count,sortMs=descendingSortMs,phaseGaps=phaseGaps.ToArray(),operations=operations.ToArray(),resets=resets.ToArray(),maxDispatcherOperationMs=maxOperationMs,maxInputGapMs=inputGaps.DefaultIfEmpty(0).Max(),maxDispatcherGapMs=gaps.DefaultIfEmpty(0).Max()});
                Check($"actual descending header sorts all entries {count}",large.BrowserPreferences.Sorts is [{Member:"DisplayName",Direction:System.ComponentModel.ListSortDirection.Descending}]
                    && large.VisibleRows.Select(row=>row.Path).SequenceEqual(large.VisibleRows.Select(row=>row.Path).OrderByDescending(path=>path,StringComparer.CurrentCulture)));
                Check($"descending sort preserves UI responsiveness {count}",inputGaps.Count>0 && inputGaps.Max()<200 && maxOperationMs<200);
                Capture(browser,$"large-list-{count}");
                if(count==100000)
                {
                    gaps.Clear();inputGaps.Clear();operations.Clear();operationStarts.Clear();resets.Clear();phaseGaps.Clear();maxOperationMs=0;phase="grid viewport and last item";
                    previous=System.Diagnostics.Stopwatch.GetTimestamp();inputPrevious=previous;
                    window.Dispatcher.Hooks.OperationStarted+=Started;window.Dispatcher.Hooks.OperationCompleted+=Finished;heartbeat.Start();inputHeartbeat.Start();watch.Restart();
                    ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(browser.GridViewAction)!.GetPattern(PatternInterface.Toggle)).Toggle();await Layout(browser);
                    Check("large grid realizes a bounded number of tiles",large.GridView && Descendants(browser.EntryTiles).OfType<ListBoxItem>().Count() is >0 and <200);
                    var tileScroller=Descendants(browser.EntryTiles).OfType<ScrollViewer>().Single();
                    var scroll=(IScrollProvider)UIElementAutomationPeer.CreatePeerForElement(tileScroller)!.GetPattern(PatternInterface.Scroll);
                    Check("large grid exposes a real scroll range",scroll.VerticallyScrollable);scroll.SetScrollPercent(-1,100);await Layout(browser);
                    var last=large.VisibleRows.Last();var lastTile=(ListBoxItem)browser.EntryTiles.ItemContainerGenerator.ContainerFromItem(last);
                    Check("scrolling reaches last entry without realizing the whole grid",lastTile is not null && Descendants(browser.EntryTiles).OfType<ListBoxItem>().Count() is >0 and <200);
                    var lastBox=Descendants(lastTile!).OfType<CheckBox>().Single();Toggle(lastBox);await Layout(browser);
                    Check("virtualized tile checkbox selects the real last entry",large.SelectedEntries is [var chosen] && chosen.Index==last.Entry.Index);
                    Toggle(lastBox);await Layout(browser);var gridMs=watch.Elapsed.TotalMilliseconds;await Task.Delay(40);
                    heartbeat.Stop();inputHeartbeat.Stop();window.Dispatcher.Hooks.OperationStarted-=Started;window.Dispatcher.Hooks.OperationCompleted-=Finished;
                    gridPerformance.Add(new{count,elapsedMs=gridMs,operations=operations.ToArray(),maxDispatcherOperationMs=maxOperationMs,maxInputGapMs=inputGaps.DefaultIfEmpty(0).Max(),maxDispatcherGapMs=gaps.DefaultIfEmpty(0).Max()});
                    Check("large grid input delay and single UI operation stay below 200ms",inputGaps.Count>0 && inputGaps.Max()<200 && maxOperationMs<200);
                    Capture(browser,"large-grid-last");
                    ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(browser.ListViewAction)!.GetPattern(PatternInterface.Toggle)).Toggle();await Layout(browser);
                    Check("returning from large grid retains inventory and empty selection",!large.GridView && large.VisibleRows.Count==count && large.SelectedEntries.Count==0);
                    large.VisibleRows.Single(row=>row.Entry.Index==0).Selected=true;
                    large.Search="file-00000";large.Search="no-such-entry";large.Search="file-099999";
                    Check("updating list reports status and blocks stale extraction",large.IsRefreshing && !large.CanSelect && !large.ExtractAllCommand.CanExecute(null) && large.ViewStatus==UiText.Get("Browser.Updating")
                        && !browser.EntryTable.IsEnabled && Descendants(browser).OfType<TextBlock>().Any(text=>text.Text==UiText.Get("Browser.Updating")));
                    Capture(browser,"large-list-updating");
                    await large.WaitForIdleAsync();await Layout(browser);
                    Check("rapid searches publish the latest result only",large.VisibleRows.Count==1 && large.VisibleRows[0].Entry.Index==99999 && large.SelectedEntries is [{Index:0}]);
                    large.Search="";await large.WaitForIdleAsync();large.Navigate("folder-001");await large.WaitForIdleAsync();await Layout(browser);
                    Check("background navigation retains folder scope and hidden selection",large.VisibleRows.Count==1000 && large.VisibleRows.All(row=>row.ParentPath=="folder-001") && large.SelectedEntries is [{Index:0}]);
                    large.ResetBrowserLayoutCommand.Execute(null);await large.WaitForIdleAsync();
                    Check("reset clears background sorting",large.BrowserPreferences.Sorts is null && !large.GridView && browser.EntryTable.Columns.All(column=>column.SortDirection is null));
                    var before=large.VisibleRows.ToArray();large.Search="file";browser.Close();await large.WaitForIdleAsync();
                    Check("closing a large browser prevents pending publication",!large.IsRefreshing && before.SequenceEqual(large.VisibleRows));browser=null;
                }
                else {browser.Close();browser=null;}
            }
            var embeddedEntries=Enumerable.Range(0,10000).Select(i=>new ArchiveEntry((uint)i,$"folder/file-{i:D5}.txt",1,false,false)).ToArray();
            var embeddedModel=await Task.Run(()=>new ArchiveContentsViewModel(Path.Combine(data,"embedded-large.zip"),embeddedEntries));
            var embeddedDialog=new ArchiveContentsDialog(embeddedModel);var host=window.HostArchiveAsync(embeddedDialog,embeddedModel);
            try
            {
                await Layout(window);await embeddedModel.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));await Layout(window);
                Check("embedded large browser publishes initial folder after its root loads",!embeddedModel.IsRefreshing && embeddedModel.VisibleRows.Count==1 && embeddedModel.VisibleRows[0].Entry.Directory);
            }
            finally {await embeddedDialog.EmbeddedClose!();await host;}
            var unopened=await Task.Run(()=>new ArchiveContentsViewModel(Path.Combine(data,"unopened.zip"),embeddedEntries));
            var unopenedDialog=new ArchiveContentsDialog(unopened);unopenedDialog.Close();await unopened.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));unopened.StartInitialView();
            Check("closing before initial load cancels deferred publication",!unopened.IsRefreshing && unopened.VisibleRows.Count==0);
        }
        catch(Exception failure){error=failure.ToString();Console.Error.WriteLine(error);}
        finally {browser?.Close();theme.SetMode("light");theme.Apply(false);}
        var passed=error is null && checks.Count>0 && checks.Values.All(value=>value);
        await File.WriteAllTextAsync(Path.Combine(root,$"artifacts/post032-ui-{language}-validation.json"),JsonSerializer.Serialize(new{passed,error,checks,captures,performance,descendingPerformance,gridPerformance,language,syntheticData=true,actualInstallationChanged=false},new JsonSerializerOptions{WriteIndented=true}));
        return passed;
    }
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);index++)
        {var child=System.Windows.Media.VisualTreeHelper.GetChild(root,index);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
