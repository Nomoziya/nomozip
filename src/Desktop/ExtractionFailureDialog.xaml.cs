using System.Windows;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed record ExtractionFailureRow(string Path, string Reason);

public partial class ExtractionFailureDialog : Window
{
    public ExtractionFailureDialog(string archiveName, EngineResult failure)
    {
        InitializeComponent();
        ArchiveName.Text = archiveName; ArchiveName.ToolTip = archiveName;
        ResultSummary.Text = $"处理通过 {failure.SuccessfulItems} 项 · 失败 {failure.Failures.Count} 项";
        FailureTable.ItemsSource = failure.Failures.Select(value => new ExtractionFailureRow(
            string.IsNullOrEmpty(value.Path) ? $"条目 {value.Index + 1}（引擎未提供路径）" : value.Path,
            value.Code switch
            {
                "wrong_password" => "密码不匹配",
                "password_or_corrupt" => "密码不匹配或文件损坏",
                "unsupported_method" => "不支持的压缩方法",
                "corrupt_archive" => "文件损坏",
                _ => "处理失败"
            })).ToArray();
        WindowPlacement.FitWorkArea(this, SystemParameters.WorkArea);
        Loaded += (_, _) => FailureTable.Focus();
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
