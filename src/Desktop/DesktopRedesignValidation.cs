using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        var viewports=new List<object>();
        var nativeWindows=new List<object>();
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
            await DesktopFrameValidation.RunAsync(window,Check);
            var previewSources=Path.Combine(folder,"synthetic","source-list");Directory.CreateDirectory(previewSources);
            var sourceFolder=Path.Combine(previewSources,"项目文件");Directory.CreateDirectory(sourceFolder);
            var previewText=Path.Combine(previewSources,"说明.txt");await File.WriteAllTextAsync(previewText,"Synthetic source text for visual verification.\r\n");
            var previewSheet=Path.Combine(previewSources,"数据.csv");await File.WriteAllTextAsync(previewSheet,"Name,Value\r\nSample,123\r\n");
            var previewImage=Path.Combine(previewSources,"素材.png");
            var sourceBitmap=BitmapSource.Create(24,24,96,96,PixelFormats.Gray8,null,Enumerable.Repeat((byte)128,24*24).ToArray(),24);
            var sourceEncoder=new PngBitmapEncoder();sourceEncoder.Frames.Add(BitmapFrame.Create(sourceBitmap));using(var stream=File.Create(previewImage))sourceEncoder.Save(stream);
            model.CompressionSources.Clear();model.AddCompressionSources([sourceFolder,previewText,previewImage,previewSheet]);model.SelectedCompressionSource=model.CompressionSources[0];
            model.SelectedTask=model.Tasks[0];
            model.Tasks[0].Running=true;model.Tasks[0].Percent=62;model.Tasks[0].Status=UiText.Get("Redesign.SampleExtracting");
            model.Tasks[0].SelectedOutputPath=@"D:\Pictures\album";
            model.Tasks[2].ResultOutputPath=@"D:\Work\assets";
            model.Tasks.Add(new ArchiveTaskRow(@"D:\Downloads\project.7z","18 MB"));
            model.Tasks.Add(new ArchiveTaskRow(@"D:\Downloads\history.zip","240 MB") {Completed=true,Status=UiText.Get("Redesign.SampleCompleted"),ResultOutputPath=@"D:\Documents\history"});
            var failedPreviewTask=new ArchiveTaskRow(@"D:\Downloads\damaged.7z","8 MB") {Status=UiText.Get("T_db5cb5a070e6"),Failure=new EngineResult {Status="error",Code="synthetic-preview"}};
            model.Tasks.Add(failedPreviewTask);
            Check("failed task offers details rather than extract",failedPreviewTask.StateKind=="error" && failedPreviewTask.PrimaryActionCaption==UiText.Get("Redesign.ViewDetails"));
            window.MinWidth=1152;window.Width=1152;window.Height=768;await Layout();
            Check("five destinations use four sidebar items plus bottom settings",window.Navigation.Items.Count==4 && window.SettingsNavigation.IsVisible);
            foreach(var mode in new[]{"light","dark","soft","contrast"})
            {
                theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");
                model.SelectedAppearance=model.AppearanceOptions.Single(option=>option.Code==(mode=="contrast"?"system":mode));
                if(mode!="contrast")foreach(var pair in new[]{("TextBrush","SurfaceBrush",4.5),("SecondaryBrush","SurfaceBrush",4.5),("OnAccentBrush","AccentBrush",4.5),("SelectionTextBrush","SelectionBrush",4.5),("ErrorBrush","SurfaceBrush",4.5),("WarningTextBrush","WarningSurfaceBrush",4.5),("BorderBrush","SurfaceBrush",3.0),("FocusBrush","SurfaceBrush",3.0)})
                    Check(mode+" contrast "+pair.Item1+" on "+pair.Item2,DesktopThemeValidation.Contrast(((SolidColorBrush)Application.Current.Resources[pair.Item1]).Color,((SolidColorBrush)Application.Current.Resources[pair.Item2]).Color)>=pair.Item3);
                foreach(var width in new[]{800,1152})
                {
                    window.MinWidth=width;window.Width=width;window.Height=width==800?600:768;
                    foreach(var page in new[]{4,0,1,3,5,2})
                    {
                        model.Page=page;model.SettingsSection=0;await Layout();
                        viewports.Add(new {mode,page,requestedWidth=width,actualWindowWidth=window.ActualWidth,actualWindowHeight=window.ActualHeight,clientWidth=((FrameworkElement)window.Content).ActualWidth,clientHeight=((FrameworkElement)window.Content).ActualHeight});
                        Check($"{mode}/{width}/{page} main workspace remains inside window",Bounds(window.MainContent).Right<=window.ActualWidth+1 && window.MainContent.ActualWidth>400);
                        Check($"{mode}/{width}/{page} navigation remains reachable",width==800 ? window.ReferenceCompactMenu.IsVisible && window.ReferenceCompactMenu.ContextMenu.Items.Count==5 : window.SettingsNavigation.IsVisible && Bounds(window.SettingsNavigation).Bottom<=window.ActualHeight+1);
                        if(page==0)
                        {
                            Check($"{mode}/{width} each source task appears exactly once",window.ArchiveTasks.Items.Count==model.Tasks.Count && model.TaskView.Cast<ArchiveTaskRow>().Distinct().Count()==model.Tasks.Count);
                            Check($"{mode}/{width} row actions keep compact height",VisualDescendants(window.ArchiveTasks).OfType<Button>().Where(value=>value.DataContext is ArchiveTaskRow).All(value=>value.ActualHeight<=44));
                            var rowButtons=VisualDescendants(window.ArchiveTasks).OfType<Button>().Where(value=>value.DataContext is ArchiveTaskRow && value.IsVisible).ToArray();
                            Check($"{mode}/{width} every task action stays inside its cell and table",rowButtons.Length>=2 && rowButtons.All(button=>
                            {
                                DependencyObject? parent=button;while(parent is not null && parent is not DataGridCell)parent=VisualTreeHelper.GetParent(parent);
                                var cell=parent as DataGridCell;
                                return cell is not null && Bounds(button).Left>=Bounds(cell).Left-.5 && Bounds(button).Right<=Math.Min(Bounds(cell).Right,Bounds(window.ArchiveTasks).Right)+.5;
                            }));
                            var primaryWidths=rowButtons.Where(button=>Equals(button.Tag,"TaskPrimary")).Select(button=>Math.Round(button.ActualWidth,1)).Distinct().ToArray();
                            Check($"{mode}/{width} task primary pills share one width",primaryWidths.Length==1 && primaryWidths[0]>=100);

                        }
                        if(page==3)
                        {
                            var sourceTable=window.CompressionPage.CompressionSourceTable;
                            Check($"{mode}/{width} compression source headers remain inside the table",VisualDescendants(sourceTable).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().Where(header=>header.Column is not null).All(header=>Bounds(header).Right<=Bounds(sourceTable).Right+1));
                            Check($"{mode}/{width} compression footer stays reachable",Bounds(window.CompressionPage.CreateButton).Bottom<=((FrameworkElement)window.Content).ActualHeight+1 && window.CompressionPage.CreateButton.ActualWidth>60);
                            Check($"{mode}/{width} compression supports enabled 7z level and encryption",window.CompressionPage.LevelBox.IsEnabled && window.CompressionPage.EncryptBox.IsEnabled);
                            var form=window.CompressionPage.SettingsForm;
                            Check($"{mode}/{width} compression form uses available space without excessive stretching",form.ActualWidth>300 && form.ActualWidth<=640.5 && Bounds(form).Right<=Bounds(window.CompressionPage.SettingsScroll).Right+1);
                            var formatBounds=Bounds(window.CompressionPage.FormatBox);var levelBounds=Bounds(window.CompressionPage.LevelBox);
                            Check($"{mode}/{width} short compression choices fit on one readable row",Math.Abs(formatBounds.Top-levelBounds.Top)<1 && formatBounds.Width>=80 && formatBounds.Width<=110 && levelBounds.Width>=80 && levelBounds.Width<=180 && levelBounds.Left-formatBounds.Right>=19 && levelBounds.Right<=Bounds(form).Right+1);
                        }
                        Capture($"{mode}-{width}-page-{page}");
                        if(width==1152 && page==0 && mode is "light" or "dark" or "soft")
                            nativeWindows.Add(await DesktopNativeWindowCapture.CaptureAsync(window,Path.Combine(folder,$"{mode}-{width}-page-{page}-window.png")));
                    }
                    model.Page=2;
                    for(var section=0;section<6;section++)
                    {
                        model.SettingsSection=section;await Layout();
                        Check($"{mode}/{width}/settings-{section} no horizontal page overflow",window.PreferencesScroll.ExtentWidth<=window.PreferencesScroll.ViewportWidth+1);
                        Capture($"{mode}-{width}-settings-{section}");
                    }
                    model.Page=1;model.SelectedPassword=model.PasswordRows.FirstOrDefault();await Layout();
                    Check($"{mode}/{width} vault detail adapts",window.VaultDetails.IsVisible && Math.Abs(window.VaultDetailsColumn.Width.Value-360)<1);
                    if(width>=1000)Check($"{mode}/{width} collapsed vault rules are not clipped",Bounds(window.VaultDetails.RulesExpander).Bottom<=Bounds(window.VaultDetails.DetailScroll).Bottom+1);
                }
                if(mode=="contrast") Check("system contrast preserves foreground/background pairs",ReferenceEquals(Application.Current.Resources["TextBrush"],SystemColors.WindowTextBrush) && ReferenceEquals(Application.Current.Resources["SelectionBrush"],SystemColors.HighlightBrush));
                if(mode=="soft")
                {
                    Check("soft contrast uses graphite warm text and muted focus",((SolidColorBrush)Application.Current.Resources["CanvasBrush"]).Color==Color.FromRgb(0x25,0x28,0x2b) && ((SolidColorBrush)Application.Current.Resources["TextBrush"]).Color==Color.FromRgb(0xe5,0xe2,0xda) && ((SolidColorBrush)Application.Current.Resources["FocusBrush"]).Color==Color.FromRgb(0xc5,0xb9,0x8b));
                    theme.Apply(true);Check("system high contrast takes priority over soft palette",ReferenceEquals(Application.Current.Resources["TextBrush"],SystemColors.WindowTextBrush));theme.Apply(false);
                    var preference=Path.Combine(folder,"synthetic","soft-appearance");await AppearancePreferenceStore.SaveAsync(preference,"soft");Check("soft appearance preference survives save and reload",AppearancePreferenceStore.Read(preference).Mode=="soft");
                    window.MinWidth=1152;window.Width=1152;window.Height=768;model.Page=0;await Layout();
                    window.Opacity=1;window.Activate();window.ArchiveTasks.ScrollIntoView(model.Tasks[0]);await Layout();
                    var row=(DataGridRow)window.ArchiveTasks.ItemContainerGenerator.ContainerFromItem(model.Tasks[0]);
                    var cell=VisualDescendants(row).OfType<DataGridCell>().First();
                    Keyboard.Focus(cell);await Layout();
                    var ring=(Border)row.Template.FindName("RowFocus",row);
                    Check("soft task focus uses actual WPF keyboard focus",cell.IsKeyboardFocused && row.IsKeyboardFocusWithin);
                    Check("soft task focus renders the semantic focus border",ring.Visibility==Visibility.Visible && ReferenceEquals(ring.BorderBrush,Application.Current.Resources["FocusBrush"]));
                    Capture("soft-task-keyboard-focus");
                }
            }
            theme.SetMode("light");theme.Apply(false);window.MinWidth=800;window.Width=800;window.Height=768;model.Page=3;
            var originalCompressionOutput=model.CompressionOutput;
            model.CompressionOutput=@"D:\Documents\项目资料\2026 年季度归档与交付文件\设计素材与完整原始附件\需要保留完整名称的项目资料备份与交付记录.7z";
            await Layout();window.CompressionPage.ArchiveNameBox.Focus();window.CompressionPage.ArchiveNameBox.CaretIndex=window.CompressionPage.ArchiveNameBox.Text.Length;await Layout();
            Check("compact compression long fields preserve complete editable values",window.CompressionPage.ArchiveOutputBox.Text==model.CompressionOutputDirectory && window.CompressionPage.ArchiveNameBox.Text==model.CompressionOutputName && Bounds(window.CompressionPage.ArchiveNameBox).Right<=Bounds(window.CompressionPage.SettingsScroll).Right+1);
            Capture("compression-form-compact-long-fields");model.CompressionOutput=originalCompressionOutput;
            var originalLevel=model.CompressionLevel;
            foreach(var level in Enumerable.Range(0,window.CompressionPage.LevelBox.Items.Count))
            {
                model.CompressionLevel=level;await Layout();
                var box=window.CompressionPage.LevelBox;var label=((ComboBoxItem)box.SelectedItem).Content.ToString()!;
                var text=new FormattedText(label,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(box.FontFamily,box.FontStyle,box.FontWeight,box.FontStretch),box.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(box).PixelsPerDip);
                Check("compression level text fits without excessive blank space "+level,box.ActualWidth>=text.WidthIncludingTrailingWhitespace+39 && box.ActualWidth<=Math.Max(80,text.WidthIncludingTrailingWhitespace+42)+8);
            }
            model.CompressionLevel=originalLevel;model.CompressionVolumeOption=4;model.CompressionVolumeCustom="512";await Layout();
            window.CompressionPage.VolumeSizeBox.BringIntoView();await Layout();
            Check("custom volume reserves more editing space than its short unit selector",window.CompressionPage.VolumeSizeBox.ActualWidth>window.CompressionPage.VolumeUnitBox.ActualWidth);
            Capture("compression-form-custom-volume");model.CompressionVolumeOption=0;window.CompressionPage.CompressionViewport.ScrollToTop();
            window.MinWidth=1152;window.Width=1152;window.Height=768;
            model.Page=2;model.SettingsSection=4;await Layout();
            Check("update cards keep checks visible and secondary actions folded",window.CheckApplicationUpdatesButton.IsVisible && window.CheckEngineUpdatesButton.IsVisible && !window.ApplicationUpdateActions.IsExpanded && !window.EngineUpdateManagement.IsExpanded);
            window.ApplicationUpdateActions.IsExpanded=true;await Layout();
            Check("software update expansion retains all existing actions",window.OpenApplicationDownloadButton.IsVisible && window.DownloadApplicationUpdateButton.IsVisible && window.InstallApplicationUpdateButton.IsVisible);
            Capture("actual-software-update-actions");window.ApplicationUpdateActions.IsExpanded=false;
            window.EngineUpdateManagement.IsExpanded=true;await Layout();window.EngineUpdateManagement.BringIntoView();await Layout();
            Check("engine management expansion retains import install and version choices",window.ImportEngineUpdateButton.IsVisible && window.InstallEngineUpdateButton.IsVisible && window.EngineChoice.IsVisible && window.UpdateRepositoryBox.IsVisible);
            Capture("actual-engine-update-management");window.EngineUpdateManagement.IsExpanded=false;window.PreferencesScroll.ScrollToTop();
            model.Page=1;model.SelectedPassword=model.PasswordRows[0];await Layout();
            Check("vault details populate only on selection",window.VaultDetails.SecretBox.Password.Length>0 && window.VaultDetails.VisibleSecret.Text.Length==0);
            var nameHost=(ScrollViewer)window.VaultDetails.NameBox.Template.FindName("PART_ContentHost",window.VaultDetails.NameBox);
            Check("compact vault name field retains a full line",nameHost.ViewportHeight>=window.VaultDetails.NameBox.GetRectFromCharacterIndex(0).Height-1);
            model.Page=0;await Layout();
            Check("leaving vault clears both password controls",window.VaultDetails.SecretBox.Password.Length==0 && window.VaultDetails.VisibleSecret.Text.Length==0);
            model.TaskFilterIndex=3;Check("ended task filter excludes pending entries",model.TaskView.Cast<ArchiveTaskRow>().All(value=>value.Completed || value.Failure is not null));
            model.TaskFilterIndex=2;Check("pending task filter excludes completed entries",model.TaskView.Cast<ArchiveTaskRow>().All(value=>!value.Completed && !value.Running && value.Failure is null));model.TaskFilterIndex=0;

            // Exercise the real engine through the embedded file workspace using a tiny synthetic archive.
            var data=Path.Combine(folder,"synthetic",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
            var vault=new DpapiPasswordVault(Path.Combine(data,"passwords.apv"));
            model=new MainViewModel(AppContext.BaseDirectory,data,vault,new WindowDialogs(window));window.DataContext=model;await model.InitializeAsync();
            var openedResults=new List<string[]>();model.ConfigureResultDirectoryValidation(paths=>openedResults.Add(paths.ToArray()));
            var downloads=Path.Combine(data,UiText.LanguageCode=="en"?"Downloads":"下载");Directory.CreateDirectory(downloads);
            var archive=Path.Combine(downloads,"sample.zip");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
            {
                foreach(var name in new[]{"notes.txt","other.txt"}) {var entry=zip.CreateEntry(name);using var writer=new StreamWriter(entry.Open());writer.Write("Synthetic UI validation "+name);}
                foreach(var name in new[]{"Photos/","Documents/","Documents/requirements.txt","Documents/changes.txt","metadata.json","readme.md"})
                {var entry=zip.CreateEntry(name);if(!name.EndsWith('/')) {using var writer=new StreamWriter(entry.Open());writer.Write("Synthetic design verification content.");}}
                var pixels=new byte[480*300*4];
                for(var y=0;y<300;y++)for(var x=0;x<480;x++){var offset=(y*480+x)*4;pixels[offset]=(byte)(110+x/5);pixels[offset+1]=(byte)(100+y/3);pixels[offset+2]=45;pixels[offset+3]=255;}
                var image=BitmapSource.Create(480,300,96,96,PixelFormats.Bgra32,null,pixels,480*4);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                using var encoded=new MemoryStream();encoder.Save(encoded);encoded.Position=0;
                using(var output=zip.CreateEntry("cover.png").Open())encoded.CopyTo(output);
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
            Check("archive header and row checkboxes retain all four edges",VisualDescendants(entryTable).OfType<CheckBox>().Where(value=>value.IsVisible).All(UnclippedSelectionBox));
            var entryIcons=VisualDescendants(entryTable).OfType<FileKindIcon>().ToArray();
            Check("file icons reflect actual folders and image entries",entryIcons.Any(icon=>icon.DataContext is ArchiveContentRow {Entry.Directory:true} && icon.IconKind=="folder") && entryIcons.Any(icon=>icon.DataContext is ArchiveContentRow {Path:"cover.png"} && icon.IconKind=="image"));
            Check("folder and image glyphs are actually visible",entryIcons.Where(icon=>icon.IconKind is "folder" or "image").All(icon=>((FrameworkElement)icon.Template.FindName(icon.IconKind=="folder"?"Folder":"Picture",icon)).Visibility==Visibility.Visible && ((FrameworkElement)icon.Template.FindName("Document",icon)).Visibility==Visibility.Collapsed));
            Check("embedded file columns retain readable widths",entryTable.Columns[0].ActualWidth>=40 && entryTable.Columns[1].ActualWidth>=200 && entryTable.Columns[2].ActualWidth>=78 && entryTable.Columns[3].ActualWidth>=80);
            Check("extraction toolbar precedes file list",Bounds(extractAll).Bottom<=Bounds(entryTable).Top);
            Check("archive name occupies shared workspace header",window.ArchiveHeaderHost.Content is FrameworkElement && window.FilesLandingActions.Visibility==Visibility.Collapsed);
            Check("breadcrumb includes the actual containing directory",contents.ArchiveLocation==Path.GetFileName(Path.GetDirectoryName(archive)));
            Check("archive has one full-width footer below the preview",window.ArchiveFooterHost.Content is Border && window.GlobalStatus.Visibility==Visibility.Collapsed && Math.Abs(window.ArchiveFooterHost.ActualWidth-window.FilesWorkspace.ActualWidth)<1);
            var filesNav=(ListBoxItem)window.Navigation.Items[0];filesNav.ApplyTemplate();
            var navSurface=(Border)filesNav.Template.FindName("Pill",filesNav);
            Check("selected navigation retains its blue marker and regular label",((Border)filesNav.Template.FindName("NavigationMarker",filesNav)).IsVisible && filesNav.FontWeight==FontWeights.Normal && filesNav.ActualHeight>=36);
            Check("archive root hides nested file paths",contents.VisibleRows.All(row=>!row.Path.TrimEnd('/','\\').Replace('\\','/').Contains('/')));
            contents.CurrentRow=contents.VisibleRows.Single(row=>row.Path.TrimEnd('/','\\')=="Documents");contents.OpenCurrentEntryCommand.Execute(null);await Layout();
            Check("opening folder shows its children and breadcrumb",contents.CurrentFolder=="Documents" && contents.VisibleRows.Count==2 && contents.Breadcrumbs.Count==2 && contents.VisibleRows.All(row=>!row.DisplayName.Contains('/')));
            var overview=(TextBlock)VisualDescendants((DependencyObject)window.ArchiveFooterHost.Content).Single(value=>value is TextBlock {Name:"ContentsSummary"});
            Check("unselected folder overview still displays both real counts",overview.Text.Contains(contents.CompactSummary,StringComparison.Ordinal) && overview.Text.Contains(contents.CompactSelectionSummary,StringComparison.Ordinal));
            Capture("actual-archive-folder-navigation");contents.Search="changes";
            Check("archive search finds complete paths across folders",contents.VisibleRows.Count==1 && contents.VisibleRows[0].DisplayName.Contains("Documents"));contents.Search="";
            contents.FolderUpCommand.Execute(null);Check("folder up returns to archive root",contents.CurrentFolder.Length==0 && !contents.FolderUpCommand.CanExecute(null));
            contents.SelectVisibleCommand.Execute(null);Check("visible folders select all real descendants",contents.SelectedEntries.Count==9);contents.ClearSelectionCommand.Execute(null);
            IReadOnlyList<ArchiveEntry>? virtualSelection=null;
            var virtualFolder=new ArchiveContentsViewModel("virtual.zip",[new(0,"outer/inner/note.txt",1,false,false)],(selection,_)=>{virtualSelection=selection;return Task.FromResult<string?>(null);});
            virtualFolder.Navigate("outer");virtualFolder.VisibleRows.Single().Selected=true;virtualFolder.ExtractSelectedCommand.Execute(null);await virtualFolder.WaitForIdleAsync();
            Check("implicit folders resolve to real entry indices",virtualSelection is [{Index:0,Path:"outer/inner/note.txt"}]);
            contents.CurrentRow=contents.Rows.Single(value=>value.Path=="notes.txt");contents.PreviewCommand.Execute(null);await Until(()=>!contents.IsWorking);
            Check("explicit preview is embedded",window.PreviewHost.Content is FrameworkElement && window.ArchivePreviewPane.IsVisible);
            var preview=(ArchivePreviewViewModel)((FrameworkElement)window.PreviewHost.Content).DataContext;
            Check("preview contains actual archive content",preview.Text.Contains("Synthetic UI validation notes.txt",StringComparison.Ordinal));
            Capture("actual-embedded-browser-preview");
            contents.CurrentRow=contents.Rows.Single(value=>value.Path=="cover.png");contents.CurrentRow.Selected=true;
            contents.PreviewCommand.Execute(null);await Until(()=>!contents.IsWorking);await Layout();
            var imagePreview=(ArchivePreviewViewModel)((FrameworkElement)window.PreviewHost.Content).DataContext;
            Check("requested image supplies its real row thumbnail",contents.Rows.Single(row=>row.Path==imagePreview.EntryPath).PreviewThumbnail==imagePreview.Picture);
            Check("image preview renders decoded archive image",imagePreview.IsImage && imagePreview.Picture?.PixelWidth==480);
            Check("opening preview keeps size type and actual modification columns readable",entryTable.Columns.Skip(2).Take(3).All(column=>column.Visibility==Visibility.Visible) && entryTable.Columns[1].ActualWidth>=130 && contents.Rows.All(row=>row.EncryptionText.Length>0));
            Check("native archive modification time reaches rows and preview",contents.CurrentRow!.Entry.ModifiedUtcFileTime is >0 && imagePreview.ModifiedText==contents.CurrentRow.ModifiedText && imagePreview.ModifiedText!="—");
            var actualPreviewImage=VisualDescendants((DependencyObject)window.PreviewHost.Content).OfType<Image>().Single(value=>value.Name=="PreviewImage");
            Check("image preview uses its true aspect ratio without vertical letterboxing",Math.Abs(actualPreviewImage.ActualHeight-actualPreviewImage.ActualWidth*300/480)<2);
            var detailText=VisualDescendants(entryTable).OfType<TextBlock>().Where(text=>text.DataContext is ArchiveContentRow row && (text.Text==row.Kind || text.Text==row.EncryptionText)).ToArray();
            Check("visible file detail text fits beside the preview",detailText.All(text=>
            {
                var formatted=new FormattedText(text.Text,System.Globalization.CultureInfo.CurrentUICulture,text.FlowDirection,new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(text).PixelsPerDip);
                return formatted.WidthIncludingTrailingWhitespace<=text.ActualWidth+.5;
            }));
            nativeWindows.Add(await DesktopNativeWindowCapture.CaptureAsync(window,Path.Combine(folder,"actual-embedded-browser-image-preview-light-window.png")));
            Check("footer spans beneath the visible preview pane",window.ArchivePreviewPane.IsVisible && Bounds(window.ArchiveFooterHost).Top>=Bounds(window.ArchivePreviewPane).Bottom-1);
            var footerTasks=VisualDescendants((DependencyObject)window.ArchiveFooterHost.Content).OfType<Button>().Single(value=>value.Name=="BrowserTasksAction");
            footerTasks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            Check("file overview task action opens the real task workspace",model.Page==0 && window.ArchiveFooterHost.Visibility==Visibility.Collapsed);
            model.Page=4;await Layout();
            Check("returning to files restores the overview and suppresses duplicate status",window.ArchiveFooterHost.IsVisible && window.GlobalStatus.Visibility==Visibility.Collapsed);
            // Reopen the explicitly requested preview after navigating away cleared it.
            contents.PreviewCommand.Execute(null);await Until(()=>!contents.IsWorking);await Layout();
            imagePreview=(ArchivePreviewViewModel)((FrameworkElement)window.PreviewHost.Content!).DataContext;
            Capture("actual-embedded-browser-image-preview");
            theme.SetMode("dark");await Layout();Capture("actual-embedded-browser-image-preview-dark");
            nativeWindows.Add(await DesktopNativeWindowCapture.CaptureAsync(window,Path.Combine(folder,"actual-embedded-browser-image-preview-dark-window.png")));
            await File.WriteAllTextAsync(Path.Combine(folder,"native-window-captures.json"),JsonSerializer.Serialize(nativeWindows));
            Check("main window retains native caption and all three system controls",window.WindowStyle==WindowStyle.SingleBorderWindow && window.ResizeMode==ResizeMode.CanResize);
            theme.SetMode("light");
            var tiles=VisualDescendants(browser).OfType<ListBox>().Single(value=>value.Name=="EntryTiles");
            var gridAction=VisualDescendants((DependencyObject)window.ArchiveFooterHost.Content).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(value=>value.Name=="GridViewAction");
            var listAction=VisualDescendants((DependencyObject)window.ArchiveFooterHost.Content).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(value=>value.Name=="ListViewAction");
            var selectionBefore=contents.Rows.Where(row=>row.Selected).Select(row=>row.Path).ToArray();var requestedPicture=imagePreview.Picture;
            gridAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Layout();
            Check("real grid toggle shows tiles with shared current entry and selection",contents.GridView && tiles.IsVisible && !entryTable.IsVisible && ReferenceEquals(tiles.SelectedItem,contents.CurrentRow) && selectionBefore.SequenceEqual(contents.Rows.Where(row=>row.Selected).Select(row=>row.Path)));
            Check("grid switch does not load other image thumbnails",!contents.IsWorking && contents.Rows.Count(row=>row.PreviewThumbnail is not null)==1 && ReferenceEquals(requestedPicture,imagePreview.Picture));
            Capture("actual-archive-grid");theme.SetMode("dark");await Layout();Capture("actual-archive-grid-dark");theme.SetMode("light");
            var folderRow=contents.VisibleRows.Single(row=>row.Entry.Directory && row.Path.TrimEnd('/','\\')=="Documents");tiles.SelectedItem=folderRow;await Layout();
            var folderTile=(ListBoxItem)tiles.ItemContainerGenerator.ContainerFromItem(folderRow);var folderCheck=VisualDescendants(folderTile).OfType<CheckBox>().Single();folderCheck.IsChecked=true;await Layout();
            Check("grid checkbox selects actual descendant entries",contents.Rows.Count(row=>row.Path.Replace('\\','/').StartsWith("Documents/",StringComparison.Ordinal) && row.Selected)==2);folderCheck.IsChecked=false;
            Check("grid keyboard target stays on the chosen folder",ReferenceEquals(contents.CurrentRow,folderRow));
            var openTileBinding=tiles.InputBindings.OfType<KeyBinding>().Single(binding=>binding.Key==Key.Enter);openTileBinding.Command.Execute(null);await Layout();
            await File.WriteAllTextAsync(Path.Combine(folder,"grid-navigation.json"),JsonSerializer.Serialize(new {contents.GridView,contents.CurrentFolder,paths=contents.VisibleRows.Select(row=>row.Path).ToArray()}));
            Check("grid enter opens real folder without changing display mode",contents.GridView && contents.CurrentFolder=="Documents" && contents.VisibleRows.Count==2 && contents.VisibleRows.All(row=>row.Path.Replace('\\','/').StartsWith("Documents/",StringComparison.Ordinal)));
            tiles.InputBindings.OfType<KeyBinding>().Single(binding=>binding.Key==Key.Back).Command.Execute(null);await Layout();contents.CurrentRow=contents.Rows.Single(row=>row.Path=="cover.png");
            listAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Layout();
            Check("returning to list preserves grid selection and folder state",!contents.GridView && entryTable.IsVisible && !tiles.IsVisible && contents.CurrentFolder=="" && selectionBefore.SequenceEqual(contents.Rows.Where(row=>row.Selected).Select(row=>row.Path)));
            var selectionHeader=VisualDescendants(entryTable).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().Single(header=>ReferenceEquals(header.Column,entryTable.Columns[0]));
            var selectionCheck=VisualDescendants(selectionHeader).OfType<CheckBox>().Single();
            var selectionBox=(FrameworkElement)selectionCheck.Template.FindName("Box",selectionCheck);
            var rowSelection=VisualDescendants(entryTable).OfType<DataGridRow>().SelectMany(row=>VisualDescendants(row).OfType<CheckBox>()).First();
            var rowSelectionBox=(FrameworkElement)rowSelection.Template.FindName("Box",rowSelection);
            Check("select-all checkbox is fully visible and aligned with row checkboxes",Bounds(selectionBox).Left>=Bounds(selectionHeader).Left && Bounds(selectionBox).Right<=Bounds(selectionHeader).Right && Math.Abs(Bounds(selectionBox).Left-Bounds(rowSelectionBox).Left)<.5);
            var nameHeader=VisualDescendants(entryTable).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().Single(header=>ReferenceEquals(header.Column,entryTable.Columns[1]));
            var presenter=VisualDescendants(entryTable).OfType<System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter>().Single();
            var presenterPeer=System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(presenter)!;
            var namePeer=presenterPeer.GetChildren().Single(peer=>peer.GetName()==nameHeader.Content.ToString());
            ((System.Windows.Automation.Provider.IInvokeProvider)namePeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();await Layout();
            Check("name header performs actual sorting",entryTable.Columns[1].SortDirection==System.ComponentModel.ListSortDirection.Ascending && entryTable.Items.Cast<ArchiveContentRow>().Select(row=>row.DisplayName).SequenceEqual(entryTable.Items.Cast<ArchiveContentRow>().Select(row=>row.DisplayName).OrderBy(name=>name,StringComparer.CurrentCulture)));
            theme.SetMode("soft");window.Opacity=1;window.Activate();
            entryTable.ScrollIntoView(contents.CurrentRow);await Layout();
            var entryRow=(DataGridRow)entryTable.ItemContainerGenerator.ContainerFromItem(contents.CurrentRow);
            Keyboard.Focus(VisualDescendants(entryRow).OfType<DataGridCell>().First());await Layout();
            Check("archive workspace focus remains on the selected real entry",entryRow.IsKeyboardFocusWithin && contents.CurrentRow.Path=="cover.png");Capture("actual-soft-browser-keyboard-focus");theme.SetMode("light");
            theme.SetMode("soft");
            var moreButton=VisualDescendants(browser).OfType<Button>().Single(value=>value.Name=="EditContentsButton");
            moreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            var moreMenu=moreButton.ContextMenu;moreMenu.UpdateLayout();
            var firstAction=moreMenu.Items.OfType<MenuItem>().First();firstAction.ApplyTemplate();Keyboard.Focus(firstAction);await Layout();moreMenu.UpdateLayout();
            var menuIcon=(ContentPresenter)firstAction.Template.FindName("MenuIcon",firstAction);
            var menuFocus=(Border)firstAction.Template.FindName("ItemFocus",firstAction);
            Check("archive more menu retains icons shortcut and actual keyboard focus",moreMenu.IsOpen && menuIcon.IsVisible && menuIcon.ActualWidth>=18 && firstAction.InputGestureText=="Ctrl+A" && moreMenu.InputBindings.OfType<KeyBinding>().Any(binding=>binding.Key==Key.A && binding.Modifiers==ModifierKeys.Control && ReferenceEquals(binding.Command,contents.SelectVisibleCommand)) && firstAction.IsKeyboardFocused && ReferenceEquals(menuFocus.BorderBrush,Application.Current.Resources["FocusBrush"]));
            await File.WriteAllTextAsync(Path.Combine(folder,"menu-glyphs.json"),JsonSerializer.Serialize(moreMenu.Items.OfType<MenuItem>().Select(item=>new {header=item.Header?.ToString(),iconType=item.Icon?.GetType().Name,width=(item.Icon as FrameworkElement)?.ActualWidth,height=(item.Icon as FrameworkElement)?.ActualHeight,fill=((item.Icon as FluentIcon)?.Foreground as SolidColorBrush)?.Color.ToString(),foreground=(item.Foreground as SolidColorBrush)?.Color.ToString()})));
            Check("archive menu glyphs have visible themed strokes",moreMenu.Items.OfType<MenuItem>().All(item=>item.Icon is FluentIcon icon && icon.ActualWidth>0 && icon.ActualHeight>0 && Math.Max(icon.ActualWidth,icon.ActualHeight)>=18 && icon.Foreground is SolidColorBrush brush && brush.Color==((SolidColorBrush)item.Foreground).Color));
            var popupBitmap=new RenderTargetBitmap((int)Math.Ceiling(moreMenu.ActualWidth),(int)Math.Ceiling(moreMenu.ActualHeight),96,96,PixelFormats.Pbgra32);popupBitmap.Render(moreMenu);
            var popupEncoder=new PngBitmapEncoder();popupEncoder.Frames.Add(BitmapFrame.Create(popupBitmap));using(var popupFile=File.Create(Path.Combine(folder,"actual-soft-archive-more-menu.png")))popupEncoder.Save(popupFile);
            moreMenu.IsOpen=false;theme.SetMode("light");
            System.Diagnostics.ProcessStartInfo? launchedImage=null;
            var openedRoot=Path.Combine(data,"opened-images");using var openedImages=new PreviewImageFiles(openedRoot,info=>launchedImage=info);imagePreview.ImageOpener=openedImages.OpenAsync;
            Check("image open action is enabled only for a decoded image",imagePreview.OpenImageCommand.CanExecute(null));
            var openImageButton=VisualDescendants((DependencyObject)window.PreviewHost.Content).OfType<Button>().Single(button=>button.Name=="OpenPreviewButton");((System.Windows.Automation.Provider.IInvokeProvider)System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(openImageButton)!.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();await Until(()=>imagePreview.StatusText==UiText.Get("Preview.Opened"));
            byte[] archivedImage;using(var inputZip=ZipFile.OpenRead(archive))using(var input=inputZip.GetEntry("cover.png")!.Open())using(var buffer=new MemoryStream()){input.CopyTo(buffer);archivedImage=buffer.ToArray();}
            Check("open image hands exact archive bytes to the system association",launchedImage is {UseShellExecute:true,Verb:"open"} && File.Exists(launchedImage.FileName) && File.ReadAllBytes(launchedImage.FileName).SequenceEqual(archivedImage) && File.GetAttributes(launchedImage.FileName).HasFlag(FileAttributes.ReadOnly));
            imagePreview.ImageOpener=(_,_)=>throw new System.ComponentModel.Win32Exception("Synthetic association unavailable");
            ((System.Windows.Automation.Provider.IInvokeProvider)System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(openImageButton)!.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();await Until(()=>imagePreview.StatusText==UiText.Get("Preview.OpenFailed"));
            var previewStatus=VisualDescendants((DependencyObject)window.PreviewHost.Content).OfType<TextBlock>().Single(text=>text.Name=="PreviewNotice");
            Check("unavailable image association keeps the image and bounds its error text",imagePreview.IsImage && Equals(previewStatus.Foreground,Application.Current.Resources["ErrorBrush"]) && Bounds(previewStatus).Right<=Bounds(window.ArchivePreviewPane).Right && Bounds(previewStatus).Bottom<=Bounds(window.ArchivePreviewPane).Bottom);Capture("actual-preview-open-error");
            contents.Search="metadata";await Layout();
            window.MinWidth=800;window.Width=800;window.Height=600;await Layout();
            Check("minimum window retains the requested image preview",imagePreview.Picture is not null && window.ArchivePreviewPane.IsVisible && Math.Abs(window.PreviewColumn.Width.Value-300)<1);
            var closePreviewAtMinimum=VisualDescendants((DependencyObject)window.PreviewHost.Content).OfType<Button>().Single(value=>value.Name=="ClosePreviewButton");closePreviewAtMinimum.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            Check("opened image copy survives explicit preview close until application cleanup",File.Exists(launchedImage!.FileName) && !imagePreview.OpenImageCommand.CanExecute(null));openedImages.Dispose();
            Check("application image cleanup removes the opened temporary copy",!Directory.Exists(openedRoot));
            Check("explicit preview close clears decoded image",imagePreview.Picture is null && window.PreviewHost.Content is null && !window.ArchivePreviewPane.IsVisible);
            contents.Search="";contents.CurrentRow=contents.Rows.Single(row=>row.Path=="cover.png");await Layout();
            Check("closing preview releases thumbnails hidden by filtering",contents.Rows.All(row=>row.PreviewThumbnail is null));
            Check("compact browser keeps primary columns readable",entryTable.Columns[4].Visibility==Visibility.Collapsed && entryTable.Columns[2].ActualWidth>=90);
            Capture("actual-compact-browser");window.MinWidth=1152;window.Width=1152;window.Height=768;await Layout();
            contents.PreviewCommand.Execute(null);await Until(()=>!contents.IsWorking);await Layout();
            imagePreview=(ArchivePreviewViewModel)((FrameworkElement)window.PreviewHost.Content!).DataContext;contents.CurrentRow.Selected=false;
            model.Page=0;await Layout();Check("switching away clears decoded preview",preview.Text.Length==0 && imagePreview.Picture is null && window.PreviewHost.Content is null);model.Page=4;
            window.PrepareExtractionOptionsForValidation=dialog=>
            {
                dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
                {
                    Check("selected extraction confirmation names its actual scope",dialog.Title==UiText.Get("Extraction.SelectedTitle"));
                    foreach(var appearance in new[]{"light","dark"})
                    {theme.SetMode(appearance);dialog.UpdateLayout();dialog.Opacity=1;DesktopAccessibilityValidation.Render(dialog,Path.Combine(folder,"extraction-single-"+appearance+".png"),1,clientOnly:true);dialog.Opacity=0;}
                    theme.SetMode("light");
                    var start=Descendants((DependencyObject)dialog.Content).OfType<Button>().Single(value=>value.IsDefault);
                    start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
            };
            model.OutputPath=Path.Combine(data,"output");model.ConflictPolicy=1;
            contents.Rows.Single(value=>value.Path=="notes.txt").Selected=true;
            contents.ExtractSelectedCommand.Execute(null);await Until(()=>!contents.IsWorking);
            Check("selection extraction honors requested content",Directory.GetFiles(model.OutputPath,"notes.txt",SearchOption.AllDirectories).Length==1 && Directory.GetFiles(model.OutputPath,"other.txt",SearchOption.AllDirectories).Length==0);
            Capture("actual-selection-result");
            window.CloseArchiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await opening;
            Check("closing archive releases operation lifetime",model.IsIdle && window.ArchiveHost.Content is null);
            window.PrepareExtractionOptionsForValidation=null;

            var secondArchive=Path.Combine(data,"second.zip");File.Copy(archive,secondArchive);model.AddFiles([secondArchive]);
            var thirdArchive=Path.GetFullPath(Path.Combine(data,"third.zip"));File.Copy(archive,thirdArchive);model.AddFiles([thirdArchive]);var originalRules=model.DirectoryRules;
            window.PrepareExtractionOptionsForValidation=dialog=>
            {
                dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
                {
                    model.CreateArchiveFolder=true;dialog.UpdateLayout();
                    foreach(var appearance in new[]{"light","dark"}) {theme.SetMode(appearance);dialog.UpdateLayout();dialog.Opacity=1;DesktopAccessibilityValidation.Render(dialog,Path.Combine(folder,"extraction-batch-"+appearance+".png"),1,clientOnly:true);dialog.Opacity=0;}
                    var previewTexts=VisualDescendants((DependencyObject)window.ExtractionOutputPreview.Content).OfType<TextBlock>().Select(value=>value.Text).ToArray();
                    Check("batch preview shows rule-based archive destinations",previewTexts.Any(text=>text.Contains(Path.GetFullPath(Path.Combine(model.OutputPath,"sample")),StringComparison.Ordinal)) && previewTexts.Any(text=>text.Contains(Path.GetFullPath(Path.Combine(model.OutputPath,"third")),StringComparison.Ordinal)));
                    Check("batch resume explanation is fully visible without scrolling",window.ExtractionRetainHint.TransformToAncestor(window.SettingsScroll).TransformBounds(new Rect(window.ExtractionRetainHint.RenderSize)).Bottom<=window.SettingsScroll.ActualHeight+1);
                    var batchTable=VisualDescendants((DependencyObject)dialog.Content).OfType<DataGrid>().Single(value=>value.Name=="BatchArchiveTable");
                    var remove=VisualDescendants(batchTable).OfType<Button>().Single(value=>value.Tag as string=="RemoveBatchArchive" && value.DataContext is ArchiveTaskRow row && row.Path==thirdArchive);
                    remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));dialog.UpdateLayout();
                    Check("batch inline remove updates the actual queue and retains the archive",!model.Tasks.Any(row=>row.Path==thirdArchive) && File.Exists(thirdArchive) && batchTable.Items.Count==2);
                    model.ApplyDirectoryRules(originalRules,model.OutputPath);
                    theme.SetMode("light");dialog.Close();
                }));
            };
            Check("batch extraction confirmation can cancel without running",!window.ConfirmExtractionOptions() && model.IsIdle && model.Tasks.All(row=>!row.Running));window.PrepareExtractionOptionsForValidation=null;

            // Actual inline edits use the synthetic vault and preserve secret masking on navigation.
            var saved=new SavedPassword(Guid.NewGuid(),"Synthetic vault item","synthetic-secret","",true,DateTimeOffset.UtcNow);
            await model.SaveVaultDetailsAsync(saved);model.Page=1;await Layout();
            window.VaultDetails.NameBox.Text="Edited synthetic item";window.VaultDetails.GroupBox.Text="New synthetic group";window.VaultDetails.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>model.IsIdle);
            Check("inline details persist to synthetic vault",(await vault.ReadAsync()).Single().Name=="Edited synthetic item");
            Check("editable group picker saves new groups",(await vault.ReadAsync()).Single().Group=="New synthetic group");
            model.Page=0;await Layout();Check("inline editor clears secret after saving and leaving",window.VaultDetails.SecretBox.Password.Length==0);

            // Compression is visible on Tasks but never added to the extraction input collection.
            var source=Path.Combine(data,"source.txt");await File.WriteAllTextAsync(source,"synthetic compression content");
            var extraSource=Path.Combine(data,"extra-source.txt");await File.WriteAllTextAsync(extraSource,"second synthetic source");
            model.Page=3;model.AddCompressionSources([source,extraSource]);await Layout();
            var allSourcesPeer=new System.Windows.Automation.Peers.CheckBoxAutomationPeer(window.CompressionPage.SourceSelectAll);
            window.CompressionPage.SourceSelectAll.IsChecked=false;await Layout();
            ((System.Windows.Automation.Provider.IToggleProvider)allSourcesPeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();await Layout();
            Check("source select-all includes every displayed source",window.CompressionPage.CompressionSourceTable.SelectedItems.Count==2);
            Check("source header and selected row checkboxes retain all four edges",VisualDescendants(window.CompressionPage.CompressionSourceTable).OfType<CheckBox>().Where(value=>value.IsVisible).All(UnclippedSelectionBox));
            Capture("actual-compression-multiselection");
            var laterSource=Path.Combine(data,"later-source.txt");await File.WriteAllTextAsync(laterSource,"later addition");model.AddCompressionSources([laterSource]);await Layout();
            Check("adding a source after select-all marks the partial selection",window.CompressionPage.SourceSelectAll.IsChecked is null && window.CompressionPage.CompressionSourceTable.SelectedItems.Count==2 && model.CompressionSources.Count==3);
            var selectionWidth=window.Width;var selectionMinimum=window.MinWidth;window.MinWidth=800;window.Width=800;await Layout();
            Check("compact source header and partial selection remain unclipped",Math.Abs(window.ActualWidth-800)<1 && VisualDescendants(window.CompressionPage.CompressionSourceTable).OfType<CheckBox>().Where(value=>value.IsVisible).All(UnclippedSelectionBox));
            Capture("actual-compression-compact-partial-selection");window.MinWidth=selectionMinimum;window.Width=selectionWidth;await Layout();
            window.CompressionPage.SourceSelectAll.IsChecked=true;await Layout();
            ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(window.CompressionPage.CompressionRemoveButton).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();await Layout();
            Check("remove selected removes the full source selection",model.CompressionSources.Count==0);model.AddCompressionSources([source]);await Layout();
            window.CompressionPage.ArchiveOutputBox.Text=data;window.CompressionPage.ArchiveOutputBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            window.CompressionPage.ArchiveNameBox.Text="created";window.CompressionPage.ArchiveNameBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Check("separate location and filename compose the real output path",model.CompressionOutput==Path.Combine(data,"created.7z"));
            model.CompressionFormat=1;Check("format changes retain the entered name and align extension",model.CompressionOutput==Path.Combine(data,"created.zip") && model.CompressionOutputName=="created");model.CompressionFormat=0;
            model.CreateArchiveCommand.Execute(null);await Until(()=>model.IsIdle);
            Check("compression routes to unified task workspace",model.Page==0 && model.HasCompressionActivity && File.Exists(model.CompressionOutput));
            Check("compression output is not an extraction task",model.Tasks.All(value=>!string.Equals(value.Path,model.CompressionOutput,StringComparison.OrdinalIgnoreCase)));
            var missingOutput=new ArchiveTaskRow("missing.zip") {Completed=true,ResultOutputPath=Path.Combine(data,"missing-output")};
            var priorFooter=model.Footer;model.OpenTaskOutput(missingOutput);Check("row open reports missing output instead of opening another task",model.Footer==UiText.Get("Redesign.OutputUnavailable"));model.Footer=priorFooter;
            var compressionTask=model.WorkspaceTasks.Single(value=>value.IsCompression);
            Check("task selection survives completion state refresh",ReferenceEquals(model.SelectedTask,compressionTask));
            Check("compression appears exactly once in the unified table",model.TaskView.Cast<ArchiveTaskRow>().Count(value=>value.IsCompression)==1 && model.TaskView.Cast<ArchiveTaskRow>().Count()==model.Tasks.Count+1);
            Check("compression row preserves its actual result",compressionTask.Completed && string.Equals(Path.GetFullPath(compressionTask.Path),Path.GetFullPath(model.CompressionOutput),StringComparison.OrdinalIgnoreCase) && File.Exists(compressionTask.Path) && Directory.Exists(compressionTask.LatestOutputPath));
            model.SelectedTask=compressionTask;Check("compression row cannot invoke extract selected",!model.ExtractSelectedTaskCommand.CanExecute(null));
            model.TaskFilterIndex=3;Check("ended filter includes completed compression",model.TaskView.Contains(compressionTask));model.TaskFilterIndex=2;Check("pending filter excludes completed compression",!model.TaskView.Contains(compressionTask));model.TaskFilterIndex=0;
            await Layout();
            var openCompression=VisualDescendants(window.ArchiveTasks).OfType<Button>().Single(value=>ReferenceEquals(value.DataContext,compressionTask) && Equals(value.Tag,"TaskPrimary"));openCompression.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("compression row opens only its actual output folder",openedResults.LastOrDefault() is {Length:1} opened && opened[0]==compressionTask.LatestOutputPath);
            model.SelectedTask=null;model.OpenOutputCommand.Execute(null);
            Check("global output action includes completed compression",openedResults.LastOrDefault()?.Contains(compressionTask.LatestOutputPath!)==true);model.SelectedTask=compressionTask;await Layout();
            Capture("actual-compression-result");

            model.Page=5;await Layout();
            var hashes=(FileHashViewModel)((FrameworkElement)window.ToolsHost.Content).DataContext;
            hashes.AddFiles([archive,secondArchive,source]);hashes.CalculateCommand.Execute(null);await hashes.WaitForIdleAsync();await Layout();
            Check("checksum workspace computes actual synthetic files",hashes.Rows.Count==3 && hashes.Rows.All(value=>value.Digest.Length==64));
            Check("empty expected digest remains not compared",hashes.Rows.All(value=>value.Status==UiText.Get("T_cb82f06947d1")));
            var expectedBox=VisualDescendants((DependencyObject)window.ToolsHost.Content).OfType<TextBox>().Single(value=>value.Name=="HashExpectedBox");
            expectedBox.Text="invalid";expectedBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();await Layout();
            var expectedFocus=(Border)expectedBox.Template.FindName("FieldFocus",expectedBox);
            Check("invalid digest marks its field and prevents calculation",FieldState.GetHasError(expectedBox) && expectedFocus.Visibility==Visibility.Visible && ReferenceEquals(expectedFocus.BorderBrush,Application.Current.Resources["ErrorBrush"]) && !hashes.CalculateCommand.CanExecute(null));
            Capture("actual-checksum-invalid-expected");
            expectedBox.Text=hashes.Selected!.Digest;expectedBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Check("checksum details compare the selected value",hashes.Selected.Status==UiText.Get("T_0680a4a36ca8"));
            Check("checksum summary updates when expected digest changes",hashes.Message==UiText.Format("T_98b68837365d",UiText.Get("T_f28461bb49c8"),3,1,0,0));
            await Layout();
            Capture("actual-checksum-matched");
            expectedBox.Text=new string(hashes.Selected.Digest[0]=='0'?'1':'0',64);expectedBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Check("checksum mismatch is not reported as success",hashes.Selected.Status==UiText.Get("T_a50309ed2709"));Check("checksum summary reflects current mismatch",hashes.Message==UiText.Format("T_98b68837365d",UiText.Get("T_f28461bb49c8"),3,0,1,0));await Layout();Capture("actual-checksum-mismatch");
            model.Page=2;model.SettingsSection=0;await Layout();
            var settingsPath=Path.Combine(data,"settings.json");var priorSettings=await File.ReadAllTextAsync(settingsPath);
            var shellDestination=Path.Combine(data,"shell-specific");var shellTask=new ArchiveTaskRow(archive){ShellOutputPath=shellDestination};model.Tasks.Add(shellTask);
            window.DefaultOutputBox.Text="relative-output";window.DefaultOutputBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            model.SaveFileDefaultsCommand.Execute(null);await Until(()=>model.IsIdle);
            Check("invalid default output does not replace saved settings",model.FileDefaultsMessage==UiText.Get("Settings.InvalidOutput") && await File.ReadAllTextAsync(settingsPath)==priorSettings);
            await Layout();Check("invalid default output has a visible field error",FieldState.GetHasError(window.DefaultOutputBox));Capture("actual-file-defaults-error");
            var defaultOutput=Path.Combine(data,"default-output");window.DefaultOutputBox.Text=defaultOutput;window.DefaultOutputBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();window.DefaultConflictChoice.SelectedIndex=2;
            await Layout();
            ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(window.SaveFileDefaultsAction).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Layout();await Until(()=>model.IsIdle);
            using(var savedSettings=JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath)))
                Check("general defaults save the chosen path and conflict policy",savedSettings.RootElement.GetProperty("OutputPath").GetString()==defaultOutput && savedSettings.RootElement.GetProperty("ConflictPolicy").GetInt32()==2);
            Check("changing defaults preserves queued shell destinations",shellTask.ShellOutputPath==shellDestination);
            var restoredDefaults=new MainViewModel(AppContext.BaseDirectory,data,vault,new WindowDialogs(window));await restoredDefaults.InitializeAsync();
            Check("file defaults survive reload",restoredDefaults.DefaultOutputDirectory==defaultOutput && restoredDefaults.ConflictPolicy==2 && restoredDefaults.DirectoryRules.Location==ExtractionLocation.Custom);
            await Layout();Capture("actual-file-defaults-saved");
            var passwordStage=0;
            var compressionDialogs=new WindowDialogs(window,prepareDialog:dialog=>
            {
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
                {
                    if(passwordStage==0){dialog.SecretBox.Password="synthetic-settings-password";dialog.ConfirmationBox.Password=dialog.SecretBox.Password;dialog.EncryptHeadersBox.IsChecked=false;dialog.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                    else{if(passwordStage==2)Check("ZIP password settings omit unsupported filename encryption",!dialog.CompressionHeadersPanel.IsVisible && dialog.CompressionHeaderEncryption is null);dialog.EncryptHeadersBox.IsChecked=true;dialog.Close();}
                }));
            });
            var passwordSettingsModel=new MainViewModel(AppContext.BaseDirectory,data,vault,compressionDialogs);await passwordSettingsModel.InitializeAsync();passwordSettingsModel.Page=3;
            Check("setting a password is available before encryption is enabled",!passwordSettingsModel.CompressionEncrypt && passwordSettingsModel.SetCompressionPasswordCommand.CanExecute(null));
            passwordSettingsModel.SetCompressionPasswordCommand.Execute(null);await Until(()=>passwordSettingsModel.IsIdle);
            var passwordSummary=passwordSettingsModel.CompressionPasswordSummary;
            Check("accepted password settings enable encryption and apply header choice",passwordSettingsModel.CompressionEncrypt && !passwordSettingsModel.CompressionHeaders && passwordSummary!=UiText.Get("Redesign.PasswordNotSet"));
            passwordStage=1;passwordSettingsModel.SetCompressionPasswordCommand.Execute(null);await Until(()=>passwordSettingsModel.IsIdle);
            Check("cancel preserves prior password and header settings",passwordSettingsModel.CompressionPasswordSummary==passwordSummary && !passwordSettingsModel.CompressionHeaders);
            passwordStage=2;passwordSettingsModel.CompressionFormat=1;passwordSettingsModel.SetCompressionPasswordCommand.Execute(null);await Until(()=>passwordSettingsModel.IsIdle);
            await DesktopButtonStateValidation.RunAsync(window,model,theme,folder,Check);
            await DesktopArchiveWorkspaceValidation.RunAsync(window,theme,folder,Check);
            await DesktopVaultPresentationValidation.RunAsync(window,theme,folder,Check);
            DesktopDesignDialogValidation.Run(window,theme,folder,Check);
            await DesktopReferenceVisualValidation.RunAsync(window,theme,root,folder,Check);
        }
        catch(Exception exception) {error=exception.ToString();}
        finally {window.PrepareExtractionOptionsForValidation=null;}
        DesktopTypographyValidation.Save(folder,Check);
        var passed=error is null && checks.Count>100 && checks.Values.All(value=>value);
        await File.WriteAllTextAsync(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new {passed,checks,error,viewports,scope="isolated CI; synthetic data; rendered WPF; contrast resource simulation; requested window widths can be clamped by the runner display, actual client sizes are recorded; no physical-display or Narrator audio acceptance"},new JsonSerializerOptions{WriteIndented=true}));
        return passed;
    }
    private static bool UnclippedSelectionBox(CheckBox choice)
    {
        choice.ApplyTemplate();
        if(choice.Template.FindName("Box",choice) is not Border box || Math.Abs(box.ActualWidth-18)>.1 || Math.Abs(box.ActualHeight-18)>.1)return false;
        FrameworkElement? container=choice;
        for(var ancestor=VisualTreeHelper.GetParent(choice);ancestor is not null;ancestor=VisualTreeHelper.GetParent(ancestor))
            if(ancestor is DataGridCell or System.Windows.Controls.Primitives.DataGridColumnHeader){container=(FrameworkElement)ancestor;break;}
        var bounds=box.TransformToAncestor(container).TransformBounds(new Rect(box.RenderSize));
        return bounds.Left>=-.1 && bounds.Top>=-.1 && bounds.Right<=container.ActualWidth+.1 && bounds.Bottom<=container.ActualHeight+.1;
    }
    internal static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
        {var child=VisualTreeHelper.GetChild(parent,index);yield return child;foreach(var nested in VisualDescendants(child))yield return nested;}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) {yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
