using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows.Input;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private string associationMessage = "可将 nomozip 添加到 7z、ZIP、RAR 的打开方式。";
    public string AssociationMessage { get => associationMessage; set => Set(ref associationMessage, value); }
    public ICommand RegisterAssociationsCommand { get; private set; } = null!;
    public ICommand RemoveAssociationsCommand { get; private set; } = null!;
    public ICommand OpenDefaultAppsCommand { get; private set; } = null!;
    private FileAssociationService Associations => new(Path.Combine(baseDirectory, "ArchivePassword.exe"));
    private void InitializeAssociationCommands()
    {
        RegisterAssociationsCommand = new RelayCommand(() => ChangeAssociations(true), () => IsIdle);
        RemoveAssociationsCommand = new RelayCommand(() => ChangeAssociations(false), () => IsIdle);
        OpenDefaultAppsCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }), () => IsIdle);
    }
    private void ChangeAssociations(bool register)
    {
        try
        {
            if (register) { Associations.Register(); AssociationMessage = "已添加打开方式入口。可右键压缩包，选择“打开方式”中的 nomozip。"; }
            else AssociationMessage = Associations.Unregister() ? "已移除本程序的打开方式入口。" : "当前入口不属于此程序位置，已保留原登记。";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException) { AssociationMessage = "入口未能更改，请检查当前 Windows 用户的权限。"; }
    }
}
