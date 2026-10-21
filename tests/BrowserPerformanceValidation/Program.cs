using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using ArchivePassword.Core;
using ArchivePassword.Desktop;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var samples = new List<object>();
        foreach (var count in new[] { 10000, 100000 })
        for(var attempt=1;attempt<=3;attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var memory = GC.GetTotalMemory(true);
            var entries = Enumerable.Range(0, count).Select(i => new ArchiveEntry((uint)i, $"folder-{i % 100:D3}/file-{i:D6}.txt", (ulong)(i + 1), false, false)).ToArray();
            var watch = Stopwatch.StartNew();
            var model = new ArchiveContentsViewModel("C:/synthetic/benchmark.zip", entries);
            var open = watch.Elapsed.TotalMilliseconds;
            var view = CollectionViewSource.GetDefaultView(model.VisibleRows);
            watch.Restart(); model.Search = ".txt"; var search = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); view.SortDescriptions.Add(new SortDescription(nameof(ArchiveContentRow.Path), ListSortDirection.Descending));
            var sort = watch.Elapsed.TotalMilliseconds;
            var reference=view.Cast<ArchiveContentRow>().Select(row=>row.Entry.Index).ToArray();
            watch.Restart(); ((ListCollectionView)view).CustomSort=new ArchiveContentComparer(nameof(ArchiveContentRow.Path),ListSortDirection.Descending,System.Globalization.CultureInfo.CurrentCulture);
            var typedSort=watch.Elapsed.TotalMilliseconds;
            if(!reference.SequenceEqual(view.Cast<ArchiveContentRow>().Select(row=>row.Entry.Index)))throw new Exception("Optimized sort changed inventory order.");
            watch.Restart(); model.Search = "file-000"; var narrow = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); model.Search = ""; model.Navigate("folder-001"); var navigate = watch.Elapsed.TotalMilliseconds;
            samples.Add(new { count, attempt, openMs = open, searchMs = search, legacySortMs = sort, sortMs = typedSort, narrowSearchMs = narrow, navigateMs = navigate,
                retainedBytes = GC.GetTotalMemory(true) - memory, peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64,
                maxSynchronousMs = new[] {open,search,typedSort,narrow,navigate}.Max(), visible = model.VisibleRows.Count });
        }
        var result = new { runtime = Environment.Version.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            environment = "ViewModel and WPF collection view only; no desktop window or input", samples };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
