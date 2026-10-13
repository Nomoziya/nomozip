using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

public static class WindowPlacement
{
    public static readonly DependencyProperty AutoFitProperty=DependencyProperty.RegisterAttached("AutoFit",typeof(bool),typeof(WindowPlacement),new PropertyMetadata(false,OnAutoFit));
    public static bool GetAutoFit(DependencyObject value)=>(bool)value.GetValue(AutoFitProperty);
    public static void SetAutoFit(DependencyObject value,bool enabled)=>value.SetValue(AutoFitProperty,enabled);
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(State),typeof(WindowPlacement));
    internal static bool ValidationMode {get;set;}
    private static void OnAutoFit(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not Window window) return;
        if((bool)args.NewValue && !ValidationMode) Attach(window);
        else if(window.GetValue(StateProperty) is State state) {state.Dispose();window.ClearValue(StateProperty);}
    }
    private static void Attach(Window window)
    {if(window.GetValue(StateProperty) is null) {var state=new State(window);window.SetValue(StateProperty,state);state.Attach();}}
    internal static void AttachForValidation(Window window)=>Attach(window);
    internal static bool FitCurrent(Window window,bool center=false)=>(window.GetValue(StateProperty) as State)?.Fit(center) ?? false;
    private sealed class State(Window window)
    {
        private HwndSource? source;
        private double minimumWidth,minimumHeight,maximumWidth,maximumHeight;
        private bool initialized,pending,closed,moving,starting=true;
        internal void Attach()
        {
            window.SourceInitialized+=OnSource;window.Loaded+=OnLoaded;window.ContentRendered+=OnRendered;window.DpiChanged+=OnDpi;window.Closed+=OnClosed;
            if(new WindowInteropHelper(window).Handle!=0) Initialize();
            if(window.IsLoaded) {starting=false;Fit(center:true);}
        }
        private void OnSource(object? sender,EventArgs args)=>Initialize();
        private void Initialize()
        {
            if(initialized) return;initialized=true;
            minimumWidth=window.MinWidth;minimumHeight=window.MinHeight;maximumWidth=window.MaxWidth;maximumHeight=window.MaxHeight;
            source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source?.AddHook(Hook);
        }
        private void OnLoaded(object sender,RoutedEventArgs args)=>Fit(center:true);
        private void OnRendered(object? sender,EventArgs args) {if(starting) {Fit(center:true);starting=false;}}
        private void OnDpi(object sender,DpiChangedEventArgs args)=>Queue();
        private nint Hook(nint hwnd,int message,nint wparam,nint lparam,ref bool handled)
        {
            if(message==0x0231) moving=true; // WM_ENTERSIZEMOVE: do not prevent crossing a monitor boundary.
            if(message==0x0232) {moving=false;Queue();}
            if(message is 0x007E or 0x001A) Queue(); // display/work-area changes; WPF handles WM_DPICHANGED itself.
            return 0;
        }
        private void Queue()
        {
            if(pending || closed) return;pending=true;
            window.Dispatcher.BeginInvoke(()=>{pending=false;if(!closed) Fit(center:starting);},DispatcherPriority.ApplicationIdle);
        }
        internal bool Fit(bool center=false)
        {
            if(!initialized || closed || window.WindowState!=WindowState.Normal) return false;
            var hwnd=new WindowInteropHelper(window).Handle;
            var owner=center && window.WindowStartupLocation==WindowStartupLocation.CenterOwner && window.Owner is {} parent ? new WindowInteropHelper(parent).Handle:0;
            var monitor=MonitorFromWindow(owner!=0?owner:hwnd,2);
            var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};
            if(monitor==0 || !GetMonitorInfoW(monitor,ref info) || !GetWindowRect(hwnd,out var native) || info.Work.Width<=0 || info.Work.Height<=0) return false;
            var dpi=GetDpiForWindow(hwnd);if(dpi==0) return false;var scale=dpi/96.0;
            // Work-area sizes are converted once; screen positions always stay in native pixels.
            window.MinWidth=Math.Min(minimumWidth,info.Work.Width/scale);window.MinHeight=Math.Min(minimumHeight,info.Work.Height/scale);
            if(window.SizeToContent!=SizeToContent.Manual) {window.MaxWidth=Math.Min(maximumWidth,info.Work.Width/scale);window.MaxHeight=Math.Min(maximumHeight,info.Work.Height/scale);}
            if(!double.IsNaN(window.Width) && window.Width>info.Work.Width/scale) window.Width=info.Work.Width/scale;
            if(!double.IsNaN(window.Height) && window.Height>info.Work.Height/scale) window.Height=info.Work.Height/scale;
            GetWindowRect(hwnd,out native);
            var bounds=new PixelBounds(native.Left,native.Top,native.Right,native.Bottom);var work=new PixelBounds(info.Work.Left,info.Work.Top,info.Work.Right,info.Work.Bottom);
            if(moving) return true;
            if(center && window.WindowStartupLocation!=WindowStartupLocation.Manual)
            {
                var around=owner!=0 && GetWindowRect(owner,out var ownerRect)?new PixelBounds(ownerRect.Left,ownerRect.Top,ownerRect.Right,ownerRect.Bottom):work;
                bounds=Center(bounds,around);
            }
            var fitted=Clamp(bounds,work);
            if(fitted==bounds && native.Left==fitted.Left && native.Top==fitted.Top && native.Right==fitted.Right && native.Bottom==fitted.Bottom) return true;
            return SetWindowPos(hwnd,0,fitted.Left,fitted.Top,fitted.Width,fitted.Height,0x0014); // no activation or z-order change
        }
        private void OnClosed(object? sender,EventArgs args)=>Dispose();
        internal void Dispose()
        {
            if(closed) return;closed=true;source?.RemoveHook(Hook);source=null;
            window.SourceInitialized-=OnSource;window.Loaded-=OnLoaded;window.ContentRendered-=OnRendered;window.DpiChanged-=OnDpi;window.Closed-=OnClosed;
        }
    }
    internal readonly record struct PixelBounds(int Left,int Top,int Right,int Bottom)
    {internal int Width=>Right-Left;internal int Height=>Bottom-Top;}
    internal static PixelBounds Clamp(PixelBounds bounds,PixelBounds work)
    {
        var width=Math.Min(Math.Max(1,bounds.Width),work.Width);var height=Math.Min(Math.Max(1,bounds.Height),work.Height);
        var left=Math.Clamp(bounds.Left,work.Left,work.Right-width);var top=Math.Clamp(bounds.Top,work.Top,work.Bottom-height);
        return new(left,top,left+width,top+height);
    }
    internal static PixelBounds Center(PixelBounds bounds,PixelBounds around)
    {var left=around.Left+(around.Width-bounds.Width)/2;var top=around.Top+(around.Height-bounds.Height)/2;return new(left,top,left+bounds.Width,top+bounds.Height);}
    internal static void FitWorkArea(Window window, Rect workArea)
    {
        window.MinWidth = Math.Min(window.MinWidth, workArea.Width);
        window.MinHeight = Math.Min(window.MinHeight, workArea.Height);
        window.Width = Math.Min(window.Width, workArea.Width);
        window.Height = Math.Min(window.Height, workArea.Height);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {internal int Left,Top,Right,Bottom;internal readonly int Width=>Right-Left;internal readonly int Height=>Bottom-Top;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {internal int Size;internal NativeRect Monitor,Work;internal uint Flags;}
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd,out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
}
