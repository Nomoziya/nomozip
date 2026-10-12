using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

internal static class DesktopDisplayValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, MainViewModel model, string root)
    {
        var checks = new Dictionary<string, bool>();
        var monitors = new List<MonitorInfo>();
        MonitorCallback callback = (nint monitor, nint dc, ref NativeRect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) monitors.Add(info);
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0) || monitors.Count == 0) throw new IOException("Display enumeration failed.");
        var measured = new List<object>();
        var failures = new List<object>();
        var hwnd = new WindowInteropHelper(window).Handle;
        var originalOpacity = window.Opacity;
        try
        {
            // Move only the isolated validation window. Opacity prevents it obscuring the user's desktop.
            window.Opacity = 0;
            model.Banner = ""; // Reset the synthetic high-contrast preview message from the preceding suite.
            foreach (var (monitor, index) in monitors.Select((value, index) => (value, index)))
            {
                var work = monitor.Work;
                if (!SetWindowPos(hwnd, 0, work.Left + 8, work.Top + 8, 0, 0, 0x0015)) throw new IOException("Display move failed.");
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                var dpi = GetDpiForWindow(hwnd);
                var visualDpi = VisualTreeHelper.GetDpi(window);
                measured.Add(new { index, monitor.Device, workPixels = new[] { work.Left, work.Top, work.Right, work.Bottom },
                    windowDpi = dpi, wpfDpiX = visualDpi.PixelsPerInchX, wpfDpiY = visualDpi.PixelsPerInchY,
                    awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd)) });
                checks[$"display {index} WPF matches native window DPI"] = dpi > 0 && Math.Abs(visualDpi.PixelsPerInchX - dpi) < 1;
                foreach (var scale in new[] { 1.25, 1.5, 2.0 })
                {
                    window.MinWidth = 800; window.MinHeight = 600; window.Width = 1120; window.Height = 760;
                    var simulatedWork = new Rect(0, 0, Math.Min(work.Right - work.Left, 1024) / scale, Math.Min(work.Bottom - work.Top, 720) / scale);
                    WindowPlacement.FitWorkArea(window, simulatedWork);
                    foreach (var page in new[] { 0, 3, 1, 2 })
                    {
                        model.Page = page;
                        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                        var reachable = true;
                        foreach (var control in Descendants(window).OfType<Control>().Where(control => control.TemplatedParent is null
                            && control.IsVisible && control.IsEnabled && control.Focusable && control is Button or TextBox or ComboBox or CheckBox))
                        {
                            control.BringIntoView();
                            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                            var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
                            var fits = bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
                                && bounds.Right <= window.ActualWidth + 1 && bounds.Bottom <= window.ActualHeight + 1;
                            reachable &= fits;
                            if (!fits) failures.Add(new { page, scale, control = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control)?.GetName(),
                                bounds = bounds.ToString(), windowSize = new[] { window.ActualWidth, window.ActualHeight } });
                        }
                        checks[$"display {index} simulated {scale * 100:0}% page {page} actions reachable"] = reachable;
                        if (index == 0 && scale == 2.0 && page is 0 or 3)
                            DesktopAccessibilityValidation.Render(window, Path.Combine(root, $"artifacts/accessibility-preview/compact-page-{page}-512x360.png"), 1.0);
                    }
                }
            }
        }
        finally
        {
            SetWindowPos(hwnd, 0, -32000, -32000, 0, 0, 0x0015);
            window.Opacity = originalOpacity;
            window.MinWidth = 800; window.MinHeight = 600; window.Width = 1120; window.Height = 760;
        }
        var passed = checks.Count == monitors.Count * 13 && checks.Values.All(value => value);
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts/display-validation.json"), JsonSerializer.Serialize(new
        { passed, total = checks.Count, checks, measured, failures, monitorCount = monitors.Count,
            multiMonitorMovementTested = monitors.Count > 1, operatingSystemScalingChanged = false,
            layoutWorkAreaPixelCap = new[] { 1024, 720 },
            scope = "Actual connected-display window DPI; 125/150/200% layout simulation with work-area pixels capped to 1024x720, including 512x360 DIP. No OS settings changed." }));
        return passed;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size; public NativeRect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, ref NativeRect rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(nint context);
}
