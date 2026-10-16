using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ArchivePassword.Desktop;

public partial class FileKindIcon : UserControl
{
    internal static string TypeDescription(string path,bool directory)
    {
        if(directory)return UiText.Get("T_7c7802d8adae");
        var extension=Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        var key=extension switch
        {
            "TXT" or "MD" or "LOG"=>"FileType.Text",
            "PNG" or "JPG" or "JPEG" or "GIF" or "BMP" or "WEBP"=>"FileType.Image",
            "PDF"=>"FileType.Pdf",
            "XLS" or "XLSX" or "CSV"=>"FileType.Sheet",
            "ZIP" or "7Z" or "RAR" or "GZ" or "TAR" or "XZ"=>"FileType.Archive",
            _=>null
        };
        return key is null?UiText.Get("T_39932f24fe11"):UiText.Format(key,extension);
    }
    public static readonly DependencyProperty FileNameProperty=DependencyProperty.Register(nameof(FileName),typeof(string),typeof(FileKindIcon),new PropertyMetadata("",Changed));
    public static readonly DependencyProperty IsDirectoryProperty=DependencyProperty.Register(nameof(IsDirectory),typeof(bool),typeof(FileKindIcon),new PropertyMetadata(false,Changed));
    private static readonly DependencyPropertyKey IconKindPropertyKey=DependencyProperty.RegisterReadOnly(nameof(IconKind),typeof(string),typeof(FileKindIcon),new PropertyMetadata("file"));
    public static readonly DependencyProperty IconKindProperty=IconKindPropertyKey.DependencyProperty;
    public string FileName {get=>(string)GetValue(FileNameProperty);set=>SetValue(FileNameProperty,value);}
    public bool IsDirectory {get=>(bool)GetValue(IsDirectoryProperty);set=>SetValue(IsDirectoryProperty,value);}
    public string IconKind=>(string)GetValue(IconKindProperty);
    public FileKindIcon(){InitializeComponent();}
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        var icon=(FileKindIcon)target;
        icon.SetValue(IconKindPropertyKey,icon.IsDirectory?"folder":Path.GetExtension(icon.FileName??"").ToLowerInvariant()switch
        {".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp"=>"image",".pdf"=>"pdf",".xls" or ".xlsx" or ".csv"=>"sheet",".zip" or ".7z" or ".rar" or ".gz" or ".tar"=>"archive",_=>"file"});
    }
}
