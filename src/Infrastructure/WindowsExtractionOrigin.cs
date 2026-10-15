using System.ComponentModel;
using System.Runtime.InteropServices;
using ArchivePassword.Core;
using Microsoft.Win32.SafeHandles;

namespace ArchivePassword.Infrastructure;

/// <summary>Preserves only the existing Zone.Identifier stream; it never invents a zone or unblocks a file.</summary>
public sealed class WindowsExtractionOrigin(Func<SafeFileHandle, bool?>? streamSupport = null) : IExtractionOriginReader
{
    private const string StreamName = ":Zone.Identifier";
    private const int MaximumBytes = 65536;
    public Task<IExtractionOriginMark?> ReadAsync(string archive, CancellationToken token) => Task.Run(() => ReadCoreAsync(archive, token), token);
    private async Task<IExtractionOriginMark?> ReadCoreAsync(string archive, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var path = Path.GetFullPath(archive);
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if ((streamSupport ?? SupportsStreams)(file) == false) return null;
            try
            {
                using var stream = new FileStream(path + StreamName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                var bytes = new byte[MaximumBytes + 1];
                var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken: token);
                if (length > MaximumBytes) throw new ExtractionOriginException("origin_read_failed", UiText.Get("T_21c7bcd0de0a"));
                return new Mark(bytes[..length], streamSupport ?? SupportsStreams);
            }
            catch (FileNotFoundException) { return null; } // The archive handle above proves the base file exists.
            catch (IOException error) when (UnsupportedStreams(error)) { return null; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        { throw new ExtractionOriginException("origin_read_failed", UiText.Get("T_101b04070436")); }
    }
    private static readonly string UnsupportedWarning = UiText.Get("T_e312254287d8");
    private static bool UnsupportedStreams(IOException error) => (error.HResult & 0xffff) is 1 or 50 or 123; // INVALID_FUNCTION, NOT_SUPPORTED, INVALID_NAME
    private sealed class Mark(byte[] bytes, Func<SafeFileHandle, bool?> supportsStreams) : IExtractionOriginMark
    {
        public Task<string?> ApplyAsync(IReadOnlyList<string> stagedFiles, CancellationToken token) => Task.Run(() => ApplyCoreAsync(stagedFiles, token), token);
        private async Task<string?> ApplyCoreAsync(IReadOnlyList<string> stagedFiles, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (stagedFiles.Count == 0) return null;
            try
            {
                using var file = File.OpenHandle(stagedFiles[0], FileMode.Open, FileAccess.Read, FileShare.Read);
                if (supportsStreams(file) == false) return UnsupportedWarning;
                foreach (var path in stagedFiles)
                {
                    token.ThrowIfCancellationRequested();
                    await File.WriteAllBytesAsync(path + StreamName, bytes, token);
                }
                return null;
            }
            catch (IOException error) when (UnsupportedStreams(error)) { return UnsupportedWarning; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
            { throw new ExtractionOriginException("origin_write_failed", UiText.Get("T_e9a521da468a") ); }
        }
    }
    private static bool? SupportsStreams(SafeFileHandle handle)
    {
        // SMB and some redirectors do not implement volume queries. Actual ADS I/O determines support there.
        if (!GetVolumeInformationByHandleW(handle, 0, 0, out _, out _, out var flags, 0, 0)) return null;
        return (flags & 0x00040000) != 0; // FILE_NAMED_STREAMS, including volumes mounted below the drive root.
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle file, nint volumeName, uint volumeNameSize, out uint serial, out uint maximumComponent, out uint flags, nint fileSystem, uint fileSystemSize);
}
