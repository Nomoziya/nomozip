using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopPasswordMatchValidation
{
    internal static async Task RunAsync(MainWindow window,ThemeResources theme,string data,string baseDirectory,
        Action<string,bool> check,Action<Window,string> capture)
    {
        data=Path.GetFullPath(Path.Combine(data,"match-rules"));Directory.CreateDirectory(data);
        var archive=Path.Combine(data,"source","child","Trip.zip");var vaultPath=Path.Combine(data,"synthetic.apv");
        var vault=new DpapiPasswordVault(vaultPath);var created=DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var associated=new SavedPassword(Guid.NewGuid(),"Previously associated password for quarterly document backups","synthetic-one","",true,created);
        var folder=new SavedPassword(Guid.NewGuid(),"Directory","synthetic-two","",true,created.AddDays(1),created.AddDays(5),"Archive",MatchDirectory:Path.Combine(data,"source"));
        var named=new SavedPassword(Guid.NewGuid(),"Filename","synthetic-three","",true,created.AddDays(2),created.AddDays(4),MatchText:"trip");
        var duplicate=folder with{Id=Guid.NewGuid(),Name="Duplicate",CreatedAt=created.AddDays(9)};
        var disabled=folder with{Id=Guid.NewGuid(),Name="Disabled",Secret="synthetic-four",AutoTry=false};
        var fallback=new SavedPassword(Guid.NewGuid(),"Fallback","synthetic-five","",true,created.AddDays(3));
        foreach(var record in new[]{associated,folder,named,duplicate,disabled,fallback})await vault.SaveAsync(record);
        await vault.RecordSuccessAsync(associated.Id,archive);var before=await File.ReadAllBytesAsync(vaultPath);
        var provider=new PasswordCandidateProvider(vault);var plan=await provider.ExplainAsync(archive);var actual=await provider.GetAsync(archive);
        check("matching explanation uses exact live candidate order and deduplication",plan.Entries.Select(row=>row.Id).SequenceEqual(actual.Select(row=>row.SavedId!.Value)) && plan.Entries.Select(row=>row.Id).SequenceEqual(new[]{associated.Id,folder.Id,named.Id,fallback.Id}) && plan.DisabledCount==1 && plan.DuplicateCount==1);
        check("matching explanation labels association directory and filename",plan.Entries[0].Associated && plan.Entries[1].DirectoryMatch && !plan.Entries[1].NameMatch && plan.Entries[2].NameMatch);
        var outside=await provider.ExplainAsync(Path.Combine(data,"source-other","Trip.zip"));
        check("matching directory boundary excludes similarly named siblings",!outside.Entries.Single(row=>row.Id==folder.Id).DirectoryMatch && !outside.Entries.Any(row=>row.Associated));
        check("matching DTO contains no secret or raw password record",typeof(PasswordMatchEntry).GetProperties().All(property=>property.Name is not ("Secret" or "Password") && property.PropertyType!=typeof(SavedPassword)));
        var recent=new PasswordMatchViewModel(archive,new([new(1,Guid.NewGuid(),"Recent fallback","",false,false,false,created.AddDays(10))],0,0),true).Rows.Single();
        check("matching recent fallback separates success history without claiming creation priority",recent.Reason==UiText.Get("PasswordMatch.Recent") && recent.History.Length>0);
        Exception? failure=null;var shown=false;
        var dialogs=new WindowDialogs(window,selectPasswordMatchArchive:()=>archive,preparePasswordMatches:dialog=>
        {
            dialog.Opacity=0;dialog.ShowActivated=false;dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
            {
                try
                {
                    shown=true;var view=(PasswordMatchViewModel)dialog.DataContext;
                    check("matching dialog displays redacted order and disabled auto-try notice",view.Rows.Count==4 && view.Notice==UiText.Get("PasswordMatch.AutoTryOff") && view.Rows[0].Reason.Contains(UiText.Get("PasswordMatch.Associated")));
                    foreach(var mode in new[]{"light","dark","high-contrast"})foreach(var width in new[]{820d,512d})
                    {
                        theme.SetMode(mode=="dark"?"dark":"light");theme.Apply(mode=="high-contrast");dialog.MinWidth=0;dialog.MinHeight=0;dialog.Width=width;dialog.Height=width==512?360:560;
                        await dialog.Dispatcher.InvokeAsync(dialog.UpdateLayout,DispatcherPriority.ApplicationIdle);
                        var bounds=dialog.CloseMatchButton.TransformToAncestor(dialog).TransformBounds(new Rect(dialog.CloseMatchButton.RenderSize));
                        check($"matching dialog remains readable and closable {mode} {width}",dialog.MatchTable.ActualHeight>=80 && bounds.Bottom<=dialog.ActualHeight && dialog.CloseMatchButton.IsVisible);
                        dialog.MatchTable.ScrollIntoView(view.Rows[0]);await dialog.Dispatcher.InvokeAsync(dialog.UpdateLayout,DispatcherPriority.ApplicationIdle);
                        var displayed=DesktopReadabilityValidation.Descendants(dialog.MatchTable).OfType<TextBlock>().ToArray();
                        check($"matching record name and primary reason remain complete {mode} {width}",new[]{view.Rows[0].Name,view.Rows[0].Reason,view.Rows[0].History}.All(value=>displayed.Any(text=>text.Text==value && DesktopReadabilityValidation.CompleteText(text))));
                        capture(dialog,$"next-password-match-{mode}-{width}");
                    }
                }
                catch(Exception error){failure=error;}
                finally{dialog.Close();}
            }));
        });
        var model=new MainViewModel(baseDirectory,data,vault,dialogs);await model.InitializeAsync();model.AutoTry=false;window.DataContext=model;model.Page=1;
        await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        var menu=await DesktopMenuValidation.OpenAsync(window,window.VaultMoreAction);
        check("saved-rule preview is reachable through vault menu",menu.Items.OfType<MenuItem>().Any(item=>ReferenceEquals(item.Command,model.ExplainPasswordMatchesCommand) && DesktopMenuValidation.Reachable(item)));menu.IsOpen=false;
        check($"matching preview action enabled (idle={model.IsIdle}, locked={model.VaultLocked}, records={model.PasswordRows.Count}, banner={model.Banner})",model.ExplainPasswordMatchesCommand.CanExecute(null));
        var resourceProbe=new PasswordMatchDialog(new(archive,plan,false));resourceProbe.Close();
        await DesktopMenuValidation.InvokeAsync(window,window.VaultMoreAction,model.ExplainPasswordMatchesCommand);await model.WaitForIdleAsync();if(failure is not null)throw failure;
        var unchanged=Enumerable.SequenceEqual(before,await File.ReadAllBytesAsync(vaultPath));var extracted=Directory.Exists(Path.GetDirectoryName(archive));
        check($"matching preview opens and never writes or extracts (shown={shown}, unchanged={unchanged}, extracted={extracted}, banner={model.Banner})",shown && unchanged && !extracted);
        await vault.EnableMasterPasswordAsync("synthetic-master-password");await vault.LockAsync();await model.InitializeAsync();
        check("locked vault disables matching preview",model.VaultLocked && !model.ExplainPasswordMatchesCommand.CanExecute(null));
        theme.SetMode("light");theme.Apply(false);
    }
}
