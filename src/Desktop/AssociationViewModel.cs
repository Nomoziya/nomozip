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
    public ICommand RegisterShellMenuCommand { get; private set; } = null!;
    public ICommand RemoveShellMenuCommand { get; private set; } = null!;
    private string shellMenuMessage = "尚未更改右键菜单。添加后可直接解压或打开压缩页。";
    public string ShellMenuMessage { get => shellMenuMessage; set => Set(ref shellMenuMessage, value); }
    private FileAssociationService Associations => new(Path.Combine(baseDirectory, "ArchivePassword.exe"));
    private void InitializeAssociationCommands()
    {
        RegisterAssociationsCommand = new RelayCommand(() => ChangeAssociations(true), () => IsIdle);
        RemoveAssociationsCommand = new RelayCommand(() => ChangeAssociations(false), () => IsIdle);
        RegisterShellMenuCommand = new RelayCommand(() => ChangeShellMenu(true), () => IsIdle);
        RemoveShellMenuCommand = new RelayCommand(() => ChangeShellMenu(false), () => IsIdle);
        OpenDefaultAppsCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }), () => IsIdle);
    }
    private void ChangeShellMenu(bool register)
    {
        try
        {
            var menu = new ShellMenuService(Path.Combine(baseDirectory, "ArchivePassword.exe"), shellClassesPath);
            if (register) { menu.Register(); ShellMenuMessage = "已添加 nomozip 右键菜单。Windows 11 请在“显示更多选项”中使用；原默认应用保持不变。"; }
            else ShellMenuMessage = menu.Unregister() ? "已移除本程序的右键菜单。" : "当前菜单不属于此程序位置，已保留原登记。";
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException) { ShellMenuMessage = "右键菜单未能更改，请检查程序位置和当前用户权限。"; }
    }
    private void ChangeAssociations(bool register)
    {
        try
        {
            if (register) { Associations.Register(); AssociationMessage = "已添加打开方式入口，未更改默认应用。Windows 可能在首次双击时询问打开方式；若要保留原默认应用，请在系统窗口选择原应用。也可右键选择 nomozip，仅打开一次。"; }
            else AssociationMessage = Associations.Unregister() ? "已移除本程序的打开方式入口。" : "当前入口不属于此程序位置，已保留原登记。";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException) { AssociationMessage = "入口未能更改，请检查当前 Windows 用户的权限。"; }
    }
}
