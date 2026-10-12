using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;

namespace ArchivePassword.Desktop;

public partial class VaultBackupDialog : Window
{
    private readonly bool exporting;
    public string? Passphrase { get; private set; }
    public VaultBackupDialog(bool exporting, string path)
    {
        InitializeComponent(); this.exporting = exporting;
        Title = Heading.Text = exporting ? "导出加密备份" : "打开加密备份";
        ContinueButton.Content = exporting ? "导出备份" : "预览密码";
        Description.Text = exporting
            ? "为此备份设置至少 12 个字符的密码，可使用中文。换电脑或换 Windows 用户后，凭此密码导入。请记住备份密码，忘记后无法恢复。"
            : "输入导出时设置的备份密码。下一步预览记录并选择重复项处理方式，确认后才写入密码库。";
        FileLabel.Text = "备份文件：" + Path.GetFileName(path); FileLabel.ToolTip = path;
        ConfirmationPanel.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
        WindowPlacement.FitWorkArea(this, SystemParameters.WorkArea);
        Loaded += (_, _) => SecretBox.Focus();
        Closed += (_, _) => { SecretBox.Clear(); ConfirmationBox.Clear(); };
    }
    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SecretBox.Password) || exporting && SecretBox.Password.Length < 12)
        {
            ShowError(exporting ? "备份密码至少需要 12 个字符。" : "请输入备份密码。"); SecretBox.Focus(); return;
        }
        if (exporting && SecretBox.Password != ConfirmationBox.Password)
        {
            ShowError("两次输入的备份密码不一致。"); ConfirmationBox.Focus(); return;
        }
        Passphrase = SecretBox.Password; DialogResult = true;
    }
    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        UIElementAutomationPeer.CreatePeerForElement(ErrorLabel)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void SecretChanged(object sender, RoutedEventArgs e) { if (ErrorLabel is not null) ErrorLabel.Text = ""; }
}
