using System.Windows;
using System.Windows.Controls;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public partial class PasswordDialog : Window
{
    private readonly SavedPassword? original;
    private readonly bool editing;
    public PasswordPromptResponse? Response { get; private set; }
    public SavedPassword? Edited { get; private set; }
    public PasswordDialog(string archiveName, bool failed, IReadOnlyList<SavedPassword> saved, bool canSave, SavedPassword? existing = null, bool editing = false)
    {
        InitializeComponent(); original = existing; this.editing = editing;
        SavedBox.ItemsSource = saved; SavedPanel.Visibility = !editing && saved.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EditorDetails.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        SavePassword.IsEnabled = canSave;
        Description.Text = editing ? "密码会由当前 Windows 用户加密保存在本机。" : archiveName + (failed ? "\n上一个密码未能完整验证。密码可能不匹配，或压缩包已损坏。" : "\n请输入密码，或选择一个已保存的密码。");
        if (editing)
        {
            Title = Heading.Text = existing is null ? "添加密码" : "编辑密码";
            ContinueButton.Content = "保存密码"; SavePassword.Visibility = Visibility.Collapsed; DetailsPanel.Visibility = Visibility.Visible;
            NameBox.Text = existing?.Name ?? ""; NotesBox.Text = existing?.Notes ?? ""; AutoTryBox.IsChecked = existing?.AutoTry ?? true; SecretBox.Password = existing?.Secret ?? "";
        }
        else NameBox.Text = archiveName;
        Loaded += (_, _) => SecretBox.Focus();
        Closed += (_, _) => { SecretBox.Clear(); VisibleSecret.Clear(); SavedBox.ItemsSource = null; };
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
        if (string.IsNullOrEmpty(secret)) { ErrorLabel.Text = "请输入密码。"; SecretBox.Focus(); return; }
        var selected = SavedBox.SelectedItem as SavedPassword;
        Response = new PasswordPromptResponse(secret, SavePassword.IsChecked == true && SavePassword.IsEnabled, NameBox.Text, selected?.Secret == secret ? selected.Id : null);
        if (editing) Edited = new SavedPassword(original?.Id ?? Guid.NewGuid(), string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名密码" : NameBox.Text.Trim(), secret, NotesBox.Text.Trim(), AutoTryBox.IsChecked == true, original?.CreatedAt ?? DateTimeOffset.UtcNow, original?.LastSuccessAt);
        DialogResult = true;
    }
}
