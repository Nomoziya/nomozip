using System.Windows;
using System.Windows.Media;

namespace Nomozip.DesignPreview;

public partial class App : Application
{
    public bool Dark { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ApplyTheme(false);
        var capture = e.Args.Length == 2 && e.Args[0] == "--capture";
        if (capture && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("Automated screenshot capture is restricted to isolated CI.");
        var window = new MainWindow(); MainWindow = window;
        if (capture)
            window.ContentRendered += async (_, _) =>
            {
                var passed = await Capture.RunAsync(window, e.Args[1]); Shutdown(passed ? 0 : 1);
            };
        window.Show();
    }
    public void ApplyTheme(bool dark, bool highContrast = false)
    {
        Dark = dark;
        var keys = new[] { "Canvas", "Surface", "Inset", "Text", "Muted", "Line", "ControlLine", "Accent", "OnAccent", "Selected", "SelectedText", "Hover", "Pressed", "AccentHover", "Disabled", "Success", "Danger" };
        var colors = dark
            ? new[] { "#202020", "#242424", "#202020", "#F5F5F5", "#BDBDBD", "#3C3C3C", "#737B85", "#75B6E7", "#102435", "#263E50", "#F2F7FC", "#30363C", "#354858", "#98C9EE", "#9098A0", "#8BD5A8", "#F1B5AF" }
            : new[] { "#F3F4F6", "#FFFFFF", "#F3F4F6", "#111722", "#515D6C", "#E3E7ED", "#BAC3CF", "#006CD9", "#FFFFFF", "#E2EFFD", "#111722", "#EAF2FA", "#D4E6FA", "#005BBC", "#8290A1", "#008F56", "#B52C2C" };
        for (var i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Resources["NavText"] = Resources["Accent"]; Resources["ActivitySurface"] = Resources["Surface"]; Resources["ActivityText"] = Resources["Text"];
        if (highContrast || SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "Canvas", "Surface", "Inset", "Hover", "Pressed" }) Resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Text", "Muted", "Line", "ControlLine", "Success", "Danger" }) Resources[key] = SystemColors.WindowTextBrush;
            foreach (var key in new[] { "Accent", "Selected", "AccentHover" }) Resources[key] = SystemColors.HighlightBrush;
            foreach (var key in new[] { "OnAccent", "SelectedText" }) Resources[key] = SystemColors.HighlightTextBrush;
            Resources["NavText"] = SystemColors.HighlightTextBrush; Resources["Disabled"] = SystemColors.GrayTextBrush;
            Resources["ActivitySurface"] = SystemColors.WindowBrush; Resources["ActivityText"] = SystemColors.WindowTextBrush;
        }
    }
}
