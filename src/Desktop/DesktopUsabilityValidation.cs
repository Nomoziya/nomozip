using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopUsabilityValidation
{
    internal static async Task RunAsync(MainWindow window,ThemeResources theme,string data,string baseDirectory,
        Action<string,bool> check,Action<Window,string> capture)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Requires isolated CI.");
        data=Path.Combine(data,"usability");Directory.CreateDirectory(data);
        Task Layout()=>window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        async Task Execute(Button button,MainViewModel model)
        {
            await Layout();
            // CanExecute invalidation is dispatched after a bound field changes.
            for(var attempt=0;attempt<40 && !button.IsEnabled;attempt++) {await Task.Delay(25);await Layout();}
            if(!button.IsEnabled)throw new Exception($"Command remained disabled: {button.Name}; idle={model.IsIdle}; canExecute={button.Command?.CanExecute(button.CommandParameter)}; profileNameLength={model.CompressionProfileName.Length}");
            Invoke(button);await Layout();await model.WaitForIdleAsync();await Layout();
        }
        bool Fits(FrameworkElement element)
        {
            var rect=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
            return element.IsVisible && rect.Left>=0 && rect.Top>=0 && rect.Right<=window.ActualWidth+1 && rect.Bottom<=window.ActualHeight+1;
        }
        var vault=new DpapiPasswordVault(Path.Combine(data,"synthetic.apv"));
        var model=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window));await model.InitializeAsync();window.DataContext=model;
        model.Page=3;
        await Layout();
        var view=(CompressionView)window.FindName("CompressionPage");
        var profileExpander=view.CompressionProfilesExpander;
        profileExpander.IsExpanded=true;
        var first=Path.Combine(data,"first.txt");var second=Path.Combine(data,"second.txt");
        await File.WriteAllTextAsync(first,"first source");await File.WriteAllTextAsync(second,"second source");model.AddCompressionSources([first,second]);
        await Layout();view.CompressionSourceTable.SelectedItem=model.CompressionSources[0];view.SaveCompressionProfileButton.BringIntoView();await Layout();
        check("compression scope distinguishes all sources from removal selection",model.CompressionSourceSummary==UiText.Format("Compression.SourceCount",2,1));
        model.CompressionFormat=1;model.CompressionLevel=4;model.CompressionVolumeCustom="1";model.CompressionVolumeOption=4;
        view.CompressionProfileNameBox.Text="Sharing";view.CompressionProfileNameBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        await Execute(view.SaveCompressionProfileButton,model);
        check("profile saves through actual form command",model.CompressionProfiles.Single().Name=="Sharing" && model.CompressionMessage==UiText.Get("Compression.ProfileSaved"));
        model.CompressionFormat=0;model.CompressionLevel=0;model.CompressionVolumeOption=0;
        await Layout();
        check("editing parameters releases profile selection so it can be reapplied",view.CompressionProfileBox.SelectedItem is null);
        view.CompressionProfileBox.SelectedItem=model.CompressionProfiles.Single();await Layout();
        check("profile selection restores format level and volume",model.CompressionFormat==1 && model.CompressionLevel==4 && model.CompressionVolumeOption==4 && model.CompressionVolumeCustom=="1");
        model.CompressionProfileName="sharing";model.CompressionLevel=3;await Execute(view.SaveCompressionProfileButton,model);
        check("same name replaces profile without duplicates",model.CompressionProfiles.Count==1 && model.CompressionProfiles[0].Level==3);
        model.CompressionVolumeOption=0;model.CompressionOutput=Path.Combine(data,"both-sources.zip");
        await Execute(view.CreateButton,model);
        if(!File.Exists(model.CompressionOutput))capture(window,"usability-compression-failure");
        check("compression creates archive from all sources despite one removal selection; engine="+(model.WorkspaceTasks.LastOrDefault()?.Failure?.Code ?? "none"),File.Exists(model.CompressionOutput));
        using(var archive=System.IO.Compression.ZipFile.OpenRead(model.CompressionOutput))
        {
            using var firstReader=new StreamReader(archive.GetEntry("first.txt")!.Open());using var secondReader=new StreamReader(archive.GetEntry("second.txt")!.Open());
            check("created archive contains both source bytes",archive.Entries.Count==2 && await firstReader.ReadToEndAsync()=="first source" && await secondReader.ReadToEndAsync()=="second source");
        }
        var settings=await File.ReadAllTextAsync(Path.Combine(data,"settings.json"));
        using(var json=System.Text.Json.JsonDocument.Parse(settings))
        {
            var profile=json.RootElement.GetProperty("CompressionProfiles")[0];
            check("profile persistence contains parameters only",profile.EnumerateObject().Select(property=>property.Name).Order().SequenceEqual(new[]{"Name","Format","Level","VolumeOption","VolumeUnit","VolumeCustom"}.Order()));
        }
        var restarted=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window));await restarted.InitializeAsync();
        check("last used compression parameters and profiles survive restart",restarted.CompressionFormat==1 && restarted.CompressionLevel==3 && restarted.CompressionVolumeOption==0 && restarted.CompressionProfiles.Count==1 && !restarted.CompressionEncrypt && restarted.CompressionSources.Count==0);
        restarted.SelectedCompressionProfile=restarted.CompressionProfiles.Single();restarted.Page=3;window.DataContext=restarted;await Layout();view.DeleteCompressionProfileButton.BringIntoView();await Layout();
        await Execute(view.DeleteCompressionProfileButton,restarted);
        var afterDelete=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window));await afterDelete.InitializeAsync();
        check("profile deletion persists without changing last used parameters",afterDelete.CompressionProfiles.Count==0 && afterDelete.CompressionFormat==1 && afterDelete.CompressionLevel==3);
        window.DataContext=model;await Layout();

        var failed=new ArchiveTaskRow(Path.Combine(data,"wrong-password.7z")){Failure=new EngineResult{Status="error",Code="wrong_password"},Status=UiText.Get("T_c62af9393001")};
        var cancelled=new ArchiveTaskRow(Path.Combine(data,"cancelled.7z")){Failure=new EngineResult{Status="cancelled",Code="cancelled"}};
        var paused=new ArchiveTaskRow(Path.Combine(data,"paused.7z")){Running=true,PauseRequested=true,Paused=true,Status=UiText.Get("TaskControl.PauseRequested")};
        model.Tasks.Add(failed);model.Tasks.Add(cancelled);model.Tasks.Add(paused);model.Tasks.Add(new(Path.Combine(data,"pending.7z")));
        await Layout();window.TaskDensityChoice.SelectedIndex=1;await Layout();
        check("density selector switches actual task rows to comfortable",model.TaskDensityIndex==1 && window.ArchiveTasks.RowHeight==88);
        window.TaskDensityChoice.SelectedIndex=0;await Layout();
        check("density selector restores compact task rows",model.TaskDensityIndex==0 && window.ArchiveTasks.RowHeight==64);
        model.SelectedTask=failed;await Layout();
        check("failed selection expands task details",model.TaskDetailsExpanded && window.TaskDetailsExpander.IsExpanded);
        window.TaskDetailsExpander.IsExpanded=false;await Layout();
        check("collapsing details preserves queue controls",!model.TaskDetailsExpanded && window.ExtractAction.IsVisible && window.QueueConcurrencyChoice.IsVisible);
        model.TaskFilterIndex=4;
        check("failure filter excludes cancellations and completed compression",model.TaskView.Cast<ArchiveTaskRow>().SequenceEqual(new[]{failed}) && model.TaskFilterBadges[4].Count==1);
        model.TaskFilterIndex=5;
        check("paused filter includes active paused task",model.TaskView.Cast<ArchiveTaskRow>().SequenceEqual(new[]{paused}) && model.TaskFilterBadges[5].Count==1);
        paused.PauseRequested=false;paused.Paused=false;
        check("paused filter refreshes immediately on resume",model.TaskView.IsEmpty && model.TaskFilterBadges[5].Count==0);
        paused.PauseRequested=true;paused.Paused=true;

        foreach(var mode in new[]{"light","dark","contrast"})foreach(var width in new[]{1152d,800d})
        {
            theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");window.MinWidth=width;window.Width=width;window.Height=width==800?600:768;
            model.Page=0;model.TaskFilterIndex=4;model.SelectedTask=failed;await Layout();
            if(model.IsNarrow)
            {
                var filters=window.CompactTaskFilter;
                window.Left=0;window.Top=0;window.Opacity=1;window.Activate();await Layout();
                var filterPeer=UIElementAutomationPeer.CreatePeerForElement(filters)!;
                if(filterPeer.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider expand)throw new Exception("Task filter does not expose ExpandCollapse.");
                expand.Expand();await Layout();
                for(var attempt=0;attempt<40 && filters.ItemContainerGenerator.ContainerFromIndex(4) is not ComboBoxItem;attempt++)
                {if(filters.Template.FindName("PART_Popup",filters) is Popup {Child:FrameworkElement child})child.UpdateLayout();await Task.Delay(25);await Layout();}
                if(filters.ItemContainerGenerator.ContainerFromIndex(4) is not ComboBoxItem failedItem)throw new Exception("Visible task filter popup did not realize its six choices.");
                // SelectionItem is exposed by item data peers under the ComboBox peer, not container wrapper peers.
                var choices=filterPeer.GetChildren().Where(peer=>peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider).ToArray();
                if(choices.Length!=6)throw new Exception($"Task filter exposes {choices.Length} selection peers instead of six.");
                ((ISelectionItemProvider)choices[4].GetPattern(PatternInterface.SelectionItem)).Select();
                filters.IsDropDownOpen=false;await Layout();
                window.Left=window.Top=-32000;window.Opacity=0;
                check($"six task filters reachable {mode} {width}",filters.Items.Count==6 && Fits(filters) && model.TaskFilterIndex==4);
            }
            else check($"six task filters reachable {mode} {width}",window.TaskFilterTabs.Items.Count==6 && Fits(window.TaskFilterTabs) && Descendants(window.TaskFilterTabs).OfType<ListBoxItem>().All(Fits));
            capture(window,$"usability-tasks-{mode}-{width}");
            model.Page=3;await Layout();
            check($"compression output summary and creation stay visible {mode} {width}",Fits(view.CompressionResultSummaryText) && Fits(view.CreateButton) && view.CompressionResultSummaryText.Text==model.CompressionResultSummary);
            view.CompressionProfileNameBox.BringIntoView();await Layout();
            // Open only this extension, preserving the established settings layout.
            var expander=profileExpander;expander.IsExpanded=true;
            view.SaveCompressionProfileButton.BringIntoView();await Layout();
            check($"compression profile actions reachable {mode} {width}",Fits(view.SaveCompressionProfileButton) && Fits(view.CreateButton));
            check($"profile keyboard and accessible name {mode} {width}",view.CompressionProfileNameBox.Focusable && UIElementAutomationPeer.CreatePeerForElement(view.CompressionProfileNameBox)!.GetName()==UiText.Get("Compression.ProfileName"));
            capture(window,$"usability-compression-{mode}-{width}");expander.IsExpanded=false;
        }
        model.Tasks.Clear();theme.SetMode("light");theme.Apply(false);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
