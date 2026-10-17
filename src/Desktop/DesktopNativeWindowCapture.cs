using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

// Isolated-runner evidence only; never called by normal product flows.
internal static class DesktopNativeWindowCapture
{
    internal static async Task<object> CaptureAsync(Window window,string path)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true") throw new InvalidOperationException("Native window capture requires isolated CI.");
        var left=window.Left;var top=window.Top;var opacity=window.Opacity;
        try
        {
            window.Left=32;window.Top=32;window.Opacity=1;window.Activate();
            await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.Render);
            await Task.Delay(120);DwmFlush();
            var handle=new WindowInteropHelper(window).Handle;
            if(!GetWindowRect(handle,out var bounds))throw new Win32Exception(Marshal.GetLastWin32Error());
            var width=bounds.Right-bounds.Left;var height=bounds.Bottom-bounds.Top;
            var windowDc=GetWindowDC(handle);var memoryDc=CreateCompatibleDC(windowDc);
            var bitmap=CreateCompatibleBitmap(windowDc,width,height);var previous=SelectObject(memoryDc,bitmap);
            try
            {
                // PW_RENDERFULLCONTENT includes the OS caption and accelerated WPF surface.
                if(!PrintWindow(handle,memoryDc,2))throw new Win32Exception(Marshal.GetLastWin32Error());
                var source=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                var opaque=new FormatConvertedBitmap(source,PixelFormats.Bgr32,null,0);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(opaque));
                using(var stream=File.Create(path))encoder.Save(stream);
                var style=GetWindowLongW(handle,-16);
                return new {file=Path.GetFileName(path),width,height,method="PrintWindow/PW_RENDERFULLCONTENT",nativeFrame=true,composited=false,
                    nativeCaption=(style&0x00C00000)==0x00C00000,captionMode=WindowChromeBehavior.GetEnabled(window)?"integrated":"system",minimizeBox=(style&0x00020000)!=0,maximizeBox=(style&0x00010000)!=0,systemMenu=(style&0x00080000)!=0};
            }
            finally {SelectObject(memoryDc,previous);DeleteObject(bitmap);DeleteDC(memoryDc);ReleaseDC(handle,windowDc);}
        }
        finally {window.Opacity=opacity;window.Left=left;window.Top=top;}
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window,out NativeRect rect);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(nint window,nint dc,uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindowDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window,nint dc);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window,int index);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc,int width,int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
