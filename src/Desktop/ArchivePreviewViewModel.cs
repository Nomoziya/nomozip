using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed class ArchivePreviewViewModel : ObservableObject
{
    public string EntryPath {get;}
    public string EntryName=>Path.GetFileName(EntryPath);
    public string Text {get;private set;}="";
    public BitmapSource? Picture {get;private set;}
    public string Details {get;private set;}="";
    public string PixelDimensions=>Picture is null?"":$"{Picture.PixelWidth} × {Picture.PixelHeight}";
    public long Bytes {get;private set;}
    public string SizeText=>Bytes>=1048576?$"{Bytes/1048576.0:0.#} MB":Bytes>=1024?$"{Bytes/1024.0:0.#} KB":$"{Bytes} B";
    public string Message {get;private set;}="";
    private byte[]? imageBytes;
    private bool cleared;
    private string openMessage="";
    public string StatusText=>openMessage.Length>0?openMessage:UiText.Get(HasError?"Preview.Notice":"Preview.Loaded");
    public string ModifiedText {get;private set;}="—";
    public System.Windows.Input.ICommand OpenImageCommand {get;}
    internal Func<string,byte[],Task<string>> ImageOpener {get;set;}=(name,bytes)=>PreviewImageFiles.Current.OpenAsync(name,bytes);
    public bool IsText=>Picture is null && Message.Length==0;
    public bool IsImage=>Picture is not null;
    public bool HasError=>Message.Length>0;
    private ArchivePreviewViewModel(string path)
    {
        EntryPath=path;
        OpenImageCommand=new AsyncCommand(async()=>
        {
            var copy=imageBytes!.ToArray();
            try {await ImageOpener(EntryName,copy);if(!cleared){openMessage=UiText.Get("Preview.Opened");Changed(nameof(StatusText));}}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {if(!cleared){openMessage=UiText.Get("Preview.OpenFailed");Changed(nameof(StatusText));}}
            finally {System.Security.Cryptography.CryptographicOperations.ZeroMemory(copy);}
        },()=>!cleared && IsImage && imageBytes is not null);
    }
    public static async Task<ArchivePreviewViewModel> CreateAsync(ArchiveEntry entry,byte[] bytes,CancellationToken token=default)
        =>await Task.Run(()=>Decode(entry,bytes,token),token);
    internal static ArchivePreviewViewModel Decode(ArchiveEntry entry,byte[] bytes,CancellationToken token=default)
    {
        var model=new ArchivePreviewViewModel(entry.Path){Bytes=bytes.Length,ModifiedText=entry.ModifiedLocal?.ToString("yyyy/MM/dd HH:mm") ?? "—"};
        try
        {
            token.ThrowIfCancellationRequested();
            if(ArchivePreviewPolicy.Kind(entry.Path)=="text")
            {
                var result=ArchivePreviewPolicy.DecodeText(bytes);model.Text=result.Text;
                model.Details=UiText.Format("Preview.TextDetails",bytes.Length,result.Encoding);
            }
            else
            {
                using var stream=new MemoryStream(bytes,false);
                BitmapDecoder decoder=Path.GetExtension(entry.Path).ToLowerInvariant() switch
                {
                    ".png"=>new PngBitmapDecoder(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None),
                    ".jpg" or ".jpeg"=>new JpegBitmapDecoder(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None),
                    ".bmp"=>new BmpBitmapDecoder(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None),
                    ".gif"=>new GifBitmapDecoder(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None),
                    _=>throw new NotSupportedException()
                };
                var frame=decoder.Frames[0];var width=frame.PixelWidth;var height=frame.PixelHeight;
                if(width<=0 || height<=0 || width>16384 || height>16384 || (long)width*height>16000000)
                {model.Message=UiText.Get("Preview.ImageTooLarge");return model;}
                token.ThrowIfCancellationRequested();
                var converted=new FormatConvertedBitmap(frame,PixelFormats.Pbgra32,null,0);
                var stride=checked(width*4);var pixels=new byte[checked(stride*height)];converted.CopyPixels(pixels,stride,0);
                token.ThrowIfCancellationRequested();
                model.Picture=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,pixels,stride);model.Picture.Freeze();
                model.imageBytes=bytes.ToArray();
                model.Details=UiText.Format("Preview.ImageDetails",width,height,bytes.Length);
                if(Path.GetExtension(entry.Path).Equals(".gif",StringComparison.OrdinalIgnoreCase)) model.Details+=" · "+UiText.Get("Preview.FirstFrame");
            }
        }
        catch(DecoderFallbackException) {model.Message=UiText.Get("Preview.TextInvalid");}
        catch(Exception error) when(error is IOException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {model.Message=UiText.Get("Preview.ImageInvalid");}
        return model;
    }
    internal void Clear() {cleared=true;Text="";Picture=null;if(imageBytes is not null)System.Security.Cryptography.CryptographicOperations.ZeroMemory(imageBytes);imageBytes=null;Changed(nameof(Text));Changed(nameof(Picture));System.Windows.Input.CommandManager.InvalidateRequerySuggested();}
}
