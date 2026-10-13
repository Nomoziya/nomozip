using System.Windows;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed record ExtractionFailureRow(string Path, string Reason);

public partial class ExtractionFailureDialog : Window
{
    public ExtractionFailureDialog(string archiveName, EngineResult failure,bool testing=false)
    {
        InitializeComponent();
        ArchiveName.Text = archiveName; ArchiveName.ToolTip = archiveName;
        ResultSummary.Text = $"处理通过 {failure.SuccessfulItems} 项 · 失败 {failure.Failures.Count} 项";
        if(testing)
        {
            Title=ResultHeading.Text="压缩包校验结果";
            ResultSummary.Text=(failure.IsSuccess?"校验通过":failure.Status=="cancelled"?"已取消校验":failure.Code switch {"missing_volume"=>"缺少分卷文件","wrong_password"=>"密码不匹配","password_or_corrupt"=>"密码不匹配或文件损坏","unsupported_or_corrupt"=>"格式不支持或文件损坏",_=>"校验未通过"})+" · "+ResultSummary.Text;
            ContextDescription.Text="本次仅校验，没有输出文件或更新密码库。格式未提供校验值时，仅验证数据可读取性。";
            System.Windows.Automation.AutomationProperties.SetName(FailureTable,"压缩包校验失败条目列表");
        }
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
