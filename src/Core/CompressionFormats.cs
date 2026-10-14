namespace ArchivePassword.Core;

public sealed record CompressionFormatInfo(string Name, string Label, string Extension,
    bool SingleFile, bool Encryption, bool CompressionLevel);

public static class CompressionFormats
{
    // Order preserves the existing desktop 7z / ZIP selections.
    public static IReadOnlyList<CompressionFormatInfo> All { get; } = Array.AsReadOnly(new[]
    {
        new CompressionFormatInfo("7z", "7z", ".7z", false, true, true),
        new CompressionFormatInfo("zip", "ZIP", ".zip", false, true, true),
        new CompressionFormatInfo("tar", "TAR", ".tar", false, false, false),
        new CompressionFormatInfo("gzip", "GZIP", ".gz", true, false, true),
        new CompressionFormatInfo("xz", "XZ", ".xz", true, false, true)
    });
    public static CompressionFormatInfo? Find(string name) => All.FirstOrDefault(value => value.Name == name);
}
