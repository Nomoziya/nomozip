using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using ComDataObject=System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ArchivePassword.Desktop;

// Expose only synchronous IDataObject. The shell must finish reading before DoDragDrop
// returns; no async capability is advertised, so the workspace can then be deleted.
internal static class ArchiveFileDrag
{
    internal static bool Copy(IReadOnlyList<string> paths,IDropSource? source=null)
    {
        var data=new DataObject(DataFormats.FileDrop,paths.ToArray());
        var native=new SynchronousFiles((ComDataObject)data);
        var initialized=OleInitialize(IntPtr.Zero);
        if(initialized<0) Marshal.ThrowExceptionForHR(initialized);
        try
        {
            var result=DoDragDrop(native,source ?? new CopySource(),1,out var effect);
            if(result<0) Marshal.ThrowExceptionForHR(result);
            return result==0x40100 && effect==1;
        }
        finally {OleUninitialize();GC.KeepAlive(native);}
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int DoDragDrop([MarshalAs(UnmanagedType.Interface)] ComDataObject data,
        [MarshalAs(UnmanagedType.Interface)] IDropSource source,uint allowed,out uint effect);
    [ComVisible(true),Guid("00000121-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropSource
    {
        [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escape,uint keys);
        [PreserveSig] int GiveFeedback(uint effect);
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    public sealed class CopySource:IDropSource
    {
        public int QueryContinueDrag(bool escape,uint keys)=>escape?0x40101:(keys&1)==0?0x40100:0;
        public int GiveFeedback(uint effect)=>0x40102;
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    public sealed class SynchronousFiles(ComDataObject inner):ComDataObject
    {
        public void GetData(ref FORMATETC format,out STGMEDIUM medium)=>inner.GetData(ref format,out medium);
        public void GetDataHere(ref FORMATETC format,ref STGMEDIUM medium)=>inner.GetDataHere(ref format,ref medium);
        public int QueryGetData(ref FORMATETC format)=>inner.QueryGetData(ref format);
        public int GetCanonicalFormatEtc(ref FORMATETC format,out FORMATETC output)=>inner.GetCanonicalFormatEtc(ref format,out output);
        public void SetData(ref FORMATETC format,ref STGMEDIUM medium,bool release)=>inner.SetData(ref format,ref medium,release);
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)=>inner.EnumFormatEtc(direction);
        public int DAdvise(ref FORMATETC format,ADVF flags,IAdviseSink sink,out int connection)=>inner.DAdvise(ref format,flags,sink,out connection);
        public void DUnadvise(int connection)=>inner.DUnadvise(connection);
        public int EnumDAdvise(out IEnumSTATDATA? enumerator)=>inner.EnumDAdvise(out enumerator);
    }
}
