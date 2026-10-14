using System.Globalization;
namespace ArchivePassword.Core;
public static class VolumeSizeOptions
{
    public static bool TryGetBytes(int option,string custom,int unit,out ulong size)
    {
        size=0;if(option==0) return true;
        if(option is >=1 and <=3) {size=option==1 ? 100UL<<20 : option==2 ? 1UL<<30 : 4UL<<30;return true;}
        if(option!=4 || unit is not (0 or 1) || !ulong.TryParse(custom.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var number) || number==0) return false;
        var multiplier=unit==0 ? 1UL<<20 : 1UL<<30;if(number>(1UL<<40)/multiplier) return false;
        size=number*multiplier;return true;
    }
}
