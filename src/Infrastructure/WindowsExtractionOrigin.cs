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
    public async Task<IExtractionOriginMark?> ReadAsync(string archive, CancellationToken token)
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
                if (length > MaximumBytes) throw new ExtractionOriginException("origin_read_failed", "压缩包来源信息超过 64 KiB，未开始解压。请检查来源标记。");
                return new Mark(bytes[..length], streamSupport ?? SupportsStreams);
            }
            catch (FileNotFoundException) { return null; } // The archive handle above proves the base file exists.
            catch (IOException error) when (UnsupportedStreams(error)) { return null; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        { throw new ExtractionOriginException("origin_read_failed", "无法读取压缩包的来源信息，未开始解压。请检查文件权限。"); }
    }
    private const string UnsupportedWarning = "已完成解压，但目标文件系统不支持来源标记（如 FAT32/exFAT），下载来源信息未能保留。需要保留时请改用支持命名流的目录。";
    private static bool UnsupportedStreams(IOException error) => (error.HResult & 0xffff) is 1 or 50 or 123; // INVALID_FUNCTION, NOT_SUPPORTED, INVALID_NAME
    private sealed class Mark(byte[] bytes, Func<SafeFileHandle, bool?> supportsStreams) : IExtractionOriginMark
    {
        public async Task<string?> ApplyAsync(IReadOnlyList<string> stagedFiles, CancellationToken token)
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
            { throw new ExtractionOriginException("origin_write_failed", "来源信息未能写入，本次解压文件尚未交付，已有文件保持不变。请检查目标权限。" ); }
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
