using System.Windows;
using System.Windows.Controls;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public partial class PasswordDialog : Window
{
    private readonly bool testing;
    private readonly SavedPassword? original;
    private readonly bool editing;
    private readonly bool compressing;
    private readonly string archiveName;
    private readonly EngineResult? previousFailure;
    private readonly Action<ExtractionFailureDialog>? prepareFailure;
    public PasswordPromptResponse? Response { get; private set; }
    public SavedPassword? Edited { get; private set; }
    public bool? CompressionHeaderEncryption=>CompressionHeadersPanel.Visibility==Visibility.Visible?EncryptHeadersBox.IsChecked==true:null;
    public PasswordDialog(string archiveName, bool failed, IReadOnlyList<SavedPassword> saved, bool canSave, SavedPassword? existing = null, bool editing = false, bool compressing = false, bool browsing = false, EngineResult? previousFailure = null, Action<ExtractionFailureDialog>? prepareFailure = null,bool testing=false,bool? encryptHeaders=null)
    {
        InitializeComponent(); original = existing; this.editing = editing;
        this.compressing=compressing;
        this.testing=testing;
        this.archiveName = archiveName; this.previousFailure = previousFailure; this.prepareFailure = prepareFailure;
        SavedBox.ItemsSource = saved; SavedPanel.Visibility = !editing && saved.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EditorDetails.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        SavePassword.IsEnabled = canSave;
        if (!editing && !compressing && !browsing && previousFailure?.Failures.Count > 0)
        {
            FailurePanel.Visibility = Visibility.Visible;
            FailureSummary.Text = UiText.Format("T_c2e0ceeca350", previousFailure.SuccessfulItems, previousFailure.Failures.Count)
                + (previousFailure.VerifiedEncryptedItems.Count > 0 ? UiText.Get("T_a01cf40b28cc") : "");
        }
        Description.Text = editing ? UiText.Get("T_a199bcc29a45") : archiveName + (failed ? UiText.Get("T_3e40ded6412e") : UiText.Get("T_c950f8f2b8e0"));
        if (editing)
        {
            Title = Heading.Text = existing is null ? UiText.Get("T_36b2403fa140") : UiText.Get("T_b9bd587305d1");
            ContinueButton.Content = UiText.Get("T_2e9f4819e064"); SavePassword.Visibility = Visibility.Collapsed; DetailsPanel.Visibility = Visibility.Visible;
            NameBox.Text = existing?.Name ?? ""; NotesBox.Text = existing?.Notes ?? ""; AutoTryBox.IsChecked = existing?.AutoTry ?? true; SecretBox.Password = existing?.Secret ?? "";
            GroupBox.Text = existing?.Group ?? ""; SourceBox.Text = existing?.Source ?? ""; MatchBox.Text = existing?.MatchText ?? ""; DirectoryMatchBox.Text = existing?.MatchDirectory ?? "";
        }
        else NameBox.Text = archiveName;
        if (compressing)
        {
            if(encryptHeaders is {} headers){CompressionHeadersPanel.Visibility=Visibility.Visible;EncryptHeadersBox.IsChecked=headers;}
            ConfirmationPanel.Visibility=Visibility.Visible;
            Title = Heading.Text = UiText.Get("T_84d584fcfc26"); ContinueButton.Content = UiText.Get("T_8154b012eaf1");
            Description.Text = UiText.Get("T_fe6aa19502f5");
            SavePassword.Content = UiText.Get("T_5d89c8f8c810");
        }
        if (browsing)
        {
            Title = Heading.Text = UiText.Get("T_8a03bbcdc75e"); ContinueButton.Content = UiText.Get("T_393dd8a7f29e");
            SavePassword.IsEnabled = false; SavePassword.Visibility = Visibility.Collapsed;
            Description.Text = archiveName + (failed ? UiText.Get("T_dcfba7612da5") : UiText.Get("T_7701884823bd"))
                + UiText.Get("T_58c5744db746");
        }
        if(testing)
        {
            Title=Heading.Text=UiText.Get("T_df622593db69");ContinueButton.Content=UiText.Get("T_eb1ee5f7f8d7");
            SavePassword.IsEnabled=false;SavePassword.Visibility=Visibility.Collapsed;
            Description.Text=archiveName+(failed?UiText.Get("T_dcfba7612da5"):UiText.Get("T_feaebc29ef3e"))+UiText.Get("T_0da2931b10ac");
        }
        if(!editing && !compressing && !browsing && !testing)CancelButton.Content=UiText.Get("Conflict.Cancel");
        if(failed && !editing && !compressing)
        {
            RetryBanner.Visibility=Visibility.Visible;
            RetryMessage.Text=UiText.Get(browsing || testing?"T_dcfba7612da5":"T_3e40ded6412e").Trim();
            Description.Text=archiveName+(browsing?UiText.Get("T_58c5744db746"):testing?UiText.Get("T_0da2931b10ac"):"");
        }
        Loaded += (_, _) => SecretBox.Focus();
        Closed += (_, _) => { SecretBox.Clear(); VisibleSecret.Clear();ConfirmationBox.Clear();VisibleConfirmation.Clear();SavedBox.ItemsSource = null; };
    }
    private void OnShowFailures(object sender, RoutedEventArgs e)
    {
        if (previousFailure is null) return;
        var dialog = new ExtractionFailureDialog(archiveName, previousFailure,testing) { Owner = this };
        prepareFailure?.Invoke(dialog);
        dialog.ShowDialog();
    }
    private void VisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (SecretBox is null || VisibleSecret is null) return;
        if (ShowPassword.IsChecked == true) { VisibleSecret.Text = SecretBox.Password; SecretBox.Visibility = Visibility.Collapsed; VisibleSecret.Visibility = Visibility.Visible; VisibleSecret.Focus(); }
        else { SecretBox.Password = VisibleSecret.Text; VisibleSecret.Clear(); VisibleSecret.Visibility = Visibility.Collapsed; SecretBox.Visibility = Visibility.Visible; SecretBox.Focus(); }
        if(ConfirmationBox is null)return;
        if(ShowPassword.IsChecked==true){VisibleConfirmation.Text=ConfirmationBox.Password;ConfirmationBox.Visibility=Visibility.Collapsed;VisibleConfirmation.Visibility=Visibility.Visible;}
        else{ConfirmationBox.Password=VisibleConfirmation.Text;VisibleConfirmation.Clear();VisibleConfirmation.Visibility=Visibility.Collapsed;ConfirmationBox.Visibility=Visibility.Visible;}
    }
    private void SecretChanged(object sender, RoutedEventArgs e)
    {
        if(ErrorLabel is null)return;
        ErrorLabel.Text="";
        foreach(var field in new System.Windows.Controls.Control[]{SecretBox,VisibleSecret,ConfirmationBox,VisibleConfirmation})if(field is not null)FieldState.SetHasError(field,false);
    }
    private void SaveChanged(object sender, RoutedEventArgs e) { if (!editing && DetailsPanel is not null) DetailsPanel.Visibility = SavePassword.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; }
    private void SavedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedBox.SelectedItem is SavedPassword value) { SecretBox.Password = value.Secret;ConfirmationBox.Password=value.Secret;if (ShowPassword.IsChecked == true) {VisibleSecret.Text = value.Secret;VisibleConfirmation.Text=value.Secret;} SavePassword.IsChecked = false; }
    }
    private void OnContinue(object sender, RoutedEventArgs e)
    {
        var secret = ShowPassword.IsChecked == true ? VisibleSecret.Text : SecretBox.Password;
        if (string.IsNullOrEmpty(secret))
        {
            ErrorLabel.Text = UiText.Get("T_c682f190cbd4");
            FieldState.SetHasError(ShowPassword.IsChecked==true?VisibleSecret:SecretBox,true);
            if (ShowPassword.IsChecked == true) VisibleSecret.Focus(); else SecretBox.Focus();
            return;
        }
        var selected = SavedBox.SelectedItem as SavedPassword;
        if(compressing && secret!=(ShowPassword.IsChecked==true?VisibleConfirmation.Text:ConfirmationBox.Password))
        {ErrorLabel.Text=UiText.Get("Password.ConfirmationMismatch");if(ShowPassword.IsChecked==true){FieldState.SetHasError(VisibleConfirmation,true);VisibleConfirmation.Focus();}else{FieldState.SetHasError(ConfirmationBox,true);ConfirmationBox.Focus();}return;}
        Response = new PasswordPromptResponse(secret, SavePassword.IsChecked == true && SavePassword.IsEnabled, NameBox.Text, selected?.Secret == secret ? selected.Id : null);
        if (editing && !PasswordOrganization.ValidDirectory(DirectoryMatchBox.Text.Trim()))
        { ErrorLabel.Text = UiText.Get("Password.DirectoryInvalid"); DirectoryMatchBox.Focus(); return; }
        if (editing) Edited = new SavedPassword(original?.Id ?? Guid.NewGuid(), string.IsNullOrWhiteSpace(NameBox.Text) ? UiText.Get("T_218e758854ee") : NameBox.Text.Trim(), secret, NotesBox.Text.Trim(), AutoTryBox.IsChecked == true, original?.CreatedAt ?? DateTimeOffset.UtcNow, original?.LastSuccessAt,
            GroupBox.Text.Trim(), SourceBox.Text.Trim(), MatchBox.Text.Trim(), DirectoryMatchBox.Text.Trim());
        DialogResult = true;
    }
}
