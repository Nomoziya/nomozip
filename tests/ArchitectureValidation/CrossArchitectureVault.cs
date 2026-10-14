using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

internal static class CrossArchitectureVault
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var root=Path.GetFullPath(args[0]);
        var folder=Path.Combine(root,"artifacts/cross-architecture-vault");
        const string secret="synthetic 跨架构 password '$ &";
        const string master="synthetic ARM migration master 2026";
        var expected=args[1]=="import-vault-arm64"?"arm64":"x64";
        var checks=new Dictionary<string,bool>();
        checks["actual target process on ARM64 Windows"]=EngineArchitecture.Current==expected && RuntimeInformation.OSArchitecture==Architecture.Arm64;
        if(!checks.Values.All(value=>value)) throw new InvalidOperationException("Cross-architecture acceptance requires a native ARM64 Windows machine.");
        if(args[1]=="export-vault-x64")
        {
            if(Directory.Exists(folder)) throw new InvalidOperationException("Synthetic vault exports stay immutable.");
            Directory.CreateDirectory(folder);
            foreach(var kind in new[]{"dpapi","master"})
            {
                var vault=new DpapiPasswordVault(Path.Combine(folder,kind+".apv"));
                await vault.SaveAsync(new SavedPassword(Guid.NewGuid(),"synthetic "+kind,secret,"architecture fixture",true,DateTimeOffset.UtcNow));
                if(kind=="master") await vault.EnableMasterPasswordAsync(master);
                checks[kind+" exported by actual x64 process"]=File.Exists(Path.Combine(folder,kind+".apv"));
            }
        }
        else if(args[1] is "import-vault-arm64" or "verify-vault-x64")
        {
            foreach(var kind in new[]{"dpapi","master"})
            {
                var path=Path.Combine(folder,kind+".apv");var before=await File.ReadAllBytesAsync(path);
                var vault=new DpapiPasswordVault(path);
                if(kind=="master") await vault.UnlockAsync(master);
                var record=(await vault.ReadAsync()).Single();
                checks[kind+" reads previous architecture secret and metadata"]=record.Secret==secret && record.AutoTry && record.Notes=="architecture fixture";
                var afterRead=await File.ReadAllBytesAsync(path);
                checks[kind+" reading preserves encrypted file"]=before.SequenceEqual(afterRead);
                if(args[1]=="import-vault-arm64")
                {
                    await vault.SaveAsync(record with{Name="native ARM64 preserved "+kind});
                    checks[kind+" native save keeps secret"]= (await vault.ReadAsync()).Single().Secret==secret;
                }
                else checks[kind+" x64 rereads ARM64 edit"]=record.Name=="native ARM64 preserved "+kind;
            }
        }
        else throw new ArgumentException("Unknown vault validation mode.");
        var result=new{passed=checks.Values.All(value=>value),total=checks.Count,checks,architecture=EngineArchitecture.Current,osArchitecture=RuntimeInformation.OSArchitecture.ToString(),syntheticOnly=true};
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/"+args[1]+"-validation.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(result));return result.passed?0:1;
    }
}
