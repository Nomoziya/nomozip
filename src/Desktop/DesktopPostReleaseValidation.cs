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
            Toggle(window.AutoCleanApplicationInstallersBox);Invoke(window.SaveApplicationUpdateSettingsButton);await model.WaitForIdleAsync();
            var restarted=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window));await restarted.InitializeAsync();
            Check("auto cleanup switch persists after restart",!restarted.AutoCleanApplicationInstallers);
            var cache=Path.Combine(data,"application-updates");Directory.CreateDirectory(cache);var stale=Path.Combine(cache,Guid.NewGuid().ToString("N")+".exe");await File.WriteAllTextAsync(stale,"old installer");
            await new ApplicationUpdateReceipt("0.3.1",model.ApplicationVersion,baseDirectory).SaveAsync(cache);
            await restarted.InitializeAsync();Check("disabled startup cleanup keeps installer",File.Exists(stale));
            Toggle(window.AutoCleanApplicationInstallersBox);Invoke(window.SaveApplicationUpdateSettingsButton);await model.WaitForIdleAsync();
            await new ApplicationUpdateReceipt("0.3.1",model.ApplicationVersion,baseDirectory).SaveAsync(cache);
            var upgraded=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window));await upgraded.InitializeAsync();
            Check("completed initialization consumes upgrade and clears old installer",!File.Exists(stale) && !File.Exists(Path.Combine(cache,"pending-update.json")) && upgraded.ApplicationUpdateMessage.Contains("1"));
            foreach(var mode in new[]{"light","dark"})foreach(var width in new[]{1152d,800d})
            {
                theme.SetMode(mode);theme.Apply(false);window.MinWidth=width;window.Width=width;window.Height=width==800?600:768;await Layout(window);
                window.AutoCleanApplicationInstallersBox.BringIntoView();await Layout(window);
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
            window.VaultDetails.DirectoryMatchBox.Text=Path.Combine(data,"changed-source");Invoke(window.VaultDetails.SaveButton);await model.WaitForIdleAsync();await Layout(window);
            Check("actual detail save persists source rule",(await vault.ReadAsync()).Single().MatchDirectory==Path.Combine(data,"changed-source"));

            var archive=Path.Combine(data,"content.zip");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
                foreach(var name in new[]{"folder/a.txt","folder/b.jpg","other/c.txt"}) {using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(name);}
            var dll=Directory.EnumerateFiles(Path.Combine(baseDirectory,"engines"),"7z.dll",SearchOption.AllDirectories).First();
            var engine=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),dll);
            var inventory=await engine.ListAsync(archive);Check("real archive inventory available",inventory.IsSuccess && inventory.Items.Count==3);
            var output=Path.Combine(data,"output");
            var view=new ArchiveContentsViewModel(archive,inventory.Items,async(selected,progress)=>
            {
                var result=await engine.ExtractSelectionAsync(archive,output,selected,null,new Dictionary<uint,string>());
                return result.IsSuccess?output:null;
            });
            browser=new ArchiveContentsDialog(view){Owner=window,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000,Opacity=0};browser.Show();
            browser.AdvancedFilters.IsExpanded=true;await Layout(browser);
            view.Search="b.jpg";view.Rows.Single().Selected=true;
            browser.FilterExtensionsBox.Text="txt";browser.FilterExtensionsBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();view.Search="";
            Check("filter textbox uses real binding",view.FilterExtensions=="txt" && view.VisibleRows.Count==2);
            Check("hidden checked entry retained with accurate count",view.SelectedEntries.Single().Path.Replace('\\','/')=="folder/b.jpg" && view.CompactSelectionSummary.Contains("1"));
            Invoke(browser.ExtractSelectedButton);await view.WaitForIdleAsync();await Layout(browser);
            Check("filtered selection extracts actual hidden entry only",Directory.GetFiles(output,"*",SearchOption.AllDirectories).Length==1 && File.ReadAllText(Path.Combine(output,"folder/b.jpg"))=="folder/b.jpg");
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
            theme.SetMode("light");theme.Apply(false);
            foreach(var count in new[]{10000,100000})
            {
                var entries=Enumerable.Range(0,count).Select(i=>new ArchiveEntry((uint)i,$"folder-{i%100:D3}/file-{i:D6}.txt",(ulong)i,false,false)).ToArray();
                var gaps=new List<double>();var previous=System.Diagnostics.Stopwatch.GetTimestamp();
                var heartbeat=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(16)};
                heartbeat.Tick+=(_,_)=>{var now=System.Diagnostics.Stopwatch.GetTimestamp();gaps.Add(System.Diagnostics.Stopwatch.GetElapsedTime(previous,now).TotalMilliseconds);previous=now;};heartbeat.Start();
                var watch=System.Diagnostics.Stopwatch.StartNew();
                var large=new ArchiveContentsViewModel(Path.Combine(data,"large-inventory.zip"),entries);
                browser=new ArchiveContentsDialog(large){Owner=window,MinWidth=1152,Width=1152,Height=768,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000,Opacity=0};browser.Show();await Layout(browser);
                var openMs=watch.Elapsed.TotalMilliseconds;
                watch.Restart();browser.EntrySearch.Text=".txt";browser.EntrySearch.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();await Layout(browser);var searchMs=watch.Elapsed.TotalMilliseconds;
                Check($"large inventory bound search completes {count}",large.VisibleRows.Count==count);
                var realized=Descendants(browser.EntryTable).OfType<DataGridRow>().Count();
                Check($"large list realizes a bounded number of rows {count}",realized>0 && realized<200);
                watch.Restart();CollectionViewSource.GetDefaultView(large.VisibleRows).SortDescriptions.Add(new(nameof(ArchiveContentRow.Path),System.ComponentModel.ListSortDirection.Descending));await Layout(browser);var sortMs=watch.Elapsed.TotalMilliseconds;
                Capture(browser,$"large-list-{count}");
                await Task.Delay(40);heartbeat.Stop();
                performance.Add(new{count,openMs,searchMs,sortMs,maxDispatcherGapMs=gaps.DefaultIfEmpty(0).Max(),peakWorkingSet=System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64});
                browser.Close();browser=null;
            }
        }
        catch(Exception failure){error=failure.ToString();Console.Error.WriteLine(error);}
        finally {browser?.Close();theme.SetMode("light");theme.Apply(false);}
        var passed=error is null && checks.Count>0 && checks.Values.All(value=>value);
        await File.WriteAllTextAsync(Path.Combine(root,$"artifacts/post032-ui-{language}-validation.json"),JsonSerializer.Serialize(new{passed,error,checks,captures,performance,language,syntheticData=true,actualInstallationChanged=false},new JsonSerializerOptions{WriteIndented=true}));
        return passed;
    }
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);index++)
        {var child=System.Windows.Media.VisualTreeHelper.GetChild(root,index);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
