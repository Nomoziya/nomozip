using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nomozip.DesignPreview;

internal static class PagesCapture
{
    internal static async Task<bool> RunAsync(MainWindow window, string outputDirectory)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Isolated CI required.");
        var output = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(output); var checks = new Dictionary<string, bool>(); var pictures = new List<object>(); string? error = null;
        void Check(string name, bool passed) => checks.Add(name, passed);
        async Task Layout() { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(100); }
        void Shot(string file, FrameworkElement visual, string scene, string state, bool english)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Round(visual.ActualWidth), (int)Math.Round(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(output, file))) encoder.Save(stream);
            pictures.Add(new { file, scene, state, language = english ? "en" : "zh-CN", theme = ((App)Application.Current).Dark ? "dark" : "light", clientDip = new { width = visual.ActualWidth, height = visual.ActualHeight }, offscreenPixelScale = 1, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, file)))).ToLowerInvariant() });
        }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Scene(string page, string file, string state = "typical", bool english = false)
        {
            window.ShowWorkspace(page, english, state); await Layout(); var view = window.WorkspaceView;
            Check(file + ": page title and fixed status visible", view.Controls["page-title"].IsVisible && view.Controls["page-status"].IsVisible && Math.Abs(view.ActualHeight - (window.Client.ActualHeight - 37)) < 2);
            Shot(file + ".png", window.Client, page, state, english);
        }
        try
        {
            window.IsHitTestVisible = false; window.MinWidth = 1152; window.Width = 1152; window.Height = 768; await Layout();
            await Scene("home", "01-files-empty"); await Scene("create", "02-create");
            var view = window.WorkspaceView;
            Check("create uses real source count and removes only the selection", ((ListBox)view.Controls["sources"]).Items.Count == 4);
            ((TextBox)view.Controls["create-name"]).Text = ""; Click((Button)view.Controls["create-submit"]); Check("missing archive name has inline error", ((TextBlock)view.Controls["page-status"]).Text == "请输入文件名。");
            ((TextBox)view.Controls["create-name"]).Text = "很长的项目资料备份名称_原始照片与长期归档_2026年十月"; await Layout(); Shot("03-create-long-name.png", window.Client, "create", "long name", false);
            await Scene("create", "04-create-advanced", "advanced"); await Scene("create", "05-create-empty", "empty"); Check("empty sources cannot create an archive", !((Button)view.Controls["create-submit"]).IsEnabled);
            await Scene("vault", "06-vault"); var secret = (PasswordBox)view.Controls["vault-secret"];
            ((TextBox)view.Controls["vault-name"]).Text = ""; Click((Button)view.Controls["vault-save"]); Check("vault form rejects incomplete entry", ((TextBlock)view.Controls["page-status"]).Text == "请填写名称和密码。");
            await Scene("vault", "07-vault-locked", "locked"); Check("leaving editable vault clears previous secret control", secret.Password.Length == 0 && !view.Controls.ContainsKey("vault-secret"));
            await Scene("vault", "08-vault-empty", "empty"); await Scene("vault", "09-vault-search"); ((TextBox)view.Controls["vault-search"]).Text = "摄影"; await Layout(); Check("vault search changes visible sample records", ((ListBox)view.Controls["vault-records"]).Items.Cast<ListBoxItem>().Count(row => row.Visibility == Visibility.Visible) == 1);
            Shot("09-vault-search.png", window.Client, "vault", "filtered", false);
            await Scene("tools", "10-checksum-ready");
            Click((Button)view.Controls["hash-start"]); Check("checksum without expectation never claims match", ((TextBlock)view.Controls["hash-result"]).Text == "已计算 · 未提供期望值");
            var bytes = window.Model.Entries.First(row => row.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).Bytes; Check("checksum computed from actual ZIP payload", ((TextBox)view.Controls["hash-computed"]).Text == Convert.ToHexString(SHA256.HashData(bytes)));
            ((TextBox)view.Controls["hash-expected"]).Text = "00"; Click((Button)view.Controls["hash-start"]); Check("mismatch is displayed as mismatch", ((TextBlock)view.Controls["hash-result"]).Text == "不一致"); await Layout(); Shot("11-checksum-mismatch.png", window.Client, "tools", "mismatch", false);
            ((TextBox)view.Controls["hash-expected"]).Text = ((TextBox)view.Controls["hash-computed"]).Text; Click((Button)view.Controls["hash-start"]); Check("matching expectation agrees with computed hash", ((TextBlock)view.Controls["hash-result"]).Text == "一致"); await Layout(); Shot("12-checksum-match.png", window.Client, "tools", "match", false);
            await Scene("tools", "13-checksum-empty", "empty"); Check("empty checksum list cannot calculate", !((Button)view.Controls["hash-start"]).IsEnabled);
            await Scene("settings", "14-settings"); await Scene("settings", "15-settings-file-operations", "filesettings"); await Scene("settings", "16-settings-security", "security"); await Scene("updates", "17-updates"); Check("updates start unchecked without fabricated remote status", ((TextBlock)view.Controls["update-status"]).Text == "尚未检查");
            ((App)Application.Current).ApplyTheme(true); await Scene("vault", "18-vault-dark"); await Scene("updates", "19-updates-dark"); ((App)Application.Current).ApplyTheme(false);
            window.MinWidth = 800; window.Width = 800; window.Height = 600; await Layout(); await Scene("create", "20-create-narrow-english", english: true); await Scene("vault", "21-vault-narrow-english", english: true); await Scene("settings", "22-settings-narrow-english", english: true); await Scene("tools", "23-checksum-narrow-english", english: true);
            window.MinWidth = 1152; window.Width = 1152; window.Height = 768; await Layout();
            foreach (var scene in new[] { "extract", "batch", "password", "master", "backup", "conflict", "error", "edit", "unlock" })
            {
                var dialog = window.ShowDesignDialog(scene); dialog.IsHitTestVisible = false; dialog.MinHeight = dialog.Height; dialog.Show(); await Layout(); dialog.UpdateLayout();
                var bounds = dialog.Submit.TransformToAncestor(dialog.Client).TransformBounds(new Rect(dialog.Submit.RenderSize)); Check(scene + ": submit is fully visible inside fixed footer", bounds.Bottom <= dialog.Client.ActualHeight && bounds.Right <= dialog.Client.ActualWidth && dialog.Submit.IsVisible);
                if (scene == "password") { Click(dialog.Submit); Check("blank password rejected inline", dialog.Feedback.Text == "请输入密码。"); ((PasswordBox)dialog.Controls["password"]).Password = "sample-1"; ((PasswordBox)dialog.Controls["confirm"]).Password = "sample-2"; Click(dialog.Submit); Check("password mismatch rejected inline", dialog.Feedback.Text == "两次输入的密码不一致。"); }
                if (scene == "conflict") { ((TextBox)dialog.Controls["rename"]).Text = "invalid/name"; Click(dialog.Submit); Check("rename rejects invalid file name", dialog.Feedback.Text == "请输入有效文件名。"); }
                Shot("24-dialog-" + scene + ".png", dialog.Client, scene, scene is "password" or "conflict" ? "validation error" : "typical", false);
                var oldSecret = dialog.Controls.GetValueOrDefault("password") as PasswordBox; dialog.Close(); if (oldSecret is not null) Check(scene + ": close clears secret input", oldSecret.Password.Length == 0);
            }
            var englishDialog = window.ShowDesignDialog("batch", true); englishDialog.MinHeight = englishDialog.Height; englishDialog.Show(); await Layout(); Shot("25-dialog-batch-english.png", englishDialog.Client, "batch", "typical", true); englishDialog.Close();
            ((App)Application.Current).ApplyTheme(false, true); await Scene("vault", "26-vault-system-colour-resources"); Check("high contrast uses paired selection colours", ((SolidColorBrush)Application.Current.Resources["Selected"]).Color == SystemColors.HighlightColor && ((SolidColorBrush)Application.Current.Resources["SelectedText"]).Color == SystemColors.HighlightTextColor); ((App)Application.Current).ApplyTheme(false);
            window.ShowWorkspace("settings"); await Layout(); window.IsHitTestVisible = true; IsolatedInput.Activate(window); var choice = (ComboBox)view.Controls["theme"]; choice.Focus(); IsolatedInput.Tab(); await Layout(); Check("keyboard tab advances to the next settings field", Keyboard.FocusedElement == view.Controls["language"]); Shot("27-settings-keyboard-focus.png", window.Client, "settings", "keyboard focus", false); window.IsHitTestVisible = false;
        }
        catch (Exception exception) { error = exception.ToString(); }
        var passed = error is null && checks.Values.All(value => value);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { passed, checks, error, sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"), pictures, acceptedCore = "967175a4274835fbfefeb75a6b30718051dfa41c", scope = "R2 standalone WPF design surfaces; in-memory examples, no production services or disk extraction", physicalDpiAccepted = false }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }
}
