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
        Loaded += (_, _) => ModeBox.Focus();
        Closed += (_, _) => PreviewTable.ItemsSource = null;
    }
    private void ModeChanged(object sender, SelectionChangedEventArgs e) { if (SummaryLabel is not null) RefreshPreview(); }
    private void RefreshPreview()
    {
        var plan = PasswordImportPlanner.Build(existing, incoming, Mode);
        PreviewTable.ItemsSource = plan.Rows.Select(row => new { row.Password.Name, row.Password.Notes,
            Action = row.Duplicate ? Mode == PasswordImportMode.SkipDuplicates ? UiText.Get("T_901f39739e0d") : UiText.Get("T_f710925bd393") : UiText.Get("T_0006d696d8e1") }).ToArray();
        SummaryLabel.Text = UiText.Format("T_fb4d81fcdb9e", incoming.Count, plan.Added, plan.Skipped);
        ImportButton.Content = UiText.Format("T_28295bf98b81", plan.Added); ImportButton.IsEnabled = plan.Added > 0;
        UIElementAutomationPeer.CreatePeerForElement(SummaryLabel)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void OnImport(object sender, RoutedEventArgs e) => DialogResult = true;
}
