using System.Windows;

namespace ArchivePassword.Desktop;

internal sealed class ThemeResources(ResourceDictionary resources)
{
    private readonly Dictionary<string, object> defaults = new[]
    {
        "CanvasBrush", "SurfaceBrush", "TextBrush", "SecondaryBrush", "BorderBrush", "DividerBrush",
        "AccentBrush", "OnAccentBrush", "SelectionBrush", "SelectionTextBrush", "FocusBrush", "ErrorBrush"
    }.ToDictionary(key => key, key => resources[key]);

    internal void Apply(bool highContrast)
    {
        if (!highContrast)
        {
            foreach (var entry in defaults) resources[entry.Key] = entry.Value;
            return;
        }
        resources["CanvasBrush"] = resources["SurfaceBrush"] = SystemColors.WindowBrush;
        resources["TextBrush"] = resources["SecondaryBrush"] = resources["BorderBrush"]
            = resources["DividerBrush"] = resources["FocusBrush"] = resources["ErrorBrush"] = SystemColors.WindowTextBrush;
        resources["AccentBrush"] = resources["SelectionBrush"] = SystemColors.HighlightBrush;
        resources["OnAccentBrush"] = resources["SelectionTextBrush"] = SystemColors.HighlightTextBrush;
    }
}
