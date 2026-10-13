using System.IO.Compression;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var data=Path.GetFullPath(Path.Combine(root,"artifacts/selection-validation",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
var checks=new Dictionary<string,bool>();void Check(string name,bool passed) {checks[name]=passed;Console.WriteLine((passed?"PASS ":"FAIL ")+name);if(!passed) throw new Exception(name);}
var fixture=Path.Combine(root,"tests/fixtures/generated");var zipPath=Path.Combine(data,"目录.zip");
using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create))
{
    foreach(var path in new[] {"folder/","folder/a.txt","folder/sub/b.txt","folder2/hidden.txt","empty/","outside.txt"})
    {var entry=zip.CreateEntry(path);if(!path.EndsWith('/')) {using var writer=new StreamWriter(entry.Open());writer.Write(path);}}
}
var vault=new DpapiPasswordVault(Path.Combine(data,"passwords.apv"));
foreach(var secret in new[] {"password","password2","测试密碼 ' $ & 空格"}) await vault.SaveAsync(new(Guid.NewGuid(),secret,secret,"",true,DateTimeOffset.UtcNow));
var versions=args.Length>1?args[1].Split(',').Distinct().ToArray():new[] {"26.04","26.03"};
foreach(var version in versions)
{
    var engine=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,$"engines/{version}/7z.dll"));
    var service=new ExtractionService(engine,vault,new PasswordCandidateProvider(vault));var listed=await engine.ListAsync(zipPath);Check(version+" real listing",listed.IsSuccess);
    var folder=listed.Items.Single(item=>item.Path.TrimEnd('/')=="folder");
    var group=ArchiveSelection.Expand(listed.Items,[folder.Index]);
    Check(version+" folder includes descendants not similarly named siblings",group.Count==3 && group.All(item=>item.Path.Replace('\\','/').StartsWith("folder/") || item.Index==folder.Index));
    var output=Path.Combine(data,version+"-folder");
    var outcome=await service.ExtractAsync(zipPath,output,true,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),selection:group);
    Check(version+" actual folder preserves paths and excludes unselected",outcome.Engine.IsSuccess && Directory.GetFiles(output,"*",SearchOption.AllDirectories).Length==2 && File.ReadAllText(Path.Combine(output,"folder/a.txt"))=="folder/a.txt" && !Directory.Exists(Path.Combine(output,"folder2")));
    Check(version+" requested callback counts only selection",outcome.Engine.SuccessfulItems==group.Count && outcome.Engine.Failures.Count==0);
    var nested=listed.Items.Single(item=>item.Path.Replace('\\','/')=="folder/sub/b.txt");var single=ArchiveSelection.Expand(listed.Items,[nested.Index]);
    var singleOut=Path.Combine(data,version+"-single");var one=await service.ExtractAsync(zipPath,singleOut,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),selection:single);
    Check(version+" single file writes only file with parent hierarchy",one.Engine.IsSuccess && Directory.GetFiles(singleOut,"*",SearchOption.AllDirectories).Length==1 && File.Exists(Path.Combine(singleOut,"folder/sub/b.txt")));
    var empty=listed.Items.Single(item=>item.Path.TrimEnd('/')=="empty");var emptyOut=Path.Combine(data,version+"-empty");
    var emptyResult=await service.ExtractAsync(zipPath,emptyOut,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),selection:[empty]);
    Check(version+" selected empty directory retained",emptyResult.Engine.IsSuccess && Directory.Exists(Path.Combine(emptyOut,"empty")) && Directory.GetFiles(emptyOut,"*",SearchOption.AllDirectories).Length==0);
    var altered=await engine.TestSelectionAsync(zipPath,[nested with {Path="different.txt"}],null,new Dictionary<uint,string>());
    Check(version+" stale path selection rejected",altered.Code=="selection_changed");
    var outOfRange=await engine.TestSelectionAsync(zipPath,[nested with {Index=uint.MaxValue}],null,new Dictionary<uint,string>());
    Check(version+" invalid index rejected",outOfRange.Code=="selection_changed");
    var duplicate=await engine.TestSelectionAsync(zipPath,[nested,nested],null,new Dictionary<uint,string>());
    Check(version+" duplicate indices rejected",duplicate.Code=="invalid_selection");
    var none=await engine.TestSelectionAsync(zipPath,[],null,new Dictionary<uint,string>());
    Check(version+" empty selection never becomes entire archive",none.Code=="invalid_selection");
    var clash=Path.Combine(data,version+"-conflict");Directory.CreateDirectory(Path.Combine(clash,"folder/sub"));File.WriteAllText(Path.Combine(clash,"folder/sub/b.txt"),"keep");
    foreach(var policy in new[] {ExistingFilePolicy.Skip,ExistingFilePolicy.Ask,ExistingFilePolicy.Replace})
    {
        var result=await service.ExtractAsync(zipPath,clash,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(policy),selection:single);
        Check(version+" selected conflict "+policy,policy==ExistingFilePolicy.Ask?result.Engine.Status=="cancelled":result.Engine.IsSuccess);
        Check(version+" selected conflict content "+policy,File.ReadAllText(Path.Combine(clash,"folder/sub/b.txt"))==(policy==ExistingFilePolicy.Replace?"folder/sub/b.txt":"keep") && Directory.GetFiles(clash,"*",SearchOption.AllDirectories).Length==1);
    }
    foreach(var name in new[] {"mixed-traditional.zip","mixed-aes.zip","test_read_format_rar5_encrypted.rar"})
    {
        var archive=Path.Combine(fixture,name);var entries=await engine.ListAsync(archive,"password");var files=entries.Items.Where(item=>!item.Directory).ToArray();
        var encryptedFiles=files.Where(item=>item.Encrypted).ToArray();
        foreach(var chosen in encryptedFiles.TakeLast(1).Concat(encryptedFiles.Take(1)))
        {
            var before=File.ReadAllBytes(Path.Combine(data,"passwords.apv"));var prompts=0;
            var destination=Path.Combine(data,version+"-"+name+"-"+chosen.Index);
            var savedName=version+" selected "+name+chosen.Index;
            var selectedResult=await service.ExtractAsync(archive,destination,false,_=>Task.FromResult<PasswordPromptResponse?>(prompts++==0?new(chosen==files.Last()?"password2":"password",true,savedName):null),_=>Task.FromResult(ExistingFilePolicy.Replace),selection:[chosen]);
            if(!selectedResult.Engine.IsSuccess || prompts!=1 || Directory.GetFiles(destination,"*",SearchOption.AllDirectories).Length!=1) Console.WriteLine(JsonSerializer.Serialize(new {engine=selectedResult.Engine,prompts,chosen,files=Directory.GetFiles(destination,"*",SearchOption.AllDirectories)}));
            Check(version+" mixed single password sufficient "+name+chosen.Index,selectedResult.Engine.IsSuccess && prompts==1 && Directory.GetFiles(destination,"*",SearchOption.AllDirectories).Length==1);
            Check(version+" only chosen encrypted evidence "+name+chosen.Index,selectedResult.Engine.VerifiedEncryptedItems.SequenceEqual(new[] {chosen.Index}));
            Check(version+" selected password saved only after actual success "+name+chosen.Index,(await vault.ReadAsync()).Any(record=>record.Name==savedName && record.LastSuccessAt is not null));
        }
        var pair=ArchiveSelection.Expand(entries.Items,[encryptedFiles.First().Index,encryptedFiles.Last().Index]);var attempts=0;
        var pairResult=await service.ExtractAsync(archive,Path.Combine(data,version+"-pair-"+name),false,_=>Task.FromResult<PasswordPromptResponse?>(new(attempts++==0?"password":"password2",false,"")),_=>Task.FromResult(ExistingFilePolicy.Replace),selection:pair);
        Check(version+" mixed selected pair combines correctly "+name,pairResult.Engine.IsSuccess && attempts==2 && Directory.GetFiles(pairResult.OutputPath,"*",SearchOption.AllDirectories).Length==2);
        var cancelBefore=File.ReadAllBytes(Path.Combine(data,"passwords.apv"));var cancelAttempts=0;var cancelPath=Path.Combine(data,version+"-cancel-"+name);
        var partial=await service.ExtractAsync(archive,cancelPath,false,_=>Task.FromResult<PasswordPromptResponse?>(cancelAttempts++==0?new("password",true,"never save partial"):null),_=>Task.FromResult(ExistingFilePolicy.Replace),selection:pair);
        Check(version+" partial cancel writes no selected files or passwords "+name,partial.Engine.Status=="cancelled" && Directory.GetFiles(cancelPath,"*",SearchOption.AllDirectories).Length==0 && cancelBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(data,"passwords.apv"))));
    }
    var solid=Path.Combine(data,version+"-solid.7z");var source1=Path.Combine(data,"solid-a.txt");var source2=Path.Combine(data,"solid-b.txt");File.WriteAllText(source1,new string('a',1024*1024));File.WriteAllText(source2,"second only");
    var created=await engine.CreateAsync(new([new(source1,"a.txt",false),new(source2,"b.txt",false)],"7z",5,true),solid,"solid-password");Check(version+" encrypted 7z creation",created.IsSuccess);
    var solidList=await engine.ListAsync(solid,"solid-password");var b=solidList.Items.Single(item=>item.Path=="b.txt");
    var solidResult=await service.ExtractAsync(solid,Path.Combine(data,version+"-solid-out"),false,_=>Task.FromResult<PasswordPromptResponse?>(new("solid-password",false,"")),_=>Task.FromResult(ExistingFilePolicy.Replace),selection:[b]);
    Check(version+" encrypted solid selection excludes prior block file",solidResult.Engine.IsSuccess && solidResult.Engine.VerifiedEncryptedItems.SequenceEqual(new[] {b.Index}) && Directory.GetFiles(solidResult.OutputPath).Length==1 && File.ReadAllText(Path.Combine(solidResult.OutputPath,"b.txt"))=="second only");
    using var cancel=new CancellationTokenSource();var cancelled=false;var large=Path.Combine(fixture,"large.7z");var largeList=await engine.ListAsync(large);var stopOut=Path.Combine(data,version+"-large-cancel");
    var stopped=await service.ExtractAsync(large,stopOut,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),new ForwardProgress<TaskUpdate>(update=>{if(update.Progress is not null && !cancelled) {cancelled=true;cancel.Cancel();}}),cancel.Token,selection:largeList.Items);
    Check(version+" actual selected cancellation no committed output",cancelled && stopped.Engine.Status=="cancelled" && Directory.GetFiles(stopOut,"*",SearchOption.AllDirectories).Length==0);
}
var oldWorker=Path.Combine(root,"artifacts/extraction-tools-candidate/ArchiveEngineWorker.exe");
if(File.Exists(oldWorker))
{
    var legacy=new ArchiveEngineClient(oldWorker,Path.Combine(root,"engines/26.04/7z.dll"));var oldList=await legacy.ListAsync(zipPath);
    var oldResult=await legacy.ExtractSelectionAsync(zipPath,Path.Combine(data,"old-worker"),oldList.Items.Take(1).ToArray(),null,new Dictionary<uint,string>());
    Check("old worker refuses selection rather than extracts everything",!oldResult.IsSuccess && !Directory.Exists(Path.Combine(data,"old-worker")));
}
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/selection-validation.json"),JsonSerializer.Serialize(new {passed=checks.Values.All(value=>value),total=checks.Count,checks,versions}));
return 0;
