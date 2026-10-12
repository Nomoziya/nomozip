using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public partial class VaultImportDialog : Window
{
    private readonly IReadOnlyList<SavedPassword> existing, incoming;
    public PasswordImportMode Mode => ModeBox.SelectedIndex == 1 ? PasswordImportMode.KeepBoth : PasswordImportMode.SkipDuplicates;
    public VaultImportDialog(IReadOnlyList<SavedPassword> existing, IReadOnlyList<SavedPassword> incoming)
    {
        this.existing = existing; this.incoming = incoming;
        InitializeComponent(); RefreshPreview();
        WindowPlacement.FitWorkArea(this, SystemParameters.WorkArea);
        Loaded += (_, _) => ModeBox.Focus();
        Closed += (_, _) => PreviewTable.ItemsSource = null;
    }
    private void ModeChanged(object sender, SelectionChangedEventArgs e) { if (SummaryLabel is not null) RefreshPreview(); }
    private void RefreshPreview()
    {
        var plan = PasswordImportPlanner.Build(existing, incoming, Mode);
        PreviewTable.ItemsSource = plan.Rows.Select(row => new { row.Password.Name, row.Password.Notes,
            Action = row.Duplicate ? Mode == PasswordImportMode.SkipDuplicates ? "跳过重复" : "重复 · 新增" : "新增" }).ToArray();
        SummaryLabel.Text = $"备份共 {incoming.Count} 条 · 将新增 {plan.Added} 条 · 跳过 {plan.Skipped} 条。";
        ImportButton.Content = $"确认导入（{plan.Added}）"; ImportButton.IsEnabled = plan.Added > 0;
        UIElementAutomationPeer.CreatePeerForElement(SummaryLabel)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void OnImport(object sender, RoutedEventArgs e) => DialogResult = true;
}
