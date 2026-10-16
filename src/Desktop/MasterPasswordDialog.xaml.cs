using System.Windows;
using System.Windows.Automation.Peers;

namespace ArchivePassword.Desktop;
public enum MasterPasswordAction { Unlock, Enable, Change, Disable, Recover }
public sealed record MasterPasswordResponse(string Current, string Replacement);
public partial class MasterPasswordDialog : Window
{
    private readonly MasterPasswordAction action;
    public MasterPasswordResponse? Response { get; private set; }
    public MasterPasswordDialog(MasterPasswordAction action)
    {
        InitializeComponent(); this.action=action;
        Title=Heading.Text=action switch {MasterPasswordAction.Unlock=>UiText.Get("T_1109737d1829"),MasterPasswordAction.Enable=>UiText.Get("T_440f5cfd4823"),MasterPasswordAction.Change=>UiText.Get("T_6b444e0f3275"),MasterPasswordAction.Disable=>UiText.Get("T_aa72684414b8"),_=>UiText.Get("T_7eb10fe1ce8c")};
        ContinueButton.Content=action switch {MasterPasswordAction.Unlock=>UiText.Get("T_70fba17fffb7"),MasterPasswordAction.Disable=>UiText.Get("T_aa72684414b8"),MasterPasswordAction.Recover=>UiText.Get("T_6e38ffe34b8b"),_=>UiText.Get("T_50fcfc7f7af4")};
        Description.Text=action==MasterPasswordAction.Unlock?UiText.Get("T_6d5d0fcd5f97"):action==MasterPasswordAction.Disable?UiText.Get("T_4b67ce3665b0"):UiText.Get("T_edb93ad988fa");
        CurrentPanel.Visibility=action is MasterPasswordAction.Enable or MasterPasswordAction.Recover?Visibility.Collapsed:Visibility.Visible;
        NewPanel.Visibility=action is MasterPasswordAction.Unlock or MasterPasswordAction.Disable?Visibility.Collapsed:Visibility.Visible;
        Loaded+=(_,_)=>{if(CurrentPanel.Visibility==Visibility.Visible)CurrentBox.Focus();else NewBox.Focus();};
        Closed+=(_,_)=>{CurrentBox.Clear();NewBox.Clear();ConfirmBox.Clear();};
    }
    private void OnContinue(object sender,RoutedEventArgs e)
    {
        if(CurrentPanel.Visibility==Visibility.Visible && CurrentBox.Password.Length==0){Error(UiText.Get("T_0b0191c79534"),CurrentBox);return;}
        if(NewPanel.Visibility==Visibility.Visible && (NewBox.Password.Length<12 || NewBox.Password.Length>16384)){Error(UiText.Get("T_bcf34c82614c"),NewBox);return;}
        if(NewPanel.Visibility==Visibility.Visible && NewBox.Password!=ConfirmBox.Password){Error(UiText.Get("T_d64ffabdbf39"),ConfirmBox);return;}
        Response=new(CurrentBox.Password,NewBox.Password);DialogResult=true;
    }
    private void Error(string text,UIElement field){ErrorLabel.Text=text;FieldState.SetHasError(field,true);field.Focus();UIElementAutomationPeer.CreatePeerForElement(ErrorLabel)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);}
    private void OnPasswordChanged(object sender,RoutedEventArgs e)
    {
        if(ErrorLabel is null)return;
        ErrorLabel.Text="";
        foreach(var field in new[]{CurrentBox,NewBox,ConfirmBox})if(field is not null)FieldState.SetHasError(field,false);
    }
}
