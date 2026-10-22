using System.IO;
using System.Reflection;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Desktop;
using ArchivePassword.Infrastructure;
internal static class Program {
 [STAThread] static void Main(){
  var data=Path.GetFullPath("artifacts/next-usability-probes/data");Directory.CreateDirectory(data);
  var model=new MainViewModel(Path.GetFullPath("."),data,new DpapiPasswordVault(Path.Combine(data,"synthetic.apv")),null!);
  model.CompressionFormat=1;var input=Path.Combine(data,"missing.txt");model.CompressionSources.Add(new(input));model.CompressionOutput=Path.Combine(data,"output.zip");model.CompressionExcludedExtensions="tmp";
  var method=typeof(MainViewModel).GetMethod("CaptureCompressionRetry",BindingFlags.Instance|BindingFlags.NonPublic)!;
  var snapshot=method.Invoke(model,new object[]{new[]{input},model.CompressionOutput});
  var row=new ArchiveTaskRow(model.CompressionOutput){IsCompression=true,Failure=new EngineResult{Code="io_error"}};
  typeof(ArchiveTaskRow).GetProperty("CompressionRetry",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(row,snapshot);
  model.WorkspaceTasks.Add(row);model.SelectedTask=row;model.Page=0;
  model.EditCompressionRetryCommand.Execute(null);
  Console.WriteLine(JsonSerializer.Serialize(new {model.Page,sourceCount=model.CompressionSources.Count,sourceMatches=model.CompressionSources.FirstOrDefault()?.Path==input,model.CompressionFormat,model.IsBusy,model.CompressionRunning,profile=snapshot!.GetType().GetProperty("Profile")!.GetValue(snapshot)}));
 }
}