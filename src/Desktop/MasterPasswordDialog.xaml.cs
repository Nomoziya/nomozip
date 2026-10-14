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
        Title=Heading.Text=action switch {MasterPasswordAction.Unlock=>"解锁密码库",MasterPasswordAction.Enable=>"开启主密码",MasterPasswordAction.Change=>"更改主密码",MasterPasswordAction.Disable=>"关闭主密码",_=>"为恢复的密码库设置主密码"};
        ContinueButton.Content=action switch {MasterPasswordAction.Unlock=>"解锁",MasterPasswordAction.Disable=>"关闭主密码",MasterPasswordAction.Recover=>"恢复并加密",_=>"保存主密码"};
        Description.Text=action==MasterPasswordAction.Unlock?"输入主密码后才可读取和自动尝试密码库。取消后仍可手动输入解压密码。":action==MasterPasswordAction.Disable?"需要当前主密码。关闭后，本机密码改由当前 Windows 用户加密保护。":"主密码至少 12 个字符，可使用中文。忘记后只能通过事先导出的独立加密备份及其备份密码恢复；没有重置通道。";
        CurrentPanel.Visibility=action is MasterPasswordAction.Enable or MasterPasswordAction.Recover?Visibility.Collapsed:Visibility.Visible;
        NewPanel.Visibility=action is MasterPasswordAction.Unlock or MasterPasswordAction.Disable?Visibility.Collapsed:Visibility.Visible;
        Loaded+=(_,_)=>{if(CurrentPanel.Visibility==Visibility.Visible)CurrentBox.Focus();else NewBox.Focus();};
        Closed+=(_,_)=>{CurrentBox.Clear();NewBox.Clear();ConfirmBox.Clear();};
    }
    private void OnContinue(object sender,RoutedEventArgs e)
    {
        if(CurrentPanel.Visibility==Visibility.Visible && CurrentBox.Password.Length==0){Error("请输入当前主密码。",CurrentBox);return;}
        if(NewPanel.Visibility==Visibility.Visible && (NewBox.Password.Length<12 || NewBox.Password.Length>16384)){Error("新主密码需要 12 至 16384 个字符。",NewBox);return;}
        if(NewPanel.Visibility==Visibility.Visible && NewBox.Password!=ConfirmBox.Password){Error("两次输入的新主密码不一致。",ConfirmBox);return;}
        Response=new(CurrentBox.Password,NewBox.Password);DialogResult=true;
    }
    private void Error(string text,UIElement field){ErrorLabel.Text=text;field.Focus();UIElementAutomationPeer.CreatePeerForElement(ErrorLabel)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);}
    private void OnPasswordChanged(object sender,RoutedEventArgs e){if(ErrorLabel is not null)ErrorLabel.Text="";}
}
