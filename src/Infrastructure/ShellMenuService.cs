using Microsoft.Win32;
using System.Runtime.InteropServices;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed class ShellMenuService(string executable, string classesPath = @"Software\Classes")
{
    public static readonly Guid HereClass = new("c708b15d-0d3e-47ab-8fc8-c22d21ff1171");
    public static readonly Guid FolderClass = new("c708b15d-0d3e-47ab-8fc8-c22d21ff1172");
    public static readonly Guid CompressClass = new("c708b15d-0d3e-47ab-8fc8-c22d21ff1173");
    public static readonly Guid[] Classes = [HereClass, FolderClass, CompressClass];
    public static readonly string[] MenuRoots = [@"SystemFileAssociations\.7z\shell\nomozip", @"SystemFileAssociations\.zip\shell\nomozip", @"SystemFileAssociations\.rar\shell\nomozip", @"*\shell\nomozip", @"Directory\shell\nomozip"];
    public static string ClassPath(Guid id) => @"CLSID\" + id.ToString("B");
    private string Owner => Path.GetFullPath(executable);
    private string Command => $"\"{Owner}\" --shell-server";
    public bool IsRegistered()
    {
        using var root = Registry.CurrentUser.OpenSubKey(classesPath);
        return root is not null && Classes.All(id =>
        {
            using var key = root.OpenSubKey(ClassPath(id) + @"\LocalServer32");
            return string.Equals(key?.GetValue("") as string, Command, StringComparison.OrdinalIgnoreCase);
        }) && MenuRoots.All(path => { using var key = root.OpenSubKey(path); return string.Equals(key?.GetValue("NomozipOwner") as string, Owner, StringComparison.OrdinalIgnoreCase); });
    }
    public void Register()
    {
        if (!File.Exists(Owner)) throw new FileNotFoundException("Application executable missing.");
        using var root = Registry.CurrentUser.CreateSubKey(classesPath);
        foreach (var id in Classes)
        {
            using var key = root.CreateSubKey(ClassPath(id)); key.SetValue("NomozipOwner", Owner);
            using var server = key.CreateSubKey("LocalServer32"); server.SetValue("", Command); server.SetValue("ServerExecutable", Owner);
        }
        for (var index = 0; index < MenuRoots.Length; index++)
        {
            using var key = root.CreateSubKey(MenuRoots[index]);
            key.SetValue("MUIVerb", "nomozip"); key.SetValue("Icon", $"\"{Owner}\",0");
            key.SetValue("SubCommands", ""); key.SetValue("NomozipOwner", Owner); key.SetValue("NeverDefault", ""); key.SetValue("MultiSelectModel", "Player");
            using var children = key.CreateSubKey("shell");
            if (index < 3)
            {
                AddVerb(children, "01ExtractHere", "解压到当前目录", HereClass);
                AddVerb(children, "02ExtractFolder", "解压到同名文件夹", FolderClass);
            }
            AddVerb(children, "03Compress", "压缩…", CompressClass);
        }
        Notify();
    }
    private void AddVerb(RegistryKey root, string name, string title, Guid id)
    {
        using var verb = root.CreateSubKey(name); verb.SetValue("MUIVerb", title); verb.SetValue("MultiSelectModel", "Player"); verb.SetValue("NeverDefault", "");
        using var command = verb.CreateSubKey("command"); command.SetValue("DelegateExecute", id.ToString("B"));
    }
    public bool Unregister()
    {
        using var root = Registry.CurrentUser.OpenSubKey(classesPath, writable: true);
        if (root is null) return false;
        var removed = false;
        foreach (var path in MenuRoots.Concat(Classes.Select(ClassPath)))
        {
            using var key = root.OpenSubKey(path);
            if (!string.Equals(key?.GetValue("NomozipOwner") as string, Owner, StringComparison.OrdinalIgnoreCase)) continue;
            key!.Close(); root.DeleteSubKeyTree(path, false); removed = true;
        }
        if (removed) Notify(); return removed;
    }
    private void Notify() { if (classesPath == @"Software\Classes") SHChangeNotify(0x08000000, 0, 0, 0); }
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
