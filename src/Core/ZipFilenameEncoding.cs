namespace ArchivePassword.Core;

public sealed record ZipFilenameEncoding(int CodePage, string Label)
{
    public static IReadOnlyList<ZipFilenameEncoding> Choices { get; } = [
        new(0, UiText.Get("T_5266972ebb28")), new(936, UiText.Get("T_d6c877caa132")),
        new(932, UiText.Get("T_441ac4c7443f")), new(437, "DOS / OEM · CP437"), new(65001, "UTF-8 / CP65001")];
    public static bool IsSupported(int codePage) => Choices.Any(value => value.CodePage == codePage);
}
