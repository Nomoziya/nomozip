namespace ArchivePassword.Core;

/// <summary>Captures existing origin metadata once, then applies it only to files due for commit.</summary>
public interface IExtractionOriginReader
{
    Task<IExtractionOriginMark?> ReadAsync(string archive, CancellationToken token);
}
public interface IExtractionOriginMark
{
    Task<string?> ApplyAsync(IReadOnlyList<string> stagedFiles, CancellationToken token);
}
public sealed class ExtractionOriginException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
