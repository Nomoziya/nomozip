namespace ArchivePassword.Core;

public sealed record ZipFilenameEncoding(int CodePage, string Label)
{
    public static IReadOnlyList<ZipFilenameEncoding> Choices { get; } = [
        new(0, "默认（沿用引擎）"), new(936, "简体中文 · GBK / CP936"),
        new(932, "日文 · Shift-JIS / CP932"), new(437, "DOS / OEM · CP437"), new(65001, "UTF-8 / CP65001")];
    public static bool IsSupported(int codePage) => Choices.Any(value => value.CodePage == codePage);
}
