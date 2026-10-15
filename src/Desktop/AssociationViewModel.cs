using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows.Input;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private string associationMessage = UiText.Get("T_49d677742458");
    public string AssociationMessage { get => associationMessage; set => Set(ref associationMessage, value); }
    public ICommand RegisterAssociationsCommand { get; private set; } = null!;
    public ICommand RemoveAssociationsCommand { get; private set; } = null!;
    public ICommand OpenDefaultAppsCommand { get; private set; } = null!;
    public ICommand RegisterShellMenuCommand { get; private set; } = null!;
    public ICommand RemoveShellMenuCommand { get; private set; } = null!;
    private string shellMenuMessage = UiText.Get("T_1adbe504ab1f");
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
            if (register) { menu.Register(); ShellMenuMessage = UiText.Get("T_62dc5851e8e4"); }
            else ShellMenuMessage = menu.Unregister() ? UiText.Get("T_fa2d0a57ea4e") : UiText.Get("T_8d0a0f895554");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException) { ShellMenuMessage = UiText.Get("T_9c24f5fd0bf1"); }
    }
    private void ChangeAssociations(bool register)
    {
        try
        {
            if (register) { Associations.Register(); AssociationMessage = UiText.Get("T_720075fc0e18"); }
            else AssociationMessage = Associations.Unregister() ? UiText.Get("T_ba71a8541da6") : UiText.Get("T_95b303abcdd4");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException) { AssociationMessage = UiText.Get("T_817bc97cce36"); }
    }
}
