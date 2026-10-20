using System.Diagnostics;
using System.Security.Cryptography;

namespace ArchivePassword.Infrastructure;

public sealed record DownloadProgress(long Bytes, long TotalBytes, double BytesPerSecond, bool Stalled)
{
    public int Percent => TotalBytes <= 0 ? 0 : (int)Math.Min(100, Bytes * 100 / TotalBytes);
    public string Description => UiText.Format(Stalled ? "Download.Stalled" : "Download.Progress",
        Bytes / 1048576.0, TotalBytes / 1048576.0, BytesPerSecond / 1024.0);
}

// The heartbeat continues during a pending read, so a stalled network is observable.
public static class DownloadTransfer
{
    public static async Task<string> CopyAsync(Stream input, Stream output, long expectedBytes, long maximumBytes,
        IProgress<DownloadProgress>? progress, CancellationToken token, TimeSpan? stallAfter = null)
    {
        var clock = Stopwatch.StartNew();
        long total = 0, previous = 0;
        var lastData = TimeSpan.Zero;
        var lastSample = TimeSpan.Zero;
        var stall = stallAfter ?? TimeSpan.FromSeconds(5);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        progress?.Report(new(0, expectedBytes, 0, false));
        while (true)
        {
            var read = input.ReadAsync(buffer, token).AsTask();
            using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(token);
            try
            {
                while (!read.IsCompleted)
                {
                    var tick = Task.Delay(TimeSpan.FromMilliseconds(500), heartbeat.Token);
                    if (await Task.WhenAny(read, tick).ConfigureAwait(false) == read) break;
                    await tick.ConfigureAwait(false);
                    Report();
                }
                var count = await read.ConfigureAwait(false);
                if (count == 0) break;
                total += count;
                if (total > expectedBytes || total > maximumBytes) throw new InvalidDataException("Download exceeds declared size.");
                lastData = clock.Elapsed;
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                if (clock.Elapsed - lastSample >= TimeSpan.FromMilliseconds(250)) Report();
            }
            finally { heartbeat.Cancel(); }
        }
        token.ThrowIfCancellationRequested();
        if (total != expectedBytes) throw new InvalidDataException("Download ended before its declared size.");
        Report(completed: true);
        return Convert.ToHexString(hash.GetHashAndReset());

        void Report(bool completed = false)
        {
            var now = clock.Elapsed;
            var speed = (total - previous) / Math.Max(0.001, (now - lastSample).TotalSeconds);
            progress?.Report(new(total, expectedBytes, speed, !completed && now - lastData >= stall));
            previous = total; lastSample = now;
        }
    }
}
