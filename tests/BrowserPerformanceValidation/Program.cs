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
        BrowserIdleValidation.Run();
        ValidateSortResults();
        var samples = new List<object>();
        foreach (var count in new[] { 10000, 100000 })
        for(var attempt=1;attempt<=3;attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var entries = Enumerable.Range(0, count).Select(i => new ArchiveEntry((uint)i, $"folder-{i % 100:D3}/file-{i:D6}.txt", (ulong)(i + 1), false, false)).ToArray();
            var watch = Stopwatch.StartNew();
            var model = new ArchiveContentsViewModel("C:/synthetic/benchmark.zip", entries);
            var open = watch.Elapsed.TotalMilliseconds;
            var view = CollectionViewSource.GetDefaultView(model.VisibleRows);
            watch.Restart(); model.Search = ".txt"; var search = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); view.SortDescriptions.Add(new SortDescription(nameof(ArchiveContentRow.Path), ListSortDirection.Descending));
            var sort = watch.Elapsed.TotalMilliseconds;
            var reference=view.Cast<ArchiveContentRow>().Select(row=>row.Entry.Index).ToArray();
            var allocationBefore=GC.GetAllocatedBytesForCurrentThread();watch.Restart(); ((ListCollectionView)view).CustomSort=new ArchiveContentComparer(nameof(ArchiveContentRow.Path),ListSortDirection.Descending,System.Globalization.CultureInfo.CurrentCulture);
            var typedSort=watch.Elapsed.TotalMilliseconds;var sortAllocated=GC.GetAllocatedBytesForCurrentThread()-allocationBefore;
            if(!reference.SequenceEqual(view.Cast<ArchiveContentRow>().Select(row=>row.Entry.Index)))throw new Exception("Optimized sort changed inventory order.");
            watch.Restart(); model.Search = "file-000"; var narrow = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); model.Search = ""; model.Navigate("folder-001"); var navigate = watch.Elapsed.TotalMilliseconds;
            samples.Add(new { count, attempt, openMs = open, searchMs = search, legacySortMs = sort, sortMs = typedSort, sortAllocatedBytes = sortAllocated, narrowSearchMs = narrow, navigateMs = navigate,
                peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64,
                maxSynchronousMs = new[] {open,search,typedSort,narrow,navigate}.Max(), visible = model.VisibleRows.Count });
        }
        var result = new { runtime = Environment.Version.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            environment = "ViewModel and WPF collection view only; no desktop window or input; peak working set is cumulative for this process", samples };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
        File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
    private static void ValidateSortResults()
    {
        var entries=new[]{new ArchiveEntry(0,"a.txt",10,false,false),new ArchiveEntry(1,"A.txt",2,false,false),
            new ArchiveEntry(2,"中文.txt",10,false,false),new ArchiveEntry(3,"é.txt",1,false,false),new ArchiveEntry(4,"e.txt",1,false,false)};
        foreach(var culture in new[]{"zh-CN","en-US"})foreach(var member in new[]{nameof(ArchiveContentRow.DisplayName),"Entry.Size"})
        foreach(var direction in new[]{ListSortDirection.Ascending,ListSortDirection.Descending})
        {
            var model=new ArchiveContentsViewModel("C:/synthetic/sort.zip",entries);
            var view=(ListCollectionView)CollectionViewSource.GetDefaultView(model.VisibleRows);view.Culture=System.Globalization.CultureInfo.GetCultureInfo(culture);
            var fields=new[]{new SortDescription(member,direction),new SortDescription(nameof(ArchiveContentRow.Path),ListSortDirection.Ascending)};
            using(view.DeferRefresh())foreach(var field in fields)view.SortDescriptions.Add(field);
            var expected=view.Cast<ArchiveContentRow>().Select(row=>row.Path).ToArray();
            view.CustomSort=new ArchiveContentComparer(fields,view.Culture);
            if(!expected.SequenceEqual(view.Cast<ArchiveContentRow>().Select(row=>row.Path)))throw new Exception($"Sort semantics changed: {culture}, {member}, {direction}");
        }
    }
}
