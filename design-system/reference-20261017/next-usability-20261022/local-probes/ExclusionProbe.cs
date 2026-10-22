using System.Diagnostics;
using System.Text.Json;
using ArchivePassword.Core;
CompressionExclusions.TryCreate("cache;bin;临时目录","tmp;tar.gz",out var rules);
var directories=new HashSet<string>(["cache","bin","临时目录"],StringComparer.OrdinalIgnoreCase);string[] extensions=[".tmp",".tar.gz"];
bool Previous(string path,bool directory){var name=path.Replace('\\','/').TrimEnd('/').Split('/')[^1];return directory?directories.Contains(name):extensions.Any(extension=>name.EndsWith(extension,StringComparison.OrdinalIgnoreCase));}
bool Proposed(string path,bool directory){var trimmed=path.AsSpan().TrimEnd("/\\");var name=trimmed[(trimmed.LastIndexOfAny('/','\\')+1)..];if(directory)return directories.GetAlternateLookup<ReadOnlySpan<char>>().Contains(name);foreach(var extension in extensions)if(name.EndsWith(extension,StringComparison.OrdinalIgnoreCase))return true;return false;}
var cases=new[]{"", "cache", "root/CACHE/", "root\\bin\\", "a/b/notcache", "a/临时目录", "a/file.TMP", "a/file.tar.GZ", "filetmp", "a/keep.txt", "a\\b/file.tmp"};
foreach(var path in cases)foreach(var directory in new[]{true,false})if(Previous(path,directory)!=Proposed(path,directory) || Previous(path,directory)!=rules.Excludes(path,directory))throw new Exception("semantic difference");
var paths=Enumerable.Range(0,100000).Select(i=>$"root/project/deep/contents/level-{i%30}/"+(i%5==0?"CACHE":i%3==0?$"file-{i}.TMP":$"file-{i}.txt")).ToArray();
object Measure(Func<string,bool,bool> predicate){for(var i=0;i<20000;i++)predicate(paths[i],i%5==0);var bytes=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();var matches=0;for(var round=0;round<5;round++)for(var i=0;i<paths.Length;i++)if(predicate(paths[i],i%5==0))matches++;watch.Stop();return new{milliseconds=watch.Elapsed.TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-bytes,matches,calls=500000};}
Console.WriteLine(JsonSerializer.Serialize(new{runtime=Environment.Version.ToString(),semanticCases=cases.Length*2,before=Measure(Previous),proposed=Measure(Proposed),current=Measure(rules.Excludes)}));
