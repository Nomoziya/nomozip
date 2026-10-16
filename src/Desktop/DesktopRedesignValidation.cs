using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

// Run only on an isolated GitHub runner. No local desktop input injection is used.
internal static class DesktopRedesignValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, MainViewModel model, ThemeResources theme, string root)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true") throw new InvalidOperationException("UI validation requires isolated CI.");
        var folder=Path.Combine(root,"artifacts/ui-redesign",UiText.LanguageCode);Directory.CreateDirectory(folder);
        var checks=new Dictionary<string,bool>();string? error=null;
        void Check(string name,bool passed)=>checks.Add(name,passed);
        async Task Layout()=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        async Task Until(Func<bool> predicate)
        {
            var deadline=DateTime.UtcNow.AddSeconds(35);
            while(!predicate()) {if(DateTime.UtcNow>deadline)throw new TimeoutException("UI transition timed out.");await Task.Delay(25);await Layout();}
        }
        Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        void Capture(string name)
        {
            window.Opacity=1;DesktopAccessibilityValidation.Render(window,Path.Combine(folder,name+".png"),1,clientOnly:true);window.Opacity=0;
        }
        try
        {
            model.LoadPreview();
            Check("five destinations use four sidebar items plus bottom settings",window.Navigation.Items.Count==4 && window.SettingsNavigation.IsVisible);
            foreach(var mode in new[]{"light","dark","contrast"})
            {
                theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");
                foreach(var width in new[]{800,1160})
                {
                    window.Width=width;window.Height=width==800?600:760;
                    foreach(var page in new[]{4,0,1,3,5,2})
                    {
                        model.Page=page;model.SettingsSection=0;await Layout();
                        Check($"{mode}/{width}/{page} main workspace remains inside window",Bounds(window.MainContent).Right<=window.ActualWidth+1 && window.MainContent.ActualWidth>400);
                        Check($"{mode}/{width}/{page} navigation remains reachable",window.SettingsNavigation.IsVisible && Bounds(window.SettingsNavigation).Bottom<=window.ActualHeight+1);
                        if(page==0)
                            Check($"{mode}/{width} each source task appears exactly once",window.ArchiveTasks.Items.Count==model.Tasks.Count && model.TaskView.Cast<ArchiveTaskRow>().Distinct().Count()==model.Tasks.Count);
                        Capture($"{mode}-{width}-page-{page}");
                    }
                    model.Page=2;
                    for(var section=0;section<6;section++)
                    {
                        model.SettingsSection=section;await Layout();
                        Check($"{mode}/{width}/settings-{section} no horizontal page overflow",window.PreferencesScroll.ExtentWidth<=window.PreferencesScroll.ViewportWidth+1);
                        Capture($"{mode}-{width}-settings-{section}");
                    }
                    model.Page=1;model.SelectedPassword=model.PasswordRows.FirstOrDefault();await Layout();
                    Check($"{mode}/{width} vault detail adapts",window.VaultDetails.IsVisible==(width>=1000));
                }
                if(mode=="contrast") Check("system contrast preserves foreground/background pairs",ReferenceEquals(Application.Current.Resources["TextBrush"],SystemColors.WindowTextBrush) && ReferenceEquals(Application.Current.Resources["SelectionBrush"],SystemColors.HighlightBrush));
            }
            theme.SetMode("light");theme.Apply(false);window.Width=1160;window.Height=760;
            model.Page=1;model.SelectedPassword=model.PasswordRows[0];await Layout();
            Check("vault details populate only on selection",window.VaultDetails.SecretBox.Password.Length>0 && window.VaultDetails.VisibleSecret.Text.Length==0);
            model.Page=0;await Layout();
            Check("leaving vault clears both password controls",window.VaultDetails.SecretBox.Password.Length==0 && window.VaultDetails.VisibleSecret.Text.Length==0);
            model.TaskFilterIndex=3;Check("ended task filter excludes pending entries",model.TaskView.Cast<ArchiveTaskRow>().All(value=>value.Completed || value.Failure is not null));
            model.TaskFilterIndex=2;Check("pending task filter excludes completed entries",model.TaskView.Cast<ArchiveTaskRow>().All(value=>!value.Completed && !value.Running && value.Failure is null));model.TaskFilterIndex=0;

            // Exercise the real engine through the embedded file workspace using a tiny synthetic archive.
            var data=Path.Combine(folder,"synthetic",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
            var vault=new DpapiPasswordVault(Path.Combine(data,"passwords.apv"));
            model=new MainViewModel(AppContext.BaseDirectory,data,vault,new WindowDialogs(window));window.DataContext=model;await model.InitializeAsync();
            var archive=Path.Combine(data,"sample.zip");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
            {
                foreach(var name in new[]{"notes.txt","other.txt"}) {var entry=zip.CreateEntry(name);using var writer=new StreamWriter(entry.Open());writer.Write("Synthetic UI validation "+name);}
                foreach(var name in new[]{"Photos/","Documents/","Documents/requirements.txt","Documents/changes.txt","metadata.json","readme.md"})
                {var entry=zip.CreateEntry(name);if(!name.EndsWith('/')) {using var writer=new StreamWriter(entry.Open());writer.Write("Synthetic design verification content.");}}
                var pixels=new byte[480*300*4];
                for(var y=0;y<300;y++)for(var x=0;x<480;x++){var offset=(y*480+x)*4;pixels[offset]=(byte)(110+x/5);pixels[offset+1]=(byte)(100+y/3);pixels[offset+2]=45;pixels[offset+3]=255;}
                var image=BitmapSource.Create(480,300,96,96,PixelFormats.Bgra32,null,pixels,480*4);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=zip.CreateEntry("cover.png").Open())encoder.Save(output);
            }
            var opening=model.OpenWorkspaceArchiveAsync([archive]);await Until(()=>window.ArchiveHost.Content is FrameworkElement);
            Check("archive browsing lives inside file workspace",model.Page==4 && model.IsBusy && !opening.IsCompleted);
            var browser=(FrameworkElement)window.ArchiveHost.Content;var contents=(ArchiveContentsViewModel)browser.DataContext;
            await Layout();
            // Verify the actual visual tree, not a duplicate geometry model.
            var extractAll=VisualDescendants(browser).OfType<Button>().Single(value=>value.Name=="ExtractAllButton");
            extractAll.ApplyTemplate();await Layout();
            var capsule=VisualDescendants(extractAll).OfType<Border>().First();
            Check("capsule radius equals half of actual height",Math.Abs(capsule.CornerRadius.TopLeft-capsule.ActualHeight/2)<.1 && capsule.ActualWidth>capsule.ActualHeight*2);
            var entryTable=VisualDescendants(browser).OfType<DataGrid>().Single(value=>value.Name=="EntryTable");
            Check("extraction toolbar precedes file list",Bounds(extractAll).Bottom<=Bounds(entryTable).Top);
            Check("archive name occupies shared workspace header",window.ArchiveHeaderHost.Content is FrameworkElement && window.FilesLandingActions.Visibility==Visibility.Collapsed);
            contents.CurrentRow=contents.Rows.Single(value=>value.Path=="notes.txt");contents.PreviewCommand.Execute(null);await Until(()=>!contents.IsWorking);
            Check("explicit preview is embedded",window.PreviewHost.Content is FrameworkElement && window.ArchivePreviewPane.IsVisible);
            var preview=(ArchivePreviewViewModel)((FrameworkElement)window.PreviewHost.Content).DataContext;
            Check("preview contains actual archive content",preview.Text.Contains("Synthetic UI validation notes.txt",StringComparison.Ordinal));
            Capture("actual-embedded-browser-preview");
            contents.CurrentRow=contents.Rows.Single(value=>value.Path=="cover.png");contents.CurrentRow.Selected=true;
            contents.PreviewCommand.Execute(null);await Until(()=>!contents.IsWorking);await Layout();
            var imagePreview=(ArchivePreviewViewModel)((FrameworkElement)window.PreviewHost.Content).DataContext;
            Check("image preview renders decoded archive image",imagePreview.IsImage && imagePreview.Picture?.PixelWidth==480);
            Capture("actual-embedded-browser-image-preview");contents.CurrentRow.Selected=false;
            theme.SetMode("dark");await Layout();Capture("actual-embedded-browser-image-preview-dark");theme.SetMode("light");
            model.Page=0;await Layout();Check("switching away clears decoded preview",preview.Text.Length==0 && imagePreview.Picture is null && window.PreviewHost.Content is null);model.Page=4;
            window.PrepareExtractionOptionsForValidation=dialog=>
            {
                dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                {
                    var start=Descendants((DependencyObject)dialog.Content).OfType<Button>().Single(value=>value.IsDefault);
                    start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
            };
            model.OutputPath=Path.Combine(data,"output");model.ConflictPolicy=1;
            contents.Rows.Single(value=>value.Path=="notes.txt").Selected=true;
            contents.ExtractSelectedCommand.Execute(null);await Until(()=>!contents.IsWorking);
            Check("selection extraction honors requested content",Directory.GetFiles(model.OutputPath,"notes.txt",SearchOption.AllDirectories).Length==1 && Directory.GetFiles(model.OutputPath,"other.txt",SearchOption.AllDirectories).Length==0);
            Capture("actual-selection-result");
            window.CloseArchiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await opening;
            Check("closing archive releases operation lifetime",model.IsIdle && window.ArchiveHost.Content is null);
            window.PrepareExtractionOptionsForValidation=null;

            // Actual inline edits use the synthetic vault and preserve secret masking on navigation.
            var saved=new SavedPassword(Guid.NewGuid(),"Synthetic vault item","synthetic-secret","",true,DateTimeOffset.UtcNow);
            await model.SaveVaultDetailsAsync(saved);model.Page=1;await Layout();
            window.VaultDetails.NameBox.Text="Edited synthetic item";window.VaultDetails.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>model.IsIdle);
            Check("inline details persist to synthetic vault",(await vault.ReadAsync()).Single().Name=="Edited synthetic item");
            model.Page=0;await Layout();Check("inline editor clears secret after saving and leaving",window.VaultDetails.SecretBox.Password.Length==0);

            // Compression is visible on Tasks but never added to the extraction input collection.
            var source=Path.Combine(data,"source.txt");await File.WriteAllTextAsync(source,"synthetic compression content");
            model.Page=3;model.AddCompressionSources([source]);model.CompressionOutput=Path.Combine(data,"created.7z");model.CreateArchiveCommand.Execute(null);await Until(()=>model.IsIdle);
            Check("compression routes to unified task workspace",model.Page==0 && model.HasCompressionActivity && File.Exists(model.CompressionOutput));
            Check("compression output is not an extraction task",model.Tasks.All(value=>!string.Equals(value.Path,model.CompressionOutput,StringComparison.OrdinalIgnoreCase)));
            Capture("actual-compression-result");
        }
        catch(Exception exception) {error=exception.ToString();}
        finally {window.PrepareExtractionOptionsForValidation=null;}
        var passed=error is null && checks.Count>100 && checks.Values.All(value=>value);
        await File.WriteAllTextAsync(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new {passed,checks,error,scope="isolated CI; synthetic data; rendered WPF; contrast resource simulation; no physical-display or Narrator audio acceptance"},new JsonSerializerOptions{WriteIndented=true}));
        return passed;
    }
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
        {var child=VisualTreeHelper.GetChild(parent,index);yield return child;foreach(var nested in VisualDescendants(child))yield return nested;}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) {yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
