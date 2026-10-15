using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using ArchivePassword.Infrastructure;
using Microsoft.Win32;

namespace ArchivePassword.Desktop;

internal sealed class ThemeResources
{
    private readonly ResourceDictionary resources;
    private readonly HashSet<Window> windows = [];
    private readonly Dictionary<Window, bool> titleModes = [];
    private bool highContrast;
    internal string Mode { get; private set; } = "light";
    internal bool IsDark { get; private set; }
    internal bool IsHighContrast => highContrast;
    internal event Action? Changed;
    internal ThemeResources(ResourceDictionary resources)
    {
        this.resources = resources;
        defaults = Keys.ToDictionary(key => key, key => resources[key]);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
    }
    private static readonly string[] Keys =
    [
        "CanvasBrush", "SurfaceBrush", "TextBrush", "SecondaryBrush", "BorderBrush", "DividerBrush",
        "AccentBrush", "OnAccentBrush", "SelectionBrush", "SelectionTextBrush", "FocusBrush", "ErrorBrush"
    ];
    private readonly Dictionary<string, object> defaults;
    private static readonly string[] DarkColors = ["#111827", "#1F2937", "#F3F4F6", "#CBD5E1", "#94A3B8", "#475569", "#93C5FD", "#111827", "#1E3A5F", "#BFDBFE", "#93C5FD", "#FCA5A5"];
    internal void SetMode(string mode)
    {
        if (!AppearancePreferenceStore.IsSupported(mode)) throw new ArgumentException("Unsupported appearance mode.", nameof(mode));
        Mode = mode; Apply(highContrast);
    }
    internal static bool ReadSystemDark()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    internal void Apply(bool highContrast, bool? systemDark = null)
    {
        this.highContrast = highContrast;
        IsDark = !highContrast && (Mode == "dark" || Mode == "system" && (systemDark ?? ReadSystemDark()));
        if (!highContrast)
        {
            for (var index = 0; index < Keys.Length; index++) resources[Keys[index]] = IsDark ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(DarkColors[index])) : defaults[Keys[index]];
        }
        else
        {
        resources["CanvasBrush"] = resources["SurfaceBrush"] = SystemColors.WindowBrush;
        resources["TextBrush"] = resources["SecondaryBrush"] = resources["BorderBrush"]
            = resources["DividerBrush"] = resources["FocusBrush"] = resources["ErrorBrush"] = SystemColors.WindowTextBrush;
        resources["AccentBrush"] = resources["SelectionBrush"] = SystemColors.HighlightBrush;
        resources["OnAccentBrush"] = resources["SelectionTextBrush"] = SystemColors.HighlightTextBrush;
        }
        // Native templates use these WPF system-resource keys. These are application
        // resources only; Windows settings and other applications are never changed.
        foreach (var key in new[] { SystemColors.WindowBrushKey, SystemColors.ControlBrushKey, SystemColors.InfoBrushKey }) resources[key] = resources["SurfaceBrush"];
        foreach (var key in new[] { SystemColors.WindowTextBrushKey, SystemColors.ControlTextBrushKey, SystemColors.InfoTextBrushKey }) resources[key] = resources["TextBrush"];
        resources[SystemColors.HighlightBrushKey] = resources["AccentBrush"];
        resources[SystemColors.HighlightTextBrushKey] = resources["OnAccentBrush"];
        resources[SystemColors.InactiveSelectionHighlightBrushKey] = resources["SelectionBrush"];
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = resources["SelectionTextBrush"];
        resources[SystemColors.GrayTextBrushKey] = resources["SecondaryBrush"];
        foreach (var window in windows) ApplyTitleBar(window);
        Changed?.Invoke();
    }
    private void OnWindowLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Window window || !windows.Add(window)) return;
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        if (source is not null) source.AddHook(OnWindowMessage);
        window.Closed += (_, _) => { source?.RemoveHook(OnWindowMessage); windows.Remove(window); titleModes.Remove(window); };
        ApplyTitleBar(window);
    }
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x001A or 0x0015) Application.Current.Dispatcher.BeginInvoke(() => Apply(SystemParameters.HighContrast));
        return IntPtr.Zero;
    }
    private void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && (!titleModes.TryGetValue(window, out var previous) || previous != IsDark))
        { titleModes[window] = IsDark; var value = IsDark ? 1 : 0; _ = DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
