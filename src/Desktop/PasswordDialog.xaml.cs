using System.Windows;
using System.Windows.Controls;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public partial class PasswordDialog : Window
{
    private readonly SavedPassword? original;
    private readonly bool editing;
    private readonly string archiveName;
    private readonly EngineResult? previousFailure;
    private readonly Action<ExtractionFailureDialog>? prepareFailure;
    public PasswordPromptResponse? Response { get; private set; }
    public SavedPassword? Edited { get; private set; }
    public PasswordDialog(string archiveName, bool failed, IReadOnlyList<SavedPassword> saved, bool canSave, SavedPassword? existing = null, bool editing = false, bool compressing = false, bool browsing = false, EngineResult? previousFailure = null, Action<ExtractionFailureDialog>? prepareFailure = null)
    {
        InitializeComponent(); original = existing; this.editing = editing;
        this.archiveName = archiveName; this.previousFailure = previousFailure; this.prepareFailure = prepareFailure;
        WindowPlacement.FitWorkArea(this, SystemParameters.WorkArea);
        SavedBox.ItemsSource = saved; SavedPanel.Visibility = !editing && saved.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EditorDetails.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        SavePassword.IsEnabled = canSave;
        if (!editing && !compressing && !browsing && previousFailure?.Failures.Count > 0)
        {
            FailurePanel.Visibility = Visibility.Visible;
            FailureSummary.Text = $"上次验证：{previousFailure.SuccessfulItems} 项通过，{previousFailure.Failures.Count} 项失败。尚未解压文件。"
                + (previousFailure.VerifiedEncryptedItems.Count > 0 ? "\n已验证的密码会用于对应文件。请为剩余失败条目输入其它密码；也可能是文件损坏。" : "");
        }
        Description.Text = editing ? "密码会由当前 Windows 用户加密保存在本机。" : archiveName + (failed ? "\n上一个密码未能完整验证。密码可能不匹配，或压缩包已损坏。" : "\n请输入密码，或选择一个已保存的密码。");
        if (editing)
        {
            Title = Heading.Text = existing is null ? "添加密码" : "编辑密码";
            ContinueButton.Content = "保存密码"; SavePassword.Visibility = Visibility.Collapsed; DetailsPanel.Visibility = Visibility.Visible;
            NameBox.Text = existing?.Name ?? ""; NotesBox.Text = existing?.Notes ?? ""; AutoTryBox.IsChecked = existing?.AutoTry ?? true; SecretBox.Password = existing?.Secret ?? "";
        }
        else NameBox.Text = archiveName;
        if (compressing)
        {
            Title = Heading.Text = "设置压缩密码"; ContinueButton.Content = "使用此密码";
            Description.Text = "密码用于本次创建压缩包。也可以选择已保存的密码。";
            SavePassword.Content = "压缩成功后保存到密码库";
        }
        if (browsing)
        {
            Title = Heading.Text = "输入查看密码"; ContinueButton.Content = "查看内容";
            SavePassword.IsEnabled = false; SavePassword.Visibility = Visibility.Collapsed;
            Description.Text = archiveName + (failed ? "\n密码可能不匹配，或压缩包已损坏。" : "\n此压缩包的文件名已加密，需要密码才能查看内容。")
                + "\n本次仅查看内容；如需保存新密码，请在解压成功后保存。";
        }
        Loaded += (_, _) => SecretBox.Focus();
        Closed += (_, _) => { SecretBox.Clear(); VisibleSecret.Clear(); SavedBox.ItemsSource = null; };
    }
    private void OnShowFailures(object sender, RoutedEventArgs e)
    {
        if (previousFailure is null) return;
        var dialog = new ExtractionFailureDialog(archiveName, previousFailure) { Owner = this };
        prepareFailure?.Invoke(dialog);
        dialog.ShowDialog();
    }
    private void VisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (SecretBox is null || VisibleSecret is null) return;
        if (ShowPassword.IsChecked == true) { VisibleSecret.Text = SecretBox.Password; SecretBox.Visibility = Visibility.Collapsed; VisibleSecret.Visibility = Visibility.Visible; VisibleSecret.Focus(); }
        else { SecretBox.Password = VisibleSecret.Text; VisibleSecret.Clear(); VisibleSecret.Visibility = Visibility.Collapsed; SecretBox.Visibility = Visibility.Visible; SecretBox.Focus(); }
    }
    private void SecretChanged(object sender, RoutedEventArgs e) { if (ErrorLabel is not null) ErrorLabel.Text = ""; }
    private void SaveChanged(object sender, RoutedEventArgs e) { if (!editing && DetailsPanel is not null) DetailsPanel.Visibility = SavePassword.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; }
    private void SavedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedBox.SelectedItem is SavedPassword value) { SecretBox.Password = value.Secret; if (ShowPassword.IsChecked == true) VisibleSecret.Text = value.Secret; SavePassword.IsChecked = false; }
    }
    private void OnContinue(object sender, RoutedEventArgs e)
    {
        var secret = ShowPassword.IsChecked == true ? VisibleSecret.Text : SecretBox.Password;
        if (string.IsNullOrEmpty(secret))
        {
            ErrorLabel.Text = "请输入密码。";
            if (ShowPassword.IsChecked == true) VisibleSecret.Focus(); else SecretBox.Focus();
            return;
        }
        var selected = SavedBox.SelectedItem as SavedPassword;
        Response = new PasswordPromptResponse(secret, SavePassword.IsChecked == true && SavePassword.IsEnabled, NameBox.Text, selected?.Secret == secret ? selected.Id : null);
        if (editing) Edited = new SavedPassword(original?.Id ?? Guid.NewGuid(), string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名密码" : NameBox.Text.Trim(), secret, NotesBox.Text.Trim(), AutoTryBox.IsChecked == true, original?.CreatedAt ?? DateTimeOffset.UtcNow, original?.LastSuccessAt);
        DialogResult = true;
    }
}
