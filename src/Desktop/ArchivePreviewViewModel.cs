using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed class ArchivePreviewViewModel : ObservableObject
{
    public string EntryPath {get;}
    public string Text {get;private set;}="";
    public BitmapSource? Picture {get;private set;}
    public string Details {get;private set;}="";
    public string Message {get;private set;}="";
    public bool IsText=>Picture is null && Message.Length==0;
    public bool IsImage=>Picture is not null;
    public bool HasError=>Message.Length>0;
    private ArchivePreviewViewModel(string path)=>EntryPath=path;
    public static async Task<ArchivePreviewViewModel> CreateAsync(ArchiveEntry entry,byte[] bytes,CancellationToken token=default)
        =>await Task.Run(()=>Decode(entry,bytes,token),token);
    internal static ArchivePreviewViewModel Decode(ArchiveEntry entry,byte[] bytes,CancellationToken token=default)
    {
        var model=new ArchivePreviewViewModel(entry.Path);
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
                model.Details=UiText.Format("Preview.ImageDetails",width,height,bytes.Length);
                if(Path.GetExtension(entry.Path).Equals(".gif",StringComparison.OrdinalIgnoreCase)) model.Details+=" · "+UiText.Get("Preview.FirstFrame");
            }
        }
        catch(DecoderFallbackException) {model.Message=UiText.Get("Preview.TextInvalid");}
        catch(Exception error) when(error is IOException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {model.Message=UiText.Get("Preview.ImageInvalid");}
        return model;
    }
    internal void Clear() {Text="";Picture=null;Changed(nameof(Text));Changed(nameof(Picture));}
}
