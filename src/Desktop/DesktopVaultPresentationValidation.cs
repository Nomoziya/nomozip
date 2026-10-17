using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

// Synthetic profiles and UI input are restricted to the existing isolated CI entry point.
internal static class DesktopVaultPresentationValidation
{
    internal static async Task RunAsync(MainWindow window, ThemeResources theme, string folder, Action<string,bool> check)
    {
        var original = window.DataContext;
        var data = Path.Combine(folder, "synthetic", "vault-presentation");
        Directory.CreateDirectory(data);
        var vault = new DpapiPasswordVault(Path.Combine(data, "passwords.apv"));
        var now = DateTimeOffset.Now;
        var records = new[] {
            new SavedPassword(Guid.NewGuid(), "常用资源密码", "synthetic-only", "", true, now, now),
            new SavedPassword(Guid.NewGuid(), "项目归档", "synthetic-only-2", "", true, now, now.AddDays(-1), Group:"工作"),
            new SavedPassword(Guid.NewGuid(), "家庭备份", "synthetic-only-3", "", true, now, now.AddDays(-3)),
            new SavedPassword(Guid.NewGuid(), "旧资料", "synthetic-only-4", "", false, now),
            new SavedPassword(Guid.NewGuid(), "素材下载", "synthetic-only-5", "", true, now, now.AddDays(-5)) };
        foreach (var record in records) await vault.SaveAsync(record);
        const string master = "Synthetic master password 2026";
        var entered = master;
        var dialogs = new WindowDialogs(window, prepareMaster: dialog =>
        {
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;
            dialog.ShowActivated=false;dialog.Opacity=0;
            dialog.Loaded += (_,_) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                dialog.CurrentBox.Password=entered;dialog.NewBox.Password=master;dialog.ConfirmBox.Password=master;
                Invoke(dialog.ContinueButton);
            }));
        });
        var model = new MainViewModel(AppContext.BaseDirectory, data, vault, dialogs);
        async Task Layout() => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        async Task Execute(Button button)
        {
            Invoke(button);await Layout();await model.WaitForIdleAsync();await Layout();
        }
        void Capture(string name)
        {
            window.Opacity=1;DesktopAccessibilityValidation.Render(window,Path.Combine(folder,name+".png"),1,clientOnly:true);window.Opacity=0;
        }
        try
        {
            window.DataContext=model;await model.InitializeAsync();model.Page=1;
            window.MinWidth=1152;window.Width=1152;window.Height=768;theme.SetMode("light");theme.Apply(false);await Layout();
            check("vault local protection never claims master unlock",!model.VaultMasterEnabled && window.VaultProtectAction.IsVisible && !window.VaultLockAction.IsVisible && window.VaultProtectionStatusText.Text==UiText.Get("Vault.StatusLocal"));
            Capture("vault-local-protection");
            await Execute(window.VaultProtectAction);
            check("vault set-master action exposes actual unlocked state and lock",model.VaultMasterEnabled && !model.VaultLocked && window.VaultLockAction.IsVisible && !window.VaultUnlockAction.IsVisible && window.VaultProtectionStatusText.Text==UiText.Get("Vault.StatusUnlocked"));
            check("vault protection notice uses footer without covering caption or shifting header",!window.WorkspaceBanner.IsVisible && window.GlobalStatus.Text==model.Banner && Bounds(window.VaultProtectionStatus).Top<100);

            var converter = new PasswordLastUsedConverter();
            var fixedNow = new DateTimeOffset(new DateTime(2026,10,18,16,0,0,DateTimeKind.Local));
            check("vault relative dates cover today yesterday and older dates",
                PasswordLastUsedConverter.Format(fixedNow.AddMinutes(-100),fixedNow)==(UiText.LanguageCode=="en"?"Today 14:20":"今天 14:20") &&
                PasswordLastUsedConverter.Format(fixedNow.AddDays(-1),fixedNow)==UiText.Get("Vault.UsedYesterday") &&
                PasswordLastUsedConverter.Format(fixedNow.AddDays(-6),fixedNow)==(UiText.LanguageCode=="en"?"Oct 12":"10月12日") &&
                PasswordLastUsedConverter.Format(fixedNow.AddYears(-1),fixedNow)=="2025-10-18");
            var midnight = new DateTimeOffset(new DateTime(2026,1,1,0,15,0,DateTimeKind.Local));
            check("vault dates follow local calendar across midnight year and offset",
                PasswordLastUsedConverter.Format(midnight.AddMinutes(-30).ToUniversalTime(),midnight)==UiText.Get("Vault.UsedYesterday") &&
                PasswordLastUsedConverter.Format(fixedNow.ToOffset(TimeSpan.FromHours(-7)),fixedNow)==(UiText.LanguageCode=="en"?"Today 16:00":"今天 16:00"));
            check("vault unused and exact local timestamp remain available",
                Equals(converter.Convert(null!,typeof(string),null!,CultureInfo.CurrentCulture),UiText.Get("T_9bd11dda28fc")) &&
                Equals(converter.Convert(records[0].LastSuccessAt!,typeof(string),"Full",CultureInfo.CurrentCulture),now.ToString("yyyy-MM-dd HH:mm:ss zzz",CultureInfo.CurrentCulture)));
            foreach (var mode in new[]{"light","dark","contrast"})
            {
                theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");
                foreach (var width in new[]{1152,800})
                {
                    window.MinWidth=width;window.Width=width;window.Height=width==800?600:768;model.SelectedPassword=model.PasswordRows[0];await Layout();
                    var visibleTimes=Descendants(window.PasswordTable).OfType<TextBlock>().Where(text=>text.DataContext is SavedPassword && text.ToolTip is string tip && tip.Contains(':')).ToArray();
                    check($"vault {mode}/{width} every recent-use value is readable with exact tooltip",visibleTimes.Length==4 && visibleTimes.All(text=>
                        new FormattedText(text.Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,text.Foreground,1).WidthIncludingTrailingWhitespace<=text.ActualWidth+1));
                    check($"vault {mode}/{width} state lock and last-use column remain on screen",window.VaultLockAction.IsVisible && Bounds(window.VaultProtectionStatus).Right<=Bounds(window.VaultLockAction).Left+1 && Bounds(window.VaultLockAction).Right<=window.ActualWidth && window.PasswordLastUsedColumn.ActualWidth>=123 && window.PasswordTable.Columns.Sum(column=>column.ActualWidth)<=window.PasswordTable.ActualWidth+1);
                    window.VaultLockAction.Focus();await Layout();Capture($"vault-unlocked-{mode}-{width}");
                }
            }
            theme.SetMode("light");theme.Apply(false);window.MinWidth=1152;window.Width=1152;window.Height=768;await Layout();
            await Execute(window.VaultLockAction);
            check("vault lock action clears list selected secret and exposes unlock",model.VaultLocked && model.PasswordRows.Count==0 && model.SelectedPassword is null && window.VaultDetails.SecretBox.Password.Length==0 && window.VaultUnlockAction.IsVisible && !window.VaultLockAction.IsVisible && window.VaultProtectionStatusText.Text==UiText.Get("Vault.StatusLocked"));
            Capture("vault-locked-light-1152");
            window.MinWidth=800;window.Width=800;window.Height=600;await Layout();Capture("vault-locked-light-800");
            entered="Wrong synthetic password";await Execute(window.VaultUnlockAction);
            check("vault wrong password preserves locked state and empty rows",model.VaultLocked && model.PasswordRows.Count==0 && window.VaultUnlockAction.IsVisible);
            entered=master;await Execute(window.VaultUnlockAction);
            check("vault correct unlock restores rows without changing recent use",!model.VaultLocked && model.PasswordRows.Count==5 && (await vault.ReadAsync()).All(record=>record.LastSuccessAt==records.Single(before=>before.Id==record.Id).LastSuccessAt));
            await vault.RecordSuccessAsync(records[3].Id,Path.Combine(data,"synthetic-success.zip"));
            await Execute(window.VaultLockAction);await Execute(window.VaultUnlockAction);
            var refreshed=model.PasswordRows.Single(record=>record.Id==records[3].Id);
            check("vault recorded success persists and refreshes previously unused row",refreshed.LastSuccessAt is not null && PasswordLastUsedConverter.Format(refreshed.LastSuccessAt.Value,DateTimeOffset.Now).StartsWith(UiText.Format("Vault.UsedToday",""),StringComparison.Ordinal));
        }
        finally
        {
            await model.ReleaseVaultSessionAsync();window.DataContext=original;theme.SetMode("light");theme.Apply(false);
            window.MinWidth=1152;window.Width=1152;window.Height=768;
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
