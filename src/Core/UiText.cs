using System.Globalization;
using System.Resources;

namespace ArchivePassword.Core;

/// <summary>Application text; persisted user data and protocol values are never translated.</summary>
public static class UiText
{
    private static readonly ResourceManager Resources = new("ArchivePassword.Core.Localization.Strings", typeof(UiText).Assembly);
    private static CultureInfo resourceCulture = CultureInfo.GetCultureInfo("zh-CN");
    public static string LanguageCode => resourceCulture.Name == "en-US" ? "en" : "zh-CN";
    public static bool IsSupported(string? language) => language is "zh-CN" or "en";
    public static void Configure(string? language)
    {
        resourceCulture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = resourceCulture;
        CultureInfo.DefaultThreadCurrentUICulture = resourceCulture;
        CultureInfo.CurrentCulture = resourceCulture;
        CultureInfo.CurrentUICulture = resourceCulture;
    }
    public static string Get(string key) => Resources.GetString(key, resourceCulture)
        ?? throw new MissingManifestResourceException("Missing application text: " + key);
    public static string Format(string key, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Get(key), values);
}
