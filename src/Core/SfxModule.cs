using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ArchivePassword.Core;
public sealed record SfxModule(string Version,string Sha256,byte[] Bytes)
{
    public bool IsValid()
    {
        if(Bytes.Length<512 || Bytes.Length>512*1024 || !Convert.ToHexString(SHA256.HashData(Bytes)).Equals(Sha256,StringComparison.OrdinalIgnoreCase)
            || Bytes[0]!='M' || Bytes[1]!='Z') return false;
        var offset=BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(60,4));
        return offset>=64 && offset<=Bytes.Length-24 && Bytes.AsSpan(offset,4).SequenceEqual("PE\0\0"u8)
            && BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(offset+4,2)) is 0x14c or 0x8664;
    }
}
