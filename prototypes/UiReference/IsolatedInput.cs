using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nomozip.DesignPreview;

// Only the isolated CI capture entry uses native input. The preview's normal
// interaction path never calls this helper.
internal static class IsolatedInput
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static void Guard() { if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Native input is restricted to isolated CI."); }
    internal static void Activate(Window window) { Guard(); SetForegroundWindow(new WindowInteropHelper(window).Handle); window.Activate(); }
    internal static void MoveTo(FrameworkElement element) { Guard(); var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2)); if (!SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y))) throw new InvalidOperationException("CI pointer move failed."); }
    internal static void LeftDown() { Guard(); mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); }
    internal static void LeftUp() { Guard(); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
    internal static void Tab() { Guard(); keybd_event(0x09, 0, 0, UIntPtr.Zero); keybd_event(0x09, 0, 2, UIntPtr.Zero); }
}
