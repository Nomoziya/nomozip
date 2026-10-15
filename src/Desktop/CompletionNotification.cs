using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

// A short-lived notification-area icon; no package identity or extra runtime is needed.
internal sealed class CompletionNotification : IDisposable
{
    internal const int CallbackMessage = 0x8000 + 73;
    internal const uint IconId = 1;
    internal delegate bool ShellCall(uint operation, ref IconData data);
    private readonly HwndSource source;
    private readonly IntPtr windowHandle;
    private readonly ShellCall shell;
    private readonly Action activate;
    private readonly DispatcherTimer expiry;
    private readonly uint taskbarCreated;
    private bool registered, disposed;

    internal CompletionNotification(Window window, Action activate, ShellCall? shell = null)
    {
        this.activate = activate;
        this.shell = shell ?? Shell_NotifyIconW;
        windowHandle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(windowHandle)!;
        source.AddHook(OnMessage);
        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        expiry = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background,
            (_, _) => RemoveIcon(), window.Dispatcher);
        expiry.Stop();
    }

    internal static string Body(int count) => UiText.Format("T_16ad9673c5f8", count);

    internal bool EnsureIcon()
    {
        if (disposed) return false;
        if (registered) return true;
        var data = Data();
        data.Flags = 1 | 2 | 4; // NIF_MESSAGE | NIF_ICON | NIF_TIP; never displays a balloon.
        data.Callback = CallbackMessage;
        data.Icon = LoadIconW(IntPtr.Zero, new IntPtr(32512)); // Shared Windows application icon.
        data.Tip = UiText.Get("T_e5bce8324529");
        registered = shell(0, ref data); // NIM_ADD, legacy callback layout.
        return registered;
    }

    internal bool Show(int count)
    {
        if (count <= 0 || !EnsureIcon()) return false;
        var data = Data();
        data.Flags = 0x10 | 0x40; // NIF_INFO | NIF_REALTIME: discard if not immediately useful.
        data.Info = Body(count);
        data.Title = UiText.Get("T_cab1a449f534");
        data.InfoFlags = 1 | 0x10 | 0x80; // Information, no sound, respect first-login quiet time.
        if (!shell(1, ref data)) { RemoveIcon(); return false; }
        expiry.Stop(); expiry.Start();
        return true; // Accepted by the Shell; this does not guarantee visible delivery.
    }

    private IconData Data() => new() { Size = (uint)Marshal.SizeOf<IconData>(), Window = windowHandle, Id = IconId,
        Tip = "", Info = "", Title = "" };

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        HandleMessage(message, wParam, lParam);
        return IntPtr.Zero;
    }

    internal void HandleMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (disposed) return;
        if (taskbarCreated != 0 && message == taskbarCreated)
        {
            // Explorer owns the old icon. Do not replay stale completion messages after its restart.
            expiry.Stop(); registered = false; return;
        }
        if (message != CallbackMessage || wParam.ToInt64() != IconId || !registered) return;
        switch (lParam.ToInt32())
        {
            case 0x405: // NIN_BALLOONUSERCLICK
            case 0x202: // WM_LBUTTONUP
            case 0x400: // NIN_SELECT
            case 0x401: // NIN_KEYSELECT
                RemoveIcon(); activate(); break;
            case 0x403: // NIN_BALLOONHIDE
            case 0x404: // NIN_BALLOONTIMEOUT
                RemoveIcon(); break;
        }
    }

    internal void RemoveIcon()
    {
        expiry.Stop();
        if (!registered) return;
        var data = Data(); shell(2, ref data); registered = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        RemoveIcon(); source.RemoveHook(OnMessage); disposed = true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct IconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIconW(uint operation, ref IconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint RegisterWindowMessageW(string name);
}
