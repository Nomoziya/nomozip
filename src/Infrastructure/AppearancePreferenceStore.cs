using System.Text.Json;

namespace ArchivePassword.Infrastructure;

public sealed record AppearancePreference(string Mode, bool CanWrite);
public static class AppearancePreferenceStore
{
    public const string FileName = "appearance.json";
    private sealed record Preference(int Version, string Mode);
    public static bool IsSupported(string? mode) => mode is "system" or "light" or "dark";
    public static AppearancePreference Read(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new("system", true);
        try
        {
            var value = JsonSerializer.Deserialize<Preference>(File.ReadAllText(path));
            return value is { Version: 1 } && IsSupported(value.Mode) ? new(value.Mode, true) : new("system", false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return new("system", false); }
    }
    public static async Task SaveAsync(string directory, string mode)
    {
        if (!IsSupported(mode)) throw new ArgumentException("Unsupported appearance mode.", nameof(mode));
        if (!Read(directory).CanWrite) throw new InvalidDataException("Existing appearance preference was preserved.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Preference(1, mode)));
            File.Move(temporary, Path.Combine(directory, FileName), true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
