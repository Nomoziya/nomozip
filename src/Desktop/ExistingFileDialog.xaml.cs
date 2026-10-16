using System.IO;
using System.Windows;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public partial class ExistingFileDialog : Window
{
    private readonly ExistingFileConflict conflict;
    public ExistingFileDecision Decision {get;private set;}=new(ExistingFilePolicy.Ask);
    public ExistingFileDialog(ExistingFileConflict conflict)
    {
        InitializeComponent();this.conflict=conflict;
        ExistingName.Text=IncomingName.Text=Path.GetFileName(conflict.TargetPath);
        ExistingName.ToolTip=conflict.TargetPath;IncomingName.ToolTip=conflict.SourcePath;
        static string Describe(string path)=>File.Exists(path)?UiText.Format("Conflict.FileDetails",new FileInfo(path).Length.ToString("N0"),File.GetLastWriteTime(path).ToString("g")):UiText.Get("Conflict.Directory");
        ExistingDetails.Text=Describe(conflict.TargetPath);IncomingDetails.Text=Describe(conflict.SourcePath);
        NewName.Text=ConflictFileNames.Suggest(conflict.TargetPath,path=>conflict.ReservedTargets?.Contains(path)==true);
        ReplaceButton.IsEnabled=!Directory.Exists(conflict.TargetPath);
        Loaded+=(_,_)=>ReplaceButton.Focus();
    }
    private void Accept(ExistingFilePolicy policy,string? name=null) {Decision=new(policy,name,ApplyAll.IsChecked==true);DialogResult=true;}
    private void OnReplace(object sender,RoutedEventArgs e)=>Accept(ExistingFilePolicy.Replace);
    private void OnSkip(object sender,RoutedEventArgs e)=>Accept(ExistingFilePolicy.Skip);
    private void OnRename(object sender,RoutedEventArgs e)
    {
        if(RenamePanel.Visibility!=Visibility.Visible) {RenamePanel.Visibility=Visibility.Visible;NewName.Focus();NewName.SelectAll();return;}
        var name=NewName.Text;
        if(!ConflictFileNames.IsValid(name)) {ErrorLabel.Text=UiText.Get("Conflict.InvalidName");NewName.Focus();return;}
        var target=Path.Combine(Path.GetDirectoryName(conflict.TargetPath)!,name);
        if(File.Exists(target)||Directory.Exists(target)||conflict.ReservedTargets?.Contains(target)==true) {ErrorLabel.Text=UiText.Get("Conflict.NameExists");NewName.Focus();return;}
        Accept(ExistingFilePolicy.Rename,name);
    }
}
