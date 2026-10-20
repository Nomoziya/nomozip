using System.Globalization;

namespace ArchivePassword.Core;

public sealed record ArchiveEntryFilter(string[] Extensions, ulong? MinimumBytes, ulong? MaximumBytes, int Kind)
{
    public bool Matches(ArchiveEntry entry)
    {
        if (entry.Directory) return Kind != 1 && Extensions.Length == 0 && MinimumBytes is null && MaximumBytes is null;
        return Kind != 2 && (MinimumBytes is null || entry.Size >= MinimumBytes) && (MaximumBytes is null || entry.Size <= MaximumBytes)
            && (Extensions.Length == 0 || Extensions.Any(extension => entry.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool TryCreate(string extensions, string minimum, string maximum, int kind, out ArchiveEntryFilter filter)
    {
        var types = extensions.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.StartsWith('.') ? value : "." + value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var validTypes = types.All(value => value.Length > 1 && value.Skip(1).All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
        var validMinimum = Size(minimum, true, out var min);
        var validMaximum = Size(maximum, false, out var max);
        filter = new(types, min, max, kind);
        return validTypes && validMinimum && validMaximum && kind is >= 0 and <= 2 && (min is null || max is null || min <= max);
    }

    private static bool Size(string text, bool minimum, out ulong? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.CurrentCulture, out var size) || size < 0 || size > ulong.MaxValue / 1048576m) return false;
        bytes = (ulong)(minimum ? decimal.Ceiling(size * 1048576m) : decimal.Floor(size * 1048576m));
        return true;
    }
}
