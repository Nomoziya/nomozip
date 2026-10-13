using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed class ArchiveEngineClient(string workerPath, string enginePath, int filenameCodePage = 0) : IArchiveEngine, IArchiveCreator, IArchiveMultiPasswordEngine,IArchiveSelectionEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<EngineResult> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => RunAsync("capabilities", null, null, null, null, cancellationToken);
    public Task<EngineResult> ListAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default) => RunAsync("list", archivePath, null, password, null, cancellationToken);
    public Task<EngineResult> TestAsync(string archivePath, string? password = null, IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default) => RunAsync("test", archivePath, null, password, progress, cancellationToken);
    public Task<EngineResult> ExtractAsync(string archivePath, string outputPath, string? password = null, IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default) => RunAsync("extract", archivePath, outputPath, password, progress, cancellationToken);
    public Task<EngineResult> TestWithPasswordsAsync(string archivePath, string? password, IReadOnlyDictionary<uint, string> itemPasswords, IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default) => RunAsync("test", archivePath, null, password, progress, cancellationToken, itemPasswords: itemPasswords);
    public Task<EngineResult> ExtractWithPasswordsAsync(string archivePath, string outputPath, string? password, IReadOnlyDictionary<uint, string> itemPasswords, IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default) => RunAsync("extract", archivePath, outputPath, password, progress, cancellationToken, itemPasswords: itemPasswords);
    public Task<EngineResult> CreateAsync(CompressionRequest request, string outputPath, string? password,
        IProgress<EngineProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync("create", null, outputPath, password, progress, cancellationToken, request);

    public Task<EngineResult> TestSelectionAsync(string archive,IReadOnlyList<ArchiveEntry> selection,string? password,IReadOnlyDictionary<uint,string> itemPasswords,IProgress<EngineProgress>? progress=null,CancellationToken token=default)
        =>RunAsync("test-selection",archive,null,password,progress,token,itemPasswords:itemPasswords,selection:selection);
    public Task<EngineResult> ExtractSelectionAsync(string archive,string output,IReadOnlyList<ArchiveEntry> selection,string? password,IReadOnlyDictionary<uint,string> itemPasswords,IProgress<EngineProgress>? progress=null,CancellationToken token=default)
        =>RunAsync("extract-selection",archive,output,password,progress,token,itemPasswords:itemPasswords,selection:selection);
    private async Task<EngineResult> RunAsync(string operation, string? archivePath, string? outputPath, string? password, IProgress<EngineProgress>? progress, CancellationToken token, CompressionRequest? creation = null, IReadOnlyDictionary<uint, string>? itemPasswords = null,IReadOnlyList<ArchiveEntry>? selection=null)
    {
        if (!ZipFilenameEncoding.IsSupported(filenameCodePage)) throw new ArgumentOutOfRangeException(nameof(filenameCodePage));
        if (token.IsCancellationRequested) return new EngineResult { Protocol = 1, Status = "cancelled", Code = "cancelled" };
        // Separate operations make older workers reject an override instead of silently ignoring it.
        if (filenameCodePage != 0 && operation is not ("create" or "capabilities")) operation += "-encoded";
        var start = new ProcessStartInfo(Path.GetFullPath(workerPath))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(Path.GetFullPath(enginePath));
        using var process = new Process { StartInfo = start };
        var started = false;
        try
        {
            process.Start();
            started = true;
            var diagnosticDrain = process.StandardError.ReadToEndAsync();
            // Sensitive request data stays in the parent/child pipe and is never logged.
            await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, new { protocol = 1, operation, archivePath, outputPath, password, filenameCodePage,
                itemPasswords = itemPasswords?.Select(value => new { index = value.Key, password = value.Value }),selection,
                sourceEntries = creation?.Entries, format = creation?.Format, compressionLevel = creation?.Level, encryptHeaders = creation?.EncryptHeaders }, JsonOptions, token);
            await process.StandardInput.BaseStream.WriteAsync("\n"u8.ToArray(), token);
            await process.StandardInput.BaseStream.FlushAsync(token);
            var response = ReadResultAsync(process, progress);
            EngineResult result;
            try { result = await response.WaitAsync(token); }
            catch (OperationCanceledException)
            {
                await process.StandardInput.WriteLineAsync("{\"protocol\":1,\"operation\":\"cancel\"}");
                await process.StandardInput.FlushAsync();
                try { await response.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                result = new EngineResult { Protocol = 1, Status = "cancelled", Code = "cancelled" };
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(CancellationToken.None);
            await diagnosticDrain;
            return token.IsCancellationRequested ? new EngineResult { Protocol = 1, Status = "cancelled", Code = "cancelled" } : result;
        }
        catch (OperationCanceledException) { return new EngineResult { Protocol = 1, Status = "cancelled", Code = "cancelled" }; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        {
            if (token.IsCancellationRequested) return new EngineResult { Protocol = 1, Status = "cancelled", Code = "cancelled" };
            return new EngineResult { Protocol = 1, Code = "worker_connection_failed" };
        }
        finally { if (started && !process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private static async Task<EngineResult> ReadResultAsync(Process process, IProgress<EngineProgress>? progress)
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            using var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            if (!root.TryGetProperty("protocol", out var protocol) || protocol.GetInt32() != 1) return new EngineResult { Code = "protocol_mismatch" };
            if (root.GetProperty("kind").GetString() == "progress")
                progress?.Report(new EngineProgress(root.GetProperty("completed").GetUInt64(), root.GetProperty("total").GetUInt64()));
            else if (root.GetProperty("kind").GetString() == "result")
                return root.Deserialize<EngineResult>(JsonOptions) ?? new EngineResult { Code = "invalid_response" };
        }
        return new EngineResult { Code = "worker_exited_without_result" };
    }
}
