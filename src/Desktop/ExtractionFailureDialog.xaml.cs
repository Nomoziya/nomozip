using System.Windows;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed record ExtractionFailureRow(string Path, string Reason);

public partial class ExtractionFailureDialog : Window
{
    public ExtractionFailureDialog(string archiveName, EngineResult failure,bool testing=false)
    {
        InitializeComponent();
        if(failure.IsSuccess) {ResultIcon.Kind="CheckmarkCircle";ResultIcon.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,"SuccessBrush");}
        ArchiveName.Text = archiveName; ArchiveName.ToolTip = archiveName;
        ResultSummary.Text = UiText.Format("T_825e66873fc3", failure.SuccessfulItems, failure.Failures.Count);
        if(testing)
        {
            Title=ResultHeading.Text=UiText.Get("T_e7758edec13b");
            ResultSummary.Text=(failure.IsSuccess?UiText.Get("T_3051a776eaa5"):failure.Status=="cancelled"?UiText.Get("T_90b5a4f6315a"):failure.Code switch {"missing_volume"=>UiText.Get("T_a2e6438fa7e0"),"wrong_password"=>UiText.Get("T_c62af9393001"),"password_or_corrupt"=>UiText.Get("T_64acf7e7c429"),"unsupported_or_corrupt"=>UiText.Get("T_21777c1adb09"),_=>UiText.Get("T_18ce943d8b00")})+" · "+ResultSummary.Text;
            ContextDescription.Text=UiText.Get("T_343f89b343d4");
            System.Windows.Automation.AutomationProperties.SetName(FailureTable,UiText.Get("T_f4f9aaac57e4"));
        }
        FailureTable.ItemsSource = failure.Failures.Select(value => new ExtractionFailureRow(
            string.IsNullOrEmpty(value.Path) ? UiText.Format("T_6452568341e4", value.Index + 1) : value.Path,
            value.Code switch
            {
                "wrong_password" => UiText.Get("T_c62af9393001"),
                "password_or_corrupt" => UiText.Get("T_64acf7e7c429"),
                "unsupported_method" => UiText.Get("T_121d1465121a"),
                "corrupt_archive" => UiText.Get("T_9fd0e26026e2"),
                _ => UiText.Get("T_ae173945fb80")
            })).ToArray();
        void FitColumns()
        {
            var width=Math.Max(0,FailureTable.ActualWidth-18);
            FailureTable.Columns[0].Width=Math.Max(200,width*.5);
            FailureTable.Columns[1].Width=Math.Max(160,width-FailureTable.Columns[0].Width.Value);
        }
        FailureTable.SizeChanged+=(_,_)=>FitColumns();
        Loaded += (_, _) => {FitColumns();FailureTable.Focus();};
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
