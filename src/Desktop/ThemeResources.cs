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
        "AccentBrush", "OnAccentBrush", "SelectionBrush", "SelectionTextBrush", "FocusBrush", "ErrorBrush", "HoverBrush", "SuccessBrush", "ButtonBorderBrush", "FolderIconBrush", "PdfIconBrush", "SheetIconBrush", "IconOnFillBrush", "WarningSurfaceBrush", "WarningTextBrush"
    ];
    private readonly Dictionary<string, object> defaults;
    private static readonly string[] DarkColors = ["#202020", "#242424", "#F5F5F5", "#BDBDBD", "#879099", "#3D3D3D", "#75B6E7", "#242424", "#17354A", "#F5F5F5", "#75B6E7", "#F6A6A6", "#303030", "#6BCB8E", "#505050", "#F4B83B", "#D74842", "#16845C", "#FFFFFF", "#3B3423", "#E8C878"];
    private static readonly string[] SoftColors = ["#25282B","#2C3034","#E5E2DA","#C8C5BD","#8F969A","#454C50","#9BBCD0","#25282B","#304858","#E5E2DA","#C5B98B","#E3AAA3","#333B40","#93BAA3","#78858C","#C7AB65","#BC827D","#729B84","#25282B","#39372D","#CAB77C"];
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
        IsDark = !highContrast && (Mode is "dark" or "soft" || Mode == "system" && (systemDark ?? ReadSystemDark()));
        if (!highContrast)
        {
            var colors=Mode=="soft"?SoftColors:DarkColors;
            for (var index = 0; index < Keys.Length; index++) resources[Keys[index]] = IsDark ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index])) : defaults[Keys[index]];
        }
        else
        {
        resources["CanvasBrush"] = resources["SurfaceBrush"] = resources["HoverBrush"] = SystemColors.WindowBrush;
        resources["TextBrush"] = resources["SecondaryBrush"] = resources["BorderBrush"]
            = resources["FocusBrush"] = resources["ErrorBrush"] = resources["SuccessBrush"] = resources["ButtonBorderBrush"] = SystemColors.WindowTextBrush;
        resources["DividerBrush"] = SystemColors.WindowBrush;
        resources["AccentBrush"] = resources["SelectionBrush"] = SystemColors.HighlightBrush;
        resources["OnAccentBrush"] = resources["SelectionTextBrush"] = SystemColors.HighlightTextBrush;
        resources["FolderIconBrush"] = resources["PdfIconBrush"] = resources["SheetIconBrush"] = SystemColors.WindowTextBrush;
        resources["IconOnFillBrush"] = SystemColors.WindowBrush;
        resources["WarningSurfaceBrush"] = SystemColors.WindowBrush;
        resources["WarningTextBrush"] = SystemColors.WindowTextBrush;
        }
        resources["ChromeBrush"]=highContrast?SystemColors.WindowBrush:new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(Mode=="soft"?"#2A2E31":IsDark?"#252525":"#E9EEF5"),
            (Color)ColorConverter.ConvertFromString(Mode=="soft"?"#25282B":IsDark?"#202020":"#F3F5F8"),90);
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
