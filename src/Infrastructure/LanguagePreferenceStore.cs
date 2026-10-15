using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed record LanguagePreference(string Language, bool CanWrite);
public static class LanguagePreferenceStore
{
    public const string FileName = "ui-language.json";
    private sealed record Preference(int Version, string Language);
    public static LanguagePreference Read(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path)) return new("zh-CN", true);
        try
        {
            var value = JsonSerializer.Deserialize<Preference>(File.ReadAllText(path));
            return value is { Version: 1 } && UiText.IsSupported(value.Language)
                ? new(value.Language, true) : new("zh-CN", false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return new("zh-CN", false); }
    }
    public static async Task SaveAsync(string dataDirectory, string language)
    {
        if (!UiText.IsSupported(language)) throw new ArgumentException("Unsupported UI language.", nameof(language));
        if (!Read(dataDirectory).CanWrite) throw new InvalidDataException("Existing language preference was preserved.");
        Directory.CreateDirectory(dataDirectory);
        var temporary = Path.Combine(dataDirectory, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Preference(1, language)));
            File.Move(temporary, Path.Combine(dataDirectory, FileName), true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
