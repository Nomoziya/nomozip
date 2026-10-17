using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nomozip.DesignPreview;

// Design surfaces only. All editable records/settings remain in this instance's memory.
// Production commands are mapped in MASTER before integration; no vault/update service is used here.
public sealed class WorkspacePreview : Grid
{
    public bool English { get; private set; }
    public string Page { get; private set; } = "home";
    public string State { get; private set; } = "typical";
    public Dictionary<string, FrameworkElement> Controls { get; } = new();
    public event Action<string>? Navigate;
    public event Action<string>? OpenDialog;
    public event Action? OpenArchive;
    private readonly ArchiveModel model;
    private Grid body = new();
    private TextBlock status = new();
    private DockPanel footerPanel = new();
    private readonly List<string> sources = ["项目文件", "说明.txt", "封面.jpg", "数据.csv"];
    private readonly List<string> records = ["摄影资料", "项目归档", "家庭备份", "旧资料", "素材下载"];
    private string savedName = "摄影资料";
    private bool vaultLocked;
    private string hash = "";
    public WorkspacePreview(ArchiveModel archive) { model = archive; SetResourceReference(BackgroundProperty, "Surface"); Unloaded += ClearSecrets; }
    private string T(string zh, string en) => English ? en : zh;
    private TElement Keep<TElement>(string key, TElement element) where TElement : FrameworkElement { Controls[key] = element; return element; }
    private TextBlock Text(string value, string? style = null)
    {
        var block = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
        if (style is not null) block.SetResourceReference(StyleProperty, style);
        return block;
    }
    private TextBlock Paragraph(string value, bool caption = false)
    {
        var block = Text(value, caption ? "Caption" : null); block.TextWrapping = TextWrapping.Wrap; block.TextTrimming = TextTrimming.None; return block;
    }
    private Symbol Icon(string kind, double size = 20) => new() { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
    private Button Action(string label, Action click, string? kind = null, string? style = null)
    {
        var button = new Button { Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left }; AutomationProperties.SetName(button, label);
        if (style is not null) button.SetResourceReference(StyleProperty, style);
        if (kind is null) { button.Content = label; button.SetResourceReference(ContentControl.ContentTemplateProperty, "WorkspaceButtonLabel"); }
        else { var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(Icon(kind)); var text = Text(label); text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button }); text.Margin = new Thickness(8, 0, 0, 0); content.Children.Add(text); button.Content = content; }
        button.Click += (_, _) => click(); return button;
    }
    private TextBox Input(string key, string value, bool multiline = false)
    {
        var box = Keep(key, new TextBox { Text = value, Height = multiline ? 76 : 36, Padding = new Thickness(10, 5, 10, 5), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, ToolTip = value });
        AutomationProperties.SetName(box, key); return box;
    }
    private ComboBox Choice(string key, params string[] values)
    {
        var choice = Keep(key, new ComboBox { ItemsSource = values, SelectedIndex = 0 }); choice.SetResourceReference(StyleProperty, "WorkspaceChoice"); AutomationProperties.SetName(choice, key); return choice;
    }
    private StackPanel Field(string label, FrameworkElement input)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(Text(label, "FieldLabel")); panel.Children.Add(input); AutomationProperties.SetName(input, label); return panel;
    }
    private CheckBox Check(string key, string label, bool value = true)
    {
        var check = Keep(key, new CheckBox { Style = (Style)FindResource("WorkspaceCheck"), Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 12) }); AutomationProperties.SetName(check, label); return check;
    }
    private Border Section(FrameworkElement child, Thickness? padding = null)
    {
        var border = new Border { Child = child, Padding = padding ?? new Thickness(24), BorderThickness = new Thickness(0, 0, 0, 1) }; border.SetResourceReference(Border.BorderBrushProperty, "Line"); return border;
    }
    private ScrollViewer Scroll(FrameworkElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0), CanContentScroll = true };
    private void Place(FrameworkElement child, int row, int col = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, col); Children.Add(child); }
    private void Body(FrameworkElement content) => body.Children.Add(content);
    private void Notify(string message, bool error = false) { status.Text = message; status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Success"); }
    private void Go(string page) => Navigate?.Invoke(page);
    private void Dialog(string page) => OpenDialog?.Invoke(page);
    public void ShowPage(string page, bool english = false, string state = "typical")
    {
        ClearSecrets(this, new RoutedEventArgs()); Page = page; English = english; State = state; Controls.Clear(); Children.Clear(); RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(94) }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = new GridLength(49) });
        var (title, kind) = page switch { "create" => (T("新建压缩包", "Create archive"), "Archive"), "vault" => (T("密码库", "Password vault"), "Key"), "tools" => (T("文件校验", "File checksums"), "Wrench"), "settings" or "updates" => (T("设置", "Settings"), "Settings"), _ => (T("文件", "Files"), "Folder") };
        var header = new Grid { Margin = new Thickness(34, 0, 18, 0) }; header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) }); header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; var symbol = Icon(kind, 34); symbol.SetResourceReference(Control.ForegroundProperty, "Accent"); heading.Children.Add(symbol); var name = Keep("page-title", Text(title)); name.FontSize = 26; name.FontWeight = FontWeights.Bold; name.Margin = new Thickness(18, 0, 0, 0); heading.Children.Add(name); header.Children.Add(heading);
        var subtitle = Text(T("文件与归档", "Files and archives"), "Caption"); subtitle.Text = page switch { "vault" => T("示例记录 · 仅保存在本次预览内存中", "Sample records · kept in preview memory only"), "tools" => T("计算实际样本的哈希值，可输入期望值进行比较。", "Hash the real sample payload and compare an expected value."), "create" => T("添加来源，确认格式和输出位置后创建。", "Add sources, then choose format and output location."), "settings" or "updates" => T("常规、文件操作、密码安全与更新", "General, file operations, security and updates"), _ => T("打开压缩包，或从文件和文件夹开始。", "Open an archive, or start with files and folders.") }; Grid.SetRow(subtitle, 1); header.Children.Add(subtitle); Place(Section(header, new Thickness(0)), 0);
        body = Keep("page-body", new Grid()); Place(body, 1); status = Keep("page-status", Paragraph(T("设计预览 · 仅使用内存样本", "Design preview · in-memory samples only"), true)); footerPanel = new DockPanel(); footerPanel.Children.Add(status); Place(Section(footerPanel, new Thickness(20, 0, 20, 0)), 2);
        switch (page) { case "create": Create(); break; case "vault": Vault(); break; case "tools": Tools(); break; case "settings": case "updates": Settings(page == "updates"); break; default: Home(); break; }
    }
    private void Home()
    {
        var center = new StackPanel { MaxWidth = 650, Margin = new Thickness(32), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var image = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/nomozip-256.png")), Width = 72, Height = 72, Margin = new Thickness(0, 0, 0, 20) }; center.Children.Add(image);
        var title = Text(T("轻松整理你的压缩文件", "Your archives, neatly organised")); title.FontSize = 25; title.FontWeight = FontWeights.SemiBold; title.HorizontalAlignment = HorizontalAlignment.Center; center.Children.Add(title);
        var hint = Paragraph(T("打开压缩包以浏览内容，也可以添加文件来压缩或批量解压。", "Browse an archive, create a new one, or prepare a batch extraction."), true); hint.TextAlignment = TextAlignment.Center; hint.Margin = new Thickness(0, 16, 0, 24); center.Children.Add(hint);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center }; actions.Children.Add(Action(T("打开示例压缩包", "Open sample archive"), () => OpenArchive?.Invoke(), "FolderOpen", "Primary")); actions.Children.Add(Action(T("新建压缩包", "Create archive"), () => Go("create"), "Add")); center.Children.Add(actions);
        var secondary = Action(T("批量解压…", "Batch extraction…"), () => Dialog("batch"), "DocumentArrowDown", "Quiet"); secondary.HorizontalAlignment = HorizontalAlignment.Center; secondary.Margin = new Thickness(0, 16, 0, 0); center.Children.Add(secondary); Body(center);
    }
    private Grid Split(FrameworkElement left, FrameworkElement right, double rightWidth, bool stackNarrow)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightWidth) }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right);
        void Resize() { var narrow = stackNarrow && grid.ActualWidth < 850; grid.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : rightWidth); grid.RowDefinitions[0].Height = narrow ? new GridLength(220) : new GridLength(1, GridUnitType.Star); grid.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(0); Grid.SetRow(right, narrow ? 1 : 0); Grid.SetColumn(right, narrow ? 0 : 1); }
        grid.SizeChanged += (_, _) => Resize(); return grid;
    }
    private ListBox Rows(string key, IEnumerable<string> names, Func<string, FrameworkElement> template, int rowHeight = 44)
    {
        var list = Keep(key, new ListBox { BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch }); list.SetResourceReference(Control.BackgroundProperty, "Surface");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        var baseStyle = (Style)FindResource("FileRow"); var rowStyle = new Style(typeof(ListBoxItem), baseStyle); rowStyle.Setters.Add(new Setter(HeightProperty, (double)rowHeight)); list.ItemContainerStyle = rowStyle;
        foreach (var name in names) { var item = new ListBoxItem { Content = template(name), Tag = name, ToolTip = name }; list.Items.Add(item); }
        if (list.Items.Count > 0) list.SelectedIndex = 0; AutomationProperties.SetName(list, key); return list;
    }
    private Grid FileRow(string name, string kind = "Document", string detail = "")
    {
        var row = new Grid { Margin = new Thickness(20, 0, 12, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(detail.Length > 0 ? 85 : 0) }); row.Children.Add(Icon(kind)); var label = Text(name, "RowText"); label.Margin = new Thickness(8, 0, 8, 0); Grid.SetColumn(label, 1); row.Children.Add(label); var metadata = Text(detail, "RowMetadata"); Grid.SetColumn(metadata, 2); row.Children.Add(metadata); return row;
    }
    private DockPanel Table(ListBox list, string name, string detail)
    {
        var table = new DockPanel(); var header = new Grid { Height = 31, Margin = new Thickness(20, 0, 12, 0) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(detail.Contains('/') ? 150 : 85) }); header.Children.Add(Text(name, "Caption")); var secondary = Text(detail, "Caption"); Grid.SetColumn(secondary, 1); header.Children.Add(secondary); var line = Section(header, new Thickness(0)); DockPanel.SetDock(line, Dock.Top); table.Children.Add(line); table.Children.Add(list); return table;
    }
    private Grid VaultRow(string name)
    {
        var row = new Grid { Margin = new Thickness(20, 0, 12, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); row.Children.Add(Text(name, "RowText")); var metadata = Text(T("工作 · 已启用", "Work · enabled"), "RowMetadata"); metadata.FontSize = 13; Grid.SetColumn(metadata, 1); row.Children.Add(metadata); return row;
    }
    private void Create()
    {
        var list = new DockPanel(); var toolbar = new WrapPanel { Margin = new Thickness(16, 10, 8, 10) }; toolbar.Children.Add(Action(T("添加文件", "Add files"), () => { sources.Add("示例-" + (sources.Count + 1) + ".txt"); ShowPage(Page, English); }, "Add", "Primary")); toolbar.Children.Add(Action(T("添加文件夹", "Add folder"), () => { sources.Add("文件夹-" + (sources.Count + 1)); ShowPage(Page, English); }, "Folder")); var remove = Action(T("移除所选", "Remove"), () => { if (Controls["sources"] is ListBox { SelectedItem: ListBoxItem selected }) { sources.Remove((string)selected.Tag); ShowPage(Page, English); } }, "Dismiss"); remove.IsEnabled = sources.Count > 0; toolbar.Children.Add(remove); var toolbarFrame = Section(toolbar, new Thickness(0)); DockPanel.SetDock(toolbarFrame, Dock.Top); list.Children.Add(toolbarFrame);
        var count = Text(T($"{sources.Count} 个来源 · 选择仅用于移除", $"{sources.Count} sources · selection is for removal"), "Caption"); count.Margin = new Thickness(20, 12, 8, 12); DockPanel.SetDock(count, Dock.Bottom); list.Children.Add(count); list.Children.Add(Table(Rows("sources", State == "empty" ? [] : sources, name => FileRow(name, name.Contains('.') ? "Document" : "Folder", model.Entries.FirstOrDefault(entry => entry.Name == name)?.SizeLabel ?? "—"), 36), T("名称", "Name"), T("大小", "Size")));
        var form = new StackPanel { Margin = new Thickness(24, 18, 24, 20) }; form.Children.Add(Text(T("压缩设置", "Compression settings"), "SectionTitle")); var path = Input("create-location", @"D:\Archives"); form.Children.Add(Field(T("保存位置", "Save to"), path)); var nameBox = Input("create-name", T("项目备份", "Project backup")); form.Children.Add(Field(T("文件名", "File name"), nameBox)); form.Children.Add(Field(T("格式", "Format"), Choice("create-format", "7z", "zip"))); form.Children.Add(Field(T("压缩级别", "Compression level"), Choice("create-level", T("标准", "Normal"), T("快速", "Fast"), T("最高", "Ultra"))));
        var encryption = new DockPanel(); var setPassword = Action(T("设置密码", "Set password"), () => Dialog("password"), "Key", "Secondary"); DockPanel.SetDock(setPassword, Dock.Right); encryption.Children.Add(setPassword); encryption.Children.Add(Text(T("加密：未设置", "Encryption: not set"), "Caption")); form.Children.Add(Section(encryption, new Thickness(0, 0, 0, 18)));
        var advanced = new StackPanel(); advanced.Children.Add(Field(T("分卷大小", "Split volumes"), Choice("volume", T("不分卷", "Do not split"), "100 MB", "700 MB"))); advanced.Children.Add(Check("sfx", T("创建自解压格式 (.exe)", "Create self-extracting archive (.exe)"), false)); advanced.Children.Add(Paragraph(T("压缩参数由所选级别和引擎决定。", "Compression parameters follow the selected level and engine."), true)); var expander = new Expander { Header = T("高级选项", "Advanced options"), Content = advanced, Margin = new Thickness(0, 16, 0, 20), IsExpanded = State == "advanced" }; form.Children.Add(expander);
        var destination = Keep("create-destination", Paragraph(@"D:\Archives\" + nameBox.Text + ".7z", true)); form.Children.Add(destination); nameBox.TextChanged += (_, _) => destination.Text = path.Text + "\\" + nameBox.Text + ".7z";
        var submit = Keep("create-submit", Action(T("创建压缩包", "Create archive"), () => { if (string.IsNullOrWhiteSpace(nameBox.Text)) Notify(T("请输入文件名。", "Enter a file name."), true); else Notify(T("参数已确认；设计预览不会写入磁盘。", "Options confirmed; the design preview does not write to disk.")); }, "Archive", "Primary")); submit.IsEnabled = sources.Count > 0 && State != "empty"; submit.Height = 44; submit.Margin = new Thickness(0, 20, 0, 0); DockPanel.SetDock(submit, Dock.Right); submit.Margin = new Thickness(12, 0, 0, 0); footerPanel.Children.Insert(0, submit);
        var formBorder = new Border { Child = form, BorderThickness = new Thickness(1, 0, 0, 0) }; formBorder.SetResourceReference(Border.BorderBrushProperty, "Line"); Body(Scroll(Split(list, formBorder, 440, true)));
    }
    private Grid InlineField(string label, FrameworkElement input)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(English ? 96 : 76) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); var caption = Keep("label:" + label, Paragraph(label)); caption.VerticalAlignment = VerticalAlignment.Top; caption.Margin = new Thickness(0, 6, 10, 0); grid.Children.Add(caption); Grid.SetColumn(input, 1); grid.Children.Add(input); AutomationProperties.SetName(input, label); return grid;
    }
    private void Vault()
    {
        vaultLocked = State == "locked";
        if (vaultLocked) { var locked = new StackPanel { MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; locked.Children.Add(Icon("Key", 48)); var title = Text(T("密码库已锁定", "Vault is locked"), "SectionTitle"); title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(0, 20, 0, 16); locked.Children.Add(title); locked.Children.Add(Paragraph(T("解锁后才能查看和自动尝试密码。", "Unlock to view records and try saved passwords."), true)); locked.Children.Add(Action(T("解锁示例库", "Unlock sample vault"), () => ShowPage("vault", English), "Key", "Primary")); Body(locked); return; }
        var root = new DockPanel(); var toolbar = new Grid { Margin = new Thickness(20, 10, 16, 10) }; toolbar.ColumnDefinitions.Add(new ColumnDefinition()); toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var search = Input("vault-search", ""); AutomationProperties.SetName(search, T("搜索名称或备注", "Search names or notes")); search.ToolTip = T("搜索名称或备注", "Search names or notes"); search.Margin = new Thickness(0, 0, 12, 0); var searchFrame = new Grid(); searchFrame.Children.Add(search); var searchHint = Text(T("搜索名称或备注", "Search names or notes"), "Caption"); searchHint.Margin = new Thickness(12, 0, 24, 0); searchHint.IsHitTestVisible = false; searchFrame.Children.Add(searchHint); search.TextChanged += (_, _) => searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; toolbar.Children.Add(searchFrame); var actions = new WrapPanel(); actions.Children.Add(Action(T("添加密码", "Add password"), () => { savedName = T("新密码", "New password"); records.Add(savedName); ShowPage(Page, English); }, "Add", "Primary")); actions.Children.Add(Action(T("锁定", "Lock"), () => ShowPage("vault", English, "locked"), "Key")); actions.Children.Add(Action(T("备份", "Backup"), () => Dialog("backup"), null)); Grid.SetColumn(actions, 1); toolbar.Children.Add(actions); var toolbarFrame = Section(toolbar, new Thickness(0)); DockPanel.SetDock(toolbarFrame, Dock.Top); root.Children.Add(toolbarFrame);
        var names = State == "empty" ? [] : State == "many" ? Enumerable.Range(1, 500).Select(index => $"示例记录 {index:000} · 项目归档").ToArray() : records.ToArray(); var list = Rows("vault-records", names, name => VaultRow(name));
        search.TextChanged += (_, _) => { foreach (ListBoxItem item in list.Items) item.Visibility = ((string)item.Tag).Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; };
        var editor = new StackPanel { Margin = new Thickness(24, 20, 24, 20) }; var editorTitle = Text(savedName, "SectionTitle"); editor.Children.Add(editorTitle); var nameBox = Input("vault-name", savedName); editor.Children.Add(InlineField(T("名称", "Name"), nameBox)); var secret = Keep("vault-secret", new PasswordBox { Password = "sample-only", Style = (Style)FindResource("SecretField") }); editor.Children.Add(InlineField(T("密码", "Password"), secret)); editor.Children.Add(InlineField(T("分组", "Group"), Choice("vault-group", T("工作", "Work"), T("个人", "Personal"), T("未分组", "Ungrouped")))); editor.Children.Add(InlineField(T("备注", "Notes"), Input("vault-notes", T("用于摄影项目归档", "For photography project archives"), true))); editor.Children.Add(Check("vault-auto", T("自动尝试此密码", "Try this password automatically")));
        var rules = new StackPanel(); rules.Children.Add(Field(T("匹配文件名", "File-name pattern"), Input("vault-pattern", "*.zip"))); rules.Children.Add(Field(T("优先级", "Priority"), Input("vault-priority", "0"))); editor.Children.Add(new Expander { Header = T("匹配与优先规则", "Matching and priority"), Content = rules, Margin = new Thickness(0, 10, 0, 20) });
        var save = Keep("vault-save", Action(T("保存更改", "Save changes"), () => { if (string.IsNullOrWhiteSpace(nameBox.Text) || secret.Password.Length == 0) { Notify(T("请填写名称和密码。", "Enter a name and password."), true); return; } var index = records.IndexOf(savedName); if (index >= 0) records[index] = nameBox.Text; savedName = nameBox.Text; ShowPage(Page, English); Notify(T("已保存到示例内存库。", "Saved to the sample in-memory vault.")); }, null, "Primary")); save.Height = 40; save.HorizontalAlignment = HorizontalAlignment.Stretch;  var delete = Action(T("删除", "Delete"), () => { records.Remove(savedName); savedName = records.FirstOrDefault() ?? T("新密码", "New password"); ShowPage(Page, English, records.Count == 0 ? "empty" : "typical"); }, null, "Quiet"); delete.SetResourceReference(Control.ForegroundProperty, "Danger"); delete.Margin = new Thickness(0, 20, 0, 0); delete.HorizontalAlignment = HorizontalAlignment.Left;
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem item) { savedName = (string)item.Tag; nameBox.Text = savedName; editorTitle.Text = savedName; secret.Clear(); } };
        if (State == "empty") { editor.Visibility = Visibility.Collapsed; list.Items.Add(new ListBoxItem { Content = Text(T("暂无密码，请添加记录", "No saved passwords"), "Caption"), IsEnabled = false }); }
        var detailLayout = new DockPanel(); var fixedActions = new StackPanel { Margin = new Thickness(24, 10, 24, 10) }; var actionGrid = new Grid(); actionGrid.ColumnDefinitions.Add(new ColumnDefinition()); actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) }); save.Margin = new Thickness(0, 0, 8, 0); actionGrid.Children.Add(save); var cancelEdit = Action(T("取消", "Cancel"), () => ShowPage("vault", English)); cancelEdit.HorizontalAlignment = HorizontalAlignment.Stretch; cancelEdit.Margin = new Thickness(0); cancelEdit.Height = 40; Grid.SetColumn(cancelEdit, 1); actionGrid.Children.Add(cancelEdit); fixedActions.Visibility = State == "empty" ? Visibility.Collapsed : Visibility.Visible; fixedActions.Children.Add(actionGrid); delete.Margin = new Thickness(0, 8, 0, 0); delete.Height = 28; fixedActions.Children.Add(delete); DockPanel.SetDock(fixedActions, Dock.Bottom); detailLayout.Children.Add(fixedActions); detailLayout.Children.Add(Scroll(editor));
        var editBorder = new Border { Child = detailLayout, BorderThickness = new Thickness(1, 0, 0, 0) }; editBorder.SetResourceReference(Border.BorderBrushProperty, "Line"); root.Children.Add(Split(Table(list, T("名称", "Name"), T("分组 / 自动尝试", "Group / auto-try")), editBorder, 360, false)); Body(root);

    }
    public void ClearSensitiveInputs() => ClearSecrets(this, new RoutedEventArgs());
    private void ClearSecrets(object sender, RoutedEventArgs e) { if (Controls.TryGetValue("vault-secret", out var element) && element is PasswordBox secret) secret.Clear(); }
    private void Tools()
    {
        var layout = new DockPanel(); var toolbar = new WrapPanel { Margin = new Thickness(20, 10, 12, 10) }; toolbar.Children.Add(Action(T("添加示例文件", "Add sample file"), () => ShowPage("tools", English), "Add", "Primary")); toolbar.Children.Add(Action(T("清空", "Clear"), () => ShowPage("tools", English, "empty"), "Dismiss")); var algorithm = Choice("hash-algorithm", "SHA-256", "SHA-512", "SHA-1", "MD5"); algorithm.Width = 150; toolbar.Children.Add(algorithm); var toolbarFrame = Section(toolbar, new Thickness(0)); DockPanel.SetDock(toolbarFrame, Dock.Top); layout.Children.Add(toolbarFrame);
        var selected = model.Entries.First(row => row.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)); var list = Rows("hash-files", State == "empty" ? [] : new[] { selected.Name }, name => FileRow(name, "Document", selected.SizeLabel));
        var form = new StackPanel { Margin = new Thickness(24, 20, 24, 20) }; form.Children.Add(Text(selected.Name, "SectionTitle")); var result = Keep("hash-result", Paragraph(T("尚未计算", "Not calculated"))); form.Children.Add(result); var computed = Input("hash-computed", "", true); computed.IsReadOnly = true; computed.FontFamily = (FontFamily)FindResource("PreviewFont"); form.Children.Add(Field(T("计算值", "Computed value"), computed)); var expected = Input("hash-expected", "", true); expected.FontFamily = computed.FontFamily; form.Children.Add(Field(T("期望值（可选）", "Expected value (optional)"), expected));
        var start = Keep("hash-start", Action(T("开始校验", "Calculate checksum"), () => { hash = algorithm.SelectedIndex switch { 1 => Convert.ToHexString(SHA512.HashData(selected.Bytes)), 2 => Convert.ToHexString(SHA1.HashData(selected.Bytes)), 3 => Convert.ToHexString(MD5.HashData(selected.Bytes)), _ => Convert.ToHexString(SHA256.HashData(selected.Bytes)) }; computed.Text = hash; var value = expected.Text.Trim().Replace(" ", ""); result.Text = value.Length == 0 ? T("已计算 · 未提供期望值", "Calculated · no expected value supplied") : value.Equals(hash, StringComparison.OrdinalIgnoreCase) ? T("一致", "Match") : T("不一致", "Mismatch"); result.SetResourceReference(TextBlock.ForegroundProperty, value.Length > 0 && !value.Equals(hash, StringComparison.OrdinalIgnoreCase) ? "Danger" : "Text"); }, "CheckmarkCircle", "Primary")); start.IsEnabled = State != "empty"; form.Children.Add(start); form.Children.Add(Paragraph(T("空的期望值不会显示为“一致”。示例内容来自内存 ZIP。", "An empty expected value is not a match. Sample bytes come from the in-memory ZIP."), true));
        var detail = new Border { Child = Scroll(form), BorderThickness = new Thickness(1, 0, 0, 0) }; detail.SetResourceReference(Border.BorderBrushProperty, "Line"); layout.Children.Add(Split(Table(list, T("名称", "Name"), T("大小", "Size")), detail, 390, false)); Body(layout);
    }
    private void Settings(bool updates)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); var menu = new StackPanel { Margin = new Thickness(10, 14, 10, 0) };
        foreach (var (zh, en, target) in new[] { ("常规", "General", "settings"), ("文件操作", "File operations", "filesettings"), ("密码与安全", "Security", "security"), ("更新", "Updates", "updates") }) { var button = Action(T(zh, en), () => { ShowPage(target == "updates" ? "updates" : "settings", English, target); }, null, "Nav"); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 0, 0, 8); button.Height = 40; if ((updates && target == "updates") || (!updates && (State == target || State == "typical" && target == "settings"))) { button.SetResourceReference(Control.BackgroundProperty, "Selected"); button.SetResourceReference(Control.ForegroundProperty, "NavText"); } menu.Children.Add(button); } grid.Children.Add(Section(menu, new Thickness(0)));
        var form = new StackPanel { Margin = new Thickness(28, 24, 28, 24) }; var content = Scroll(form); Grid.SetColumn(content, 1); grid.Children.Add(content); Body(grid);
        if (updates)
        {
            form.Children.Add(Text(T("软件更新", "Application updates"), "SectionTitle")); form.Children.Add(Paragraph("nomozip", false)); form.Children.Add(Paragraph(T("当前版本在产品接入后读取实际安装信息。", "The product will read the actual installed version."), true)); form.Children.Add(Keep("update-status", Paragraph(T("尚未检查", "Not checked")))); form.Children.Add(Action(T("检查更新", "Check for updates"), () => { ((TextBlock)Controls["update-status"]).Text = T("设计预览不联网，更新检查待产品接入。", "The design preview stays offline; update checks require product integration."); }, null, "Primary"));
            form.Children.Add(Section(Paragraph(T("更新检查失败时仍可继续本地操作。", "Local operations remain available when an update check fails."), true), new Thickness(0, 18, 0, 22))); var title = Text(T("7-Zip 引擎", "7-Zip engine"), "SectionTitle"); title.Margin = new Thickness(0, 24, 0, 18); form.Children.Add(title); form.Children.Add(Paragraph(T("软件更新与引擎更新独立管理。", "Application and engine updates are managed separately."), true)); var actions = new WrapPanel(); actions.Children.Add(Action(T("检查引擎更新", "Check engine updates"), () => Notify(T("预览不下载或切换引擎。", "The preview does not download or switch engines.")), null, "Primary")); actions.Children.Add(Action(T("导入本地引擎", "Import local engine"), () => Notify(T("本地引擎导入将在实际产品中提供。", "Local engine import will use the product's existing command.")))); form.Children.Add(actions); form.Children.Add(Paragraph(T("运行任务时不能切换引擎。", "Engine switching is unavailable while tasks are running."), true)); return;
        }
        if (State == "security") { form.Children.Add(Text(T("密码与安全", "Passwords and security"), "SectionTitle")); form.Children.Add(Paragraph(T("未设置主密码 · 本机保护", "No master password · protected on this device"))); form.Children.Add(Action(T("设置主密码", "Set master password"), () => Dialog("master"), "Key", "Primary")); form.Children.Add(Field(T("空闲后自动锁定", "Lock after inactivity"), Choice("idle-lock", T("5 分钟", "5 minutes"), T("15 分钟", "15 minutes"), T("不自动锁定", "Never")))); form.Children.Add(Action(T("加密备份", "Encrypted backup"), () => Dialog("backup"))); return; }
        if (State != "filesettings") { form.Children.Add(Text(T("外观", "Appearance"), "SectionTitle")); var theme = Choice("theme", T("浅色", "Light"), T("深色", "Dark")); theme.SelectedIndex = ((App)Application.Current).Dark ? 1 : 0; theme.SelectionChanged += (_, _) => ((App)Application.Current).ApplyTheme(theme.SelectedIndex == 1); form.Children.Add(Field(T("主题", "Theme"), theme)); var language = Choice("language", "简体中文", "English"); language.SelectedIndex = English ? 1 : 0; language.SelectionChanged += (_, _) => ShowPage(Page, language.SelectedIndex == 1, State); form.Children.Add(Field(T("界面语言", "Display language"), language)); form.Children.Add(Section(Paragraph(T("高对比度遵循 Windows 系统配色。", "High contrast follows Windows system colours."), true), new Thickness(0, 0, 0, 24))); }
        var fileTitle = Text(T("文件操作默认值", "File operation defaults"), "SectionTitle"); fileTitle.Margin = new Thickness(0, 24, 0, 18); form.Children.Add(fileTitle); form.Children.Add(Field(T("默认输出文件夹", "Default output folder"), Input("default-output", @"D:\Extracted"))); form.Children.Add(Field(T("同名文件", "Existing files"), Choice("default-conflict", T("每次询问", "Ask every time"), T("跳过", "Skip"), T("覆盖", "Replace")))); form.Children.Add(Check("notifications", T("解压成功后通知", "Notify when extraction succeeds"))); form.Children.Add(Action(T("保存设置", "Save settings"), () => Notify(T("设置仅保留在预览中，未修改实际配置。", "Preview settings only; actual configuration is unchanged.")), null, "Primary"));
    }
}
