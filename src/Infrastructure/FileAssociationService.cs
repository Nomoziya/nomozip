using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace ArchivePassword.Infrastructure;
public sealed class FileAssociationService(string executable, string classesPath = @"Software\Classes")
{
    public const string ProgId = "nomozip.Archive";
    private static readonly string[] Extensions = [".7z", ".zip", ".rar"];
    private string Command => $"\"{Path.GetFullPath(executable)}\" \"%1\"";
    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(classesPath + "\\" + ProgId + @"\shell\open\command");
        return string.Equals(key?.GetValue("") as string, Command, StringComparison.OrdinalIgnoreCase);
    }
    public void Register()
    {
        using var root = Registry.CurrentUser.CreateSubKey(classesPath);
        using var program = root.CreateSubKey(ProgId);
        program.SetValue("", UiText.Get("T_e25466fdb9be"));
        program.SetValue("FriendlyTypeName", UiText.Get("T_e25466fdb9be"));
        using (var application = program.CreateSubKey("Application")) application.SetValue("ApplicationName", "nomozip");
        using (var icon = program.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{Path.GetFullPath(executable)}\",0");
        using (var command = program.CreateSubKey(@"shell\open\command")) command.SetValue("", Command);
        using (var open = program.CreateSubKey(@"shell\open")) open.SetValue("MultiSelectModel", "Single");
        foreach (var extension in Extensions)
        {
            using var openWith = root.CreateSubKey(extension + @"\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        NotifyShell();
    }
    public bool Unregister()
    {
        // A portable or older installation cannot remove another copy's registration.
        if (!IsRegistered()) return false;
        using var root = Registry.CurrentUser.OpenSubKey(classesPath, writable: true)!;
        foreach (var extension in Extensions)
        {
            using var openWith = root.OpenSubKey(extension + @"\OpenWithProgids", writable: true);
            openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
        }
        root.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        NotifyShell(); return true;
    }
    private void NotifyShell()
    {
        if (classesPath == @"Software\Classes") SHChangeNotify(0x08000000, 0, 0, 0);
    }
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
