using System.Runtime.InteropServices;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

// The application is a local COM server. Explorer never loads the .NET runtime.
internal sealed class ShellCommandServer : IDisposable
{
    private readonly List<uint> cookies = [];
    private readonly List<object> factories = [];
    private readonly Dispatcher dispatcher;
    private readonly Action<ShellSelection> accept;
    private readonly DispatcherTimer timeout;
    private int accepted;
    public ShellCommandServer(Dispatcher dispatcher, Action<ShellSelection> accept, Action expired)
    {
        this.dispatcher = dispatcher; this.accept = accept;
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
        timeout = new DispatcherTimer(TimeSpan.FromSeconds(45), DispatcherPriority.Background, (_, _) => expired(), dispatcher);
        foreach (var action in Enum.GetValues<ShellAction>())
        {
            var factory = new ShellClassFactory(() => new ShellExecuteCommand(this, action)); factories.Add(factory);
            var pointer = Marshal.GetIUnknownForObject(factory);
            try
            {
                var id = ShellMenuService.Classes[(int)action];
                Marshal.ThrowExceptionForHR(CoRegisterClassObject(in id, pointer, 4, 1, out var cookie)); cookies.Add(cookie);
            }
            finally { Marshal.Release(pointer); }
        }
        timeout.Start();
    }
    internal int Accept(ShellSelection selection)
    {
        try { selection = selection.Validate(); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or System.IO.IOException) { return unchecked((int)0x80070057); }
        if (Interlocked.CompareExchange(ref accepted, 1, 0) != 0) return unchecked((int)0x800700AA);
        dispatcher.BeginInvoke(() => { Dispose(); accept(selection); });
        return 0;
    }
    public void Dispose()
    {
        timeout.Stop(); foreach (var cookie in cookies) CoRevokeClassObject(cookie); cookies.Clear(); factories.Clear();
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern int CoRegisterClassObject(in Guid clsid, nint factory, uint context, uint flags, out uint cookie);
    [DllImport("ole32.dll")] private static extern int CoRevokeClassObject(uint cookie);
}

[ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, in Guid iid, out nint instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ShellClassFactory(Func<object> create) : IShellClassFactory
{
    public int CreateInstance(nint outer, in Guid iid, out nint instance)
    {
        instance = 0; if (outer != 0) return unchecked((int)0x80040110);
        var pointer = Marshal.GetIUnknownForObject(create());
        try { return Marshal.QueryInterface(pointer, in iid, out instance); }
        finally { Marshal.Release(pointer); }
    }
    public int LockServer(bool locked) => 0;
}
[ComVisible(true), Guid("7f9185b0-cb92-43c5-80a9-92277a4f7b54"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellExecuteCommand
{
    [PreserveSig] int SetKeyState(uint state);
    [PreserveSig] int SetParameters([MarshalAs(UnmanagedType.LPWStr)] string? parameters);
    [PreserveSig] int SetPosition(ShellPoint point);
    [PreserveSig] int SetShowWindow(int show);
    [PreserveSig] int SetNoShowUI([MarshalAs(UnmanagedType.Bool)] bool noUi);
    [PreserveSig] int SetDirectory([MarshalAs(UnmanagedType.LPWStr)] string? directory);
    [PreserveSig] int Execute();
}
[StructLayout(LayoutKind.Sequential)] public struct ShellPoint { public int X, Y; }
[ComVisible(true), Guid("1c9cd5bb-98e9-4491-a60f-31aacc72b83c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellObjectWithSelection
{
    [PreserveSig] int SetSelection([MarshalAs(UnmanagedType.Interface)] IShellItemArray? items);
    [PreserveSig] int GetSelection(in Guid iid, out nint selection);
}
[ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    [PreserveSig] int BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
    [PreserveSig] int GetPropertyStore(uint flags, in Guid iid, out nint result);
    [PreserveSig] int GetPropertyDescriptionList(nint property, in Guid iid, out nint result);
    [PreserveSig] int GetAttributes(uint flags, uint mask, out uint attributes);
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemAt(uint index, out IShellItem item);
    [PreserveSig] int EnumItems(out nint items);
}
[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    [PreserveSig] int BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
    [PreserveSig] int GetParent(out IShellItem parent);
    [PreserveSig] int GetDisplayName(uint name, out nint value);
    [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ShellExecuteCommand : IShellExecuteCommand, IShellObjectWithSelection
{
    private readonly ShellCommandServer server;
    private readonly ShellAction action;
    private IShellItemArray? selected;
    internal ShellExecuteCommand(ShellCommandServer server, ShellAction action) { this.server = server; this.action = action; }
    public int SetSelection(IShellItemArray? items) { selected = items; return 0; }
    public int GetSelection(in Guid iid, out nint selection)
    {
        selection = 0; if (selected is null) return unchecked((int)0x80004005);
        var pointer = Marshal.GetIUnknownForObject(selected);
        try { return Marshal.QueryInterface(pointer, in iid, out selection); }
        finally { Marshal.Release(pointer); }
    }
    public int Execute()
    {
        if (selected is null) return unchecked((int)0x80070057);
        var paths = new List<string>();
        try
        {
            Marshal.ThrowExceptionForHR(selected.GetCount(out var count));
            if (count is 0 or > 10_000) return unchecked((int)0x80070057);
            for (uint index = 0; index < count; index++)
            {
                Marshal.ThrowExceptionForHR(selected.GetItemAt(index, out var item));
                try
                {
                    Marshal.ThrowExceptionForHR(item.GetDisplayName(0x80058000, out var text));
                    try { paths.Add(Marshal.PtrToStringUni(text)!); }
                    finally { Marshal.FreeCoTaskMem(text); }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            return server.Accept(new ShellSelection(action, paths));
        }
        catch (COMException error) { return error.HResult; }
        finally { Marshal.ReleaseComObject(selected); selected = null; }
    }
    public int SetKeyState(uint state) => 0;
    public int SetParameters(string? parameters) => 0;
    public int SetPosition(ShellPoint point) => 0;
    public int SetShowWindow(int show) => 0;
    public int SetNoShowUI(bool noUi) => 0;
    public int SetDirectory(string? directory) => 0;
}
