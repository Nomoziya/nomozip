using ArchivePassword.Infrastructure;
using System.Text.Json;
using System.Text;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
if (args.Length is not (4 or 5)) { Console.Error.WriteLine("Usage: EngineSmoke worker dll archive output [cancel]"); return 2; }
var engine = new ArchiveEngineClient(args[0], args[1]);
var capabilities = await engine.GetCapabilitiesAsync();
if (!capabilities.IsSuccess || !capabilities.Formats.Any(value => value.Name == "7z")) return 1;
if (args.Length == 5 && args[4] == "cancel")
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var cancelled = await engine.TestAsync(args[2], progress: new CancelOnProgress(cancellation), cancellationToken: cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(cancelled));
    return cancelled.Status == "cancelled" ? 0 : 1;
}
var password = await Console.In.ReadLineAsync();
var tested = await engine.TestAsync(args[2], password);
var extracted = tested.IsSuccess ? await engine.ExtractAsync(args[2], args[3], password) : tested;
Console.WriteLine(JsonSerializer.Serialize(new { capabilities = capabilities.Formats.Count, tested, extracted }));
return extracted.IsSuccess ? 0 : 1;

sealed class CancelOnProgress(CancellationTokenSource cancellation) : IProgress<ArchivePassword.Core.EngineProgress>
{
    public void Report(ArchivePassword.Core.EngineProgress value) => cancellation.Cancel();
}
