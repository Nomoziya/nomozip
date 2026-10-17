using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace Nomozip.DesignPreview;

public sealed class DialogPreview : Window
{
    public Border Client { get; } = new();
    public StackPanel Form { get; } = new();
    public TextBlock Feedback { get; } = new();
    public Button Submit { get; } = new();
    public Dictionary<string, FrameworkElement> Controls { get; } = new();
    public string Scene { get; }
    public bool English { get; }
    private string T(string zh, string en) => English ? en : zh;
    private TextBlock Text(string text, bool secondary = false) { var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None }; if (secondary) block.SetResourceReference(StyleProperty, "Caption"); return block; }
    private TextBox Input(string key, string text, bool multiline = false)
    {
        var input = new TextBox { Text = text, Height = multiline ? 84 : 36, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, ToolTip = text }; Controls[key] = input; return input;
    }
    private PasswordBox Secret(string key) { var box = new PasswordBox { Style = (Style)FindResource("SecretField") }; Controls[key] = box; return box; }
    private void Field(string label, FrameworkElement input) { var text = Text(label); text.Margin = new Thickness(0, 0, 0, 8); Form.Children.Add(text); input.Margin = new Thickness(0, 0, 0, 16); Form.Children.Add(input); AutomationProperties.SetName(input, label); }
    private CheckBox Check(string key, string text, bool enabled = true) { var value = new CheckBox { Style = (Style)FindResource("WorkspaceCheck"), Content = text, IsChecked = enabled, Margin = new Thickness(0, 0, 0, 14) }; Controls[key] = value; return value; }
    public DialogPreview(Window owner, string scene, bool english = false)
    {
        Scene = scene; English = english; Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 560; Height = scene == "batch" ? 760 : scene is "extract" ? 720 : scene is "edit" or "error" ? 560 : 520; MinWidth = 480; MinHeight = 400;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; UseLayoutRounding = true; SnapsToDevicePixels = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display); FontFamily = (FontFamily)FindResource("UiFont"); FontSize = 15; SetResourceReference(ForegroundProperty, "Text"); SetResourceReference(BackgroundProperty, "Surface");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        Title = scene switch { "extract" => T("解压所选项目", "Extract selected items"), "batch" => T("批量解压", "Batch extraction"), "password" => T("设置压缩密码", "Set archive password"), "master" => T("设置主密码", "Set master password"), "backup" => T("加密备份", "Encrypted backup"), "conflict" => T("目标位置存在同名文件", "A file already exists"), "error" => T("部分文件未能解压", "Some files could not be extracted"), "edit" => T("确认写回压缩包", "Confirm archive write-back"), _ => T("输入密码", "Enter password") };
        Client.SetResourceReference(Border.BackgroundProperty, "Surface"); Client.SetResourceReference(Border.BorderBrushProperty, "ControlLine"); Client.BorderThickness = new Thickness(1); Client.CornerRadius = new CornerRadius(7); Content = Client;
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) }); Client.Child = layout;
        var header = new Grid { Margin = new Thickness(24, 20, 18, 12) }; var heading = Text(Title); heading.FontSize = 22; heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(42, 4, 34, 0); header.Children.Add(heading); var icon = new Symbol { Kind = scene is "extract" or "batch" ? "DocumentArrowDown" : scene is "password" or "master" or "backup" or "unlock" ? "Key" : "Document", Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) }; icon.SetResourceReference(Control.ForegroundProperty, "Accent"); header.Children.Add(icon);
        var close = new Button { Content = new Symbol { Kind = "Dismiss", Width = 18, Height = 18 }, Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)FindResource("IconButton") }; AutomationProperties.SetName(close, T("关闭", "Close")); WindowChrome.SetIsHitTestVisibleInChrome(close, true); close.Click += (_, _) => Close(); header.Children.Add(close); layout.Children.Add(header);
        var scroll = new ScrollViewer { Content = Form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(24, 0, 24, 0) }; Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var footer = new Border { Padding = new Thickness(24, 14, 24, 14), BorderThickness = new Thickness(0, 1, 0, 0) }; footer.SetResourceReference(Border.BorderBrushProperty, "Line"); var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Submit.Content = T("确认", "Confirm"); Submit.Style = (Style)FindResource("Primary"); Submit.ContentTemplate = (DataTemplate)FindResource("WorkspaceButtonLabel"); Submit.MinWidth = 148; Submit.Height = 44; Submit.Margin = new Thickness(0, 0, 10, 0); Submit.IsDefault = true; buttons.Children.Add(Submit); var cancel = new Button { Content = T("取消", "Cancel"), ContentTemplate = (DataTemplate)FindResource("WorkspaceButtonLabel"), MinWidth = 110, Height = 44, IsCancel = true }; cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel); footer.Child = buttons; Grid.SetRow(footer, 2); layout.Children.Add(footer);
        switch (scene) { case "extract": case "batch": Extraction(); break; case "conflict": Conflict(); break; case "error": Failure(); break; case "edit": Edit(); break; default: Password(); break; }
        Feedback.TextWrapping = TextWrapping.Wrap; Feedback.TextTrimming = TextTrimming.None; Feedback.Margin = new Thickness(0, 16, 0, 12); Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); AutomationProperties.SetLiveSetting(Feedback, AutomationLiveSetting.Polite); Form.Children.Add(Feedback);
        Closed += (_, _) => { foreach (var box in Controls.Values.OfType<PasswordBox>()) box.Clear(); };
    }
    private void Extraction()
    {
        var batch = Scene == "batch"; Form.Children.Add(Text(batch ? T("3 个压缩包", "3 archives") : T("2 个文件 · 图片备份.zip", "2 files · Photos.zip"), true));
        if (batch)
        {
            var entries = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
            foreach (var name in new[] { "图片备份.zip", "资料合集.7z", "设计素材.rar" }) { var row = new DockPanel { Height = 36, Margin = new Thickness(10, 0, 8, 0) }; var remove = new Button { Content = new Symbol { Kind = "Dismiss", Width = 16, Height = 16 }, Style = (Style)FindResource("IconButton"), Width = 28, Height = 28 }; AutomationProperties.SetName(remove, T("移除 ", "Remove ") + name); remove.Click += (_, _) => { entries.Children.Remove(row); Submit.Content = T($"开始演示（{entries.Children.Count}）", $"Start demo ({entries.Children.Count})"); Submit.IsEnabled = entries.Children.Count > 0; }; DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove); row.Children.Add(Text(name)); entries.Children.Add(row); }
            var border = new Border { Child = entries, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) }; border.SetResourceReference(Border.BorderBrushProperty, "Line"); Form.Children.Add(border);
        }
        var output = Input("output", @"D:\Extracted"); Field(T("输出位置", "Output location"), output); var folder = Check("folder", T("建立同名文件夹", "Create a folder for each archive")); Form.Children.Add(folder);
        var preview = Text(@"D:\Extracted\图片备份", true); preview.Margin = new Thickness(0, 0, 0, 18); preview.TextWrapping = TextWrapping.NoWrap; preview.TextTrimming = TextTrimming.CharacterEllipsis; preview.ToolTip = preview.Text; Form.Children.Add(preview);
        void UpdatePreview() { preview.Text = output.Text + (folder.IsChecked == true ? "\\图片备份" : ""); preview.ToolTip = preview.Text; } output.TextChanged += (_, _) => UpdatePreview(); folder.Checked += (_, _) => UpdatePreview(); folder.Unchecked += (_, _) => UpdatePreview();
        var conflict = new ComboBox { ItemsSource = new[] { T("每次询问", "Ask every time"), T("跳过", "Skip"), T("覆盖", "Replace") }, SelectedIndex = 0, Style = (Style)FindResource("WorkspaceChoice") }; Field(T("同名文件", "Existing files"), conflict); Form.Children.Add(Check("try-passwords", T("尝试密码库中的密码", "Try passwords from the vault")));
        var resume = new StackPanel(); resume.Children.Add(Check("resume", T("保留解压进度", "Keep extraction progress"))); resume.Children.Add(Text(T("以完整文件为检查点；不保存密码。不支持文件内字节续传。", "Checkpoints retain complete files, never passwords. No resume within a file."), true)); Form.Children.Add(new Expander { Header = T("解压规则与续作", "Extraction rules and resume"), IsExpanded = true, Content = resume });
        Submit.Content = batch ? T("开始演示（3）", "Start demo (3)") : T("开始演示", "Start demo"); Submit.Click += (_, _) => { if (string.IsNullOrWhiteSpace(output.Text)) Feedback.Text = T("请选择输出位置。", "Choose an output location."); else { Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Text"); Feedback.Text = T("参数已确认。此设计预览只演示内存样本，不写入所填位置。", "Options confirmed. This preview uses memory samples and does not write to the entered path."); } };
    }
    private void Password()
    {
        Form.Children.Add(Text(Scene == "backup" ? T("备份使用独立密码，不包含主密码。", "The backup uses a separate password and excludes the master password.") : Scene == "master" ? T("请牢记主密码，丢失后无法恢复。", "Keep your master password safe; it cannot be recovered.") : T("仅用于本次设计预览，关闭时清除输入。", "Used only in this design preview; cleared when closed."), true));
        var secret = Secret("password"); Field(T("密码", "Password"), secret); var confirm = Secret("confirm"); if (Scene != "unlock") Field(T("确认密码", "Confirm password"), confirm);
        Submit.Click += (_, _) => { Feedback.Text = secret.Password.Length == 0 ? T("请输入密码。", "Enter a password.") : Scene != "unlock" && secret.Password != confirm.Password ? T("两次输入的密码不一致。", "The passwords do not match.") : T("输入已验证；未保存或更改实际密码库。", "Input validated; the actual password vault is unchanged."); };
    }
    private void Conflict()
    {
        Form.Children.Add(Text(T("封面.jpg 已存在。请选择处理方式。", "Cover.jpg already exists. Choose an action."))); Form.Children.Add(Text(@"D:\Pictures\摄影项目\2026\十月\封面.jpg", true)); Field(T("重命名为", "Rename to"), Input("rename", "封面 (2).jpg"));
        var options = new WrapPanel(); foreach (var (zh, en) in new[] { ("覆盖", "Replace"), ("跳过", "Skip") }) { var action = new Button { Content = T(zh, en), Margin = new Thickness(0, 0, 8, 0) }; action.Click += (_, _) => Feedback.Text = T("已选择：", "Selected: ") + action.Content; options.Children.Add(action); } Form.Children.Add(options); Form.Children.Add(Check("all-conflicts", T("应用到其余冲突", "Apply to remaining conflicts"), false)); Submit.Content = T("重命名", "Rename"); Submit.Click += (_, _) => { var name = ((TextBox)Controls["rename"]).Text; Feedback.Text = string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ? T("请输入有效文件名。", "Enter a valid file name.") : T("重命名选项已确认；预览不会改动文件。", "Rename option confirmed; preview files remain unchanged."); };
    }
    private void Failure()
    {
        Form.Children.Add(Text(T("已完成 10 项，2 项失败。已成功的文件不会被标为失败。", "10 files completed, 2 failed. Completed files keep their success state.")));
        var error = Text(T("无法写入目标位置：访问被拒绝。", "Cannot write to the destination: access denied.")); error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); error.Margin = new Thickness(0, 18, 0, 12); Form.Children.Add(error);
        var details = Input("details", @"D:\Pictures\摄影项目\长期归档\原始照片\需要保留完整名称的摄影作品备份\封面.jpg" + Environment.NewLine + T("请检查写入权限，或选择其他输出文件夹后重试。", "Check write permission, or retry with a different output folder."), true); details.IsReadOnly = true; Field(T("失败详情", "Failure details"), details); Submit.Content = T("关闭", "Close"); Submit.Click += (_, _) => Close();
    }
    private void Edit()
    {
        Form.Children.Add(Text(T("将修改后的“说明.txt”写回“图片备份.zip”。", "Write the edited Readme.txt back to Photos.zip."))); Form.Children.Add(Text(T("保留原始编码并先创建原包备份；本轮仅支持可预览且不超过 1 MiB 的文本。", "Keep the original encoding and back up the archive first. Text must be previewable and at most 1 MiB."), true));
        var content = Input("edited-text", T("图片归档说明\n本次更新：补充拍摄日期与地点。", "Photo archive notes\nUpdate: capture dates and locations."), true); Field(T("修改后的内容", "Edited text"), content); Submit.Content = T("确认写回", "Confirm write-back"); Submit.Click += (_, _) => Feedback.Text = T("已确认设计流程；预览不会修改压缩包。", "Design flow confirmed; the preview does not modify an archive.");
    }
}
