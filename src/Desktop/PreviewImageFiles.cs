using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ArchivePassword.Desktop;

// External viewers may load asynchronously. Keep explicitly opened copies until
// application exit, rather than deleting them when the preview pane closes.
internal sealed class PreviewImageFiles(string root,Action<ProcessStartInfo>? launch=null) : IDisposable
{
    private static readonly Lazy<PreviewImageFiles> current=new(()=>new(Path.Combine(Path.GetTempPath(),"nomozip-preview",Guid.NewGuid().ToString("N"))));
    internal static PreviewImageFiles Current=>current.Value;
    internal static void CleanupCurrent() {if(current.IsValueCreated)current.Value.Dispose();}
    private readonly List<string> files=[];
    internal async Task<string> OpenAsync(string name,byte[] bytes)
    {
        var extension=Path.GetExtension(name).ToLowerInvariant();
        if(extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"))throw new InvalidOperationException();
        var directory=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,Path.GetFileName(name));files.Add(path);
        await File.WriteAllBytesAsync(path,bytes);File.SetAttributes(path,FileAttributes.ReadOnly);
        var info=new ProcessStartInfo {FileName=path,UseShellExecute=true,Verb="open"};
        if(launch is null)Process.Start(info)?.Dispose();else launch(info);
        return path;
    }
    public void Dispose()
    {
        foreach(var path in files.ToArray())
        {
            try {if(File.Exists(path)){File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}Directory.Delete(Path.GetDirectoryName(path)!);files.Remove(path);}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException) { /* A viewer can still own the copy. Leave it in the OS temporary directory. */ }
        }
        if(Directory.Exists(root))try {Directory.Delete(root);}catch(Exception error) when(error is IOException or UnauthorizedAccessException){}
    }
}
