using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var own=Path.Combine(root,"artifacts/mv-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(own);
var checks=new Dictionary<string,bool>();void Check(string name,bool value){checks.Add(name,value);Console.WriteLine($"{(value?"PASS":"FAIL")} {name}");if(!value) throw new Exception(name);}
async Task<bool> Reject(Func<Task> action){try{await action();return false;}catch(Exception e) when(e is VaultException or VaultTransferException or IOException or OperationCanceledException){return true;}}
var password="合成主密码 $ ' 2026 one";var next="另一个合成主密码 2026 two";var path=Path.Combine(own,"passwords.apv");var backup=path+".bak";
var record=new SavedPassword(Guid.NewGuid(),"synthetic","测试密碼 ' $ & 空格","synthetic note",true,DateTimeOffset.UtcNow,null,"group","source","header");var archive=Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z");
var vault=new DpapiPasswordVault(path);
try
{
 Check("new vault protection disabled",await vault.GetProtectionStateAsync()==VaultProtectionState.Disabled);
 await vault.SaveAsync(record);await vault.RecordSuccessAsync(record.Id,archive);var original=await File.ReadAllBytesAsync(path);var originalBackup=await File.ReadAllBytesAsync(backup);
 Check("legacy APV1 unchanged before enabling",Encoding.ASCII.GetString(original,0,4)=="APV1" && (await new DpapiPasswordVault(path).ReadAsync()).Count==1);
 Check("short master rejected",await Reject(()=>vault.EnableMasterPasswordAsync("short")) && File.ReadAllBytes(path).SequenceEqual(original));
 using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Check("cancelled enable preserves both originals",await Reject(()=>vault.EnableMasterPasswordAsync(password,cancelled.Token)) && File.ReadAllBytes(path).SequenceEqual(original) && File.ReadAllBytes(backup).SequenceEqual(originalBackup));}
 using(var lockedFile=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){Check("actual write failure preserves primary and backup",await Reject(()=>vault.EnableMasterPasswordAsync(password)) && File.ReadAllBytes(path).SequenceEqual(original) && File.ReadAllBytes(backup).SequenceEqual(originalBackup));}
 Check("write failure removes staged protection files",!Directory.GetFiles(own,"*.protect-*").Any());
 await vault.EnableMasterPasswordAsync(password);
 Check("enable protects primary and recovery file",new[]{path,backup}.All(p=>Encoding.ASCII.GetString(File.ReadAllBytes(p),0,4)=="APV2"));
 Check("enable preserves records metadata and archive association",(await vault.ReadAsync()).Single() is var current && current.Secret==record.Secret && current.Group==record.Group && current.Source==record.Source && await vault.GetAssociatedPasswordAsync(archive)==record.Id);
 Check("enabled session unlocked",await vault.GetProtectionStateAsync()==VaultProtectionState.Unlocked);
 var fresh=new DpapiPasswordVault(path);Check("fresh instance locked without settings",await fresh.GetProtectionStateAsync()==VaultProtectionState.Locked);
 Check("locked direct read rejected",await Reject(async()=>{await fresh.ReadAsync();}));
 Check("locked candidate and association rejected",await Reject(async()=>{await new PasswordCandidateProvider(fresh).GetAsync(archive);}) && await Reject(async()=>{await fresh.GetAssociatedPasswordAsync(archive);}));
 Check("locked save delete success restore import merge rejected",await Reject(()=>fresh.SaveAsync(record)) && await Reject(()=>fresh.DeleteAsync(record.Id)) && await Reject(()=>fresh.RecordSuccessAsync(record.Id,archive)) && await Reject(()=>fresh.RestoreBackupAsync()) && await Reject(async()=>{await fresh.ImportAsync([record],PasswordImportMode.SkipDuplicates);}) && await Reject(async()=>{await fresh.MergeDuplicatesAsync(record.Id,[]);}));
 var protectedBytes=File.ReadAllBytes(path);Check("wrong unlock retains cipher and lock",await Reject(()=>fresh.UnlockAsync("wrong password")) && File.ReadAllBytes(path).SequenceEqual(protectedBytes) && await fresh.GetProtectionStateAsync()==VaultProtectionState.Locked);
 Check("Windows user DPAPI alone cannot decrypt APV2",RejectDpapi(protectedBytes));
 await fresh.UnlockAsync(password);Check("correct unlock restores candidate",(await new PasswordCandidateProvider(fresh).GetAsync(archive)).Single().Secret==record.Secret);
 var engine=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines/26.04/7z.dll"));var output=Path.Combine(own,"real-output");
 var service=new ExtractionService(engine,fresh,new PasswordCandidateProvider(fresh));var extracted=await service.ExtractAsync(archive,output,true,_=>throw new Exception("Unexpected prompt"),_=>Task.FromResult(ExistingFilePolicy.Replace));
 Check("unlocked real engine automatic extraction",extracted.Engine.IsSuccess && File.ReadAllBytes(Path.Combine(output,"中文示例.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"tests/fixtures/generated/中文示例.txt"))));
 var beforeSave=File.ReadAllBytes(path);await fresh.SaveAsync(record with{Name="changed"});var afterSave=File.ReadAllBytes(path);
 Check("ordinary writes use fresh nonce and remain encrypted",!beforeSave.AsSpan(28,12).SequenceEqual(afterSave.AsSpan(28,12)) && Encoding.ASCII.GetString(File.ReadAllBytes(backup),0,4)=="APV2");
 await fresh.RestoreBackupAsync();Check("protected ordinary backup actually restores previous records",(await fresh.ReadAsync()).Single().Name==record.Name);
 Check("wrong change and disable preserve encrypted files",await Reject(()=>fresh.ChangeMasterPasswordAsync("wrong",next)) && await Reject(()=>fresh.DisableMasterPasswordAsync("wrong")) && await fresh.GetProtectionStateAsync()==VaultProtectionState.Unlocked);
 await fresh.ChangeMasterPasswordAsync(password,next);await fresh.LockAsync();
 Check("old master rejected after change",await Reject(()=>fresh.UnlockAsync(password)));
 await fresh.UnlockAsync(next);Check("new master reads both primary and backup",(await fresh.ReadAsync()).Count==1);await fresh.RestoreBackupAsync();
 Check("old master also cannot unlock new recovery copy",await Reject(()=>new DpapiPasswordVault(backup).UnlockAsync(password)));
 var good=File.ReadAllBytes(path);var bad=good.ToArray();bad[^1]^=1;File.WriteAllBytes(path,bad);Check("cipher alteration rejected",await Reject(()=>new DpapiPasswordVault(path).UnlockAsync(next)));File.WriteAllBytes(path,good);
 bad=good.ToArray();bad[8]^=1;File.WriteAllBytes(path,bad);Check("KDF alteration rejected before derivation",await Reject(()=>new DpapiPasswordVault(path).UnlockAsync(next)));File.WriteAllBytes(path,good);
 bad=good.ToArray();bad[^1]^=1;File.WriteAllBytes(path,bad);var damagedFresh=new DpapiPasswordVault(path);
 Check("fresh protected backup restore requires correct master",await Reject(()=>damagedFresh.RestoreMasterBackupAsync("wrong")) && File.ReadAllBytes(path).SequenceEqual(bad));
 await damagedFresh.RestoreMasterBackupAsync(next);Check("fresh protected backup repairs damaged primary",(await damagedFresh.ReadAsync()).Count==1 && File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(backup)));await damagedFresh.LockAsync();

 File.WriteAllBytes(backup,originalBackup);Check("legacy backup cannot bypass protected primary",await Reject(()=>fresh.RestoreBackupAsync()));File.WriteAllBytes(backup,good);
 await fresh.DisableMasterPasswordAsync(next);Check("authorized disable restores DPAPI primary and backup",new[]{path,backup}.All(p=>Encoding.ASCII.GetString(File.ReadAllBytes(p),0,4)=="APV1") && (await new DpapiPasswordVault(path).ReadAsync()).Count==1);
 await fresh.EnableMasterPasswordAsync(password);await fresh.LockAsync();
 var portable=Path.Combine(own,"portable.nvault");await EncryptedVaultBackup.ExportAsync(portable,[record],"independent synthetic backup 2026");
 Check("incorrect portable backup never yields recovery records",await Reject(async()=>{await EncryptedVaultBackup.ReadAsync(portable,"wrong");}));
 var incoming=await EncryptedVaultBackup.ReadAsync(portable,"independent synthetic backup 2026");var forgotten=File.ReadAllBytes(path);var preserved=await fresh.RecoverFromBackupAsync(incoming,next);
 Check("portable recovery preserves original encrypted file",File.ReadAllBytes(preserved).SequenceEqual(forgotten) && File.Exists(preserved+".bak"));
 Check("recovery creates new master protected data without old master",(await fresh.ReadAsync()).Single().Secret==record.Secret && await fresh.GetAssociatedPasswordAsync(archive) is null);
 await fresh.LockAsync();await fresh.UnlockAsync(next);Check("recovered vault reopens with new master",(await fresh.ReadAsync()).Count==1);
 Check("forgotten original remains locked without its own master",await Reject(()=>new DpapiPasswordVault(preserved).UnlockAsync(next)));
 await fresh.LockAsync();Check("explicit lock prevents reads again",await Reject(async()=>{await fresh.ReadAsync();}));
 Check("no staged plaintext or protection files remain",!Directory.GetFiles(own,"*.protect-*").Any() && !Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(record.Secret));
}
catch(Exception error){checks["exception "+error.GetType().Name+": "+error.Message]=false;}
finally{await vault.LockAsync();}
var result=new{passed=checks.Values.All(v=>v),total=checks.Count,checks,fixtureDirectory=own,syntheticOnly=true};await File.WriteAllTextAsync(Path.Combine(root,"artifacts/master-vault-validation.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return result.passed?0:1;
static bool RejectDpapi(byte[] bytes){try{var clear=ProtectedData.Unprotect(bytes.AsSpan(4).ToArray(),null,DataProtectionScope.CurrentUser);CryptographicOperations.ZeroMemory(clear);return false;}catch(CryptographicException){return true;}}
