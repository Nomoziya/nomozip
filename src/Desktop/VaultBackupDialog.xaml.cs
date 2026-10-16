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
        SecretReveal.Configure(this,RevealToggle,(SecretBox,VisibleSecret),(ConfirmationBox,VisibleConfirmation));
        Title = Heading.Text = exporting ? UiText.Get("T_61e8ff181802") : UiText.Get("T_e2a88581bf88");
        ContinueButton.Content = exporting ? UiText.Get("T_e9d3dd690a4e") : UiText.Get("T_77c11f78b11a");
        Description.Text = exporting
            ? UiText.Get("T_2b219f77f72a")
            : UiText.Get("T_815a722179ba");
        FileLabel.Text = UiText.Get("T_980e907463c6") + Path.GetFileName(path); FileLabel.ToolTip = path;
        ConfirmationPanel.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => SecretBox.Focus();
        Closed += (_, _) => { SecretBox.Clear(); ConfirmationBox.Clear(); };
    }
    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SecretBox.Password) || exporting && SecretBox.Password.Length < 12)
        {
            ShowError(exporting ? UiText.Get("T_652879ddcefd") : UiText.Get("T_77316765ebb8"));FieldState.SetHasError(SecretBox,true); SecretReveal.Focus(SecretBox); return;
        }
        if (exporting && SecretBox.Password != ConfirmationBox.Password)
        {
            ShowError(UiText.Get("T_f007e1890a6a"));FieldState.SetHasError(ConfirmationBox,true); SecretReveal.Focus(ConfirmationBox); return;
        }
        Passphrase = SecretBox.Password; DialogResult = true;
    }
    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        UIElementAutomationPeer.CreatePeerForElement(ErrorLabel)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void SecretChanged(object sender, RoutedEventArgs e)
    {
        if(ErrorLabel is null)return;
        ErrorLabel.Text="";
        foreach(var field in new[]{SecretBox,ConfirmationBox})if(field is not null)FieldState.SetHasError(field,false);
    }
}
