using System.IO;
using System.Windows;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;
public sealed class ExtractionRulesEditor : ObservableObject
{
    private int location, layout, naming, collision;
    private string customPath="",error="";
    private bool openAfterSuccess;
    public int Location { get=>location;set { if(Set(ref location,value)) Changed(nameof(IsCustom)); } }
    public int Layout { get=>layout;set { if(Set(ref layout,value)) Changed(nameof(UsesContainer)); } }
    public int Naming { get=>naming;set=>Set(ref naming,value); }
    public int Collision { get=>collision;set=>Set(ref collision,value); }
    public bool OpenAfterSuccess { get=>openAfterSuccess;set=>Set(ref openAfterSuccess,value); }
    public string CustomPath { get=>customPath;set=>Set(ref customPath,value); }
    public string Error { get=>error;set=>Set(ref error,value); }
    public bool IsCustom=>Location==0;
    public bool UsesContainer=>Layout!=0;
    public IReadOnlyList<string> RecentPaths { get;init; }=[];
    public ExtractionDirectoryRules Rules=>new((ExtractionLocation)Location,(ExtractionLayout)Layout,(ContainerCollision)Collision,(ContainerNaming)Naming,OpenAfterSuccess);
    public bool Validate()
    {
        if(IsCustom && (string.IsNullOrWhiteSpace(CustomPath) || !Path.IsPathFullyQualified(CustomPath))) { Error="请选择或输入完整的自定义目录。";return false; }
        try { if(IsCustom) CustomPath=Path.GetFullPath(CustomPath); }
        catch(ArgumentException) { Error="目录格式无效，请重新输入。";return false; }
        Error="";return true;
    }
}
public partial class ExtractionRulesDialog : Window
{
    public ExtractionRulesEditor Editor { get; }
    public ExtractionRulesDialog(ExtractionDirectoryRules rules,string custom,IReadOnlyList<string> recent)
    {
        InitializeComponent();Editor=new() { Location=(int)rules.Location,Layout=(int)rules.Layout,Naming=(int)rules.Naming,Collision=(int)rules.Collision,OpenAfterSuccess=rules.OpenAfterSuccess,CustomPath=custom,RecentPaths=recent };DataContext=Editor;
        WindowPlacement.FitWorkArea(this,SystemParameters.WorkArea);Loaded+=(_,_)=>LocationBox.Focus();
    }
    private void OnApply(object sender,RoutedEventArgs e) { if(Editor.Validate()) DialogResult=true; }
}
