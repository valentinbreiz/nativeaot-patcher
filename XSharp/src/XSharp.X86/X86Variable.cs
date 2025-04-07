namespace XSharp.X86;

/// <summary>
/// stores some compile time data
/// </summary>
public class X86Variable
{
    public X86DataSize DataSize { get; init; }
    public string Name { get; init; }

    public byte[]? Data8Bit { get; init; }
    public ushort[]? Data16Bit { get; init; }
    public uint[]? Data32Bit { get; init; }
    public ulong[]? Data64Bit { get; init; }

    public X86Variable(string name, params ulong[] data)
    {
        Name = name;
        DataSize = X86DataSize.QWord;
        Data64Bit = data;
    }

    public X86Variable(string name, params uint[] data)
    {
        Name = name;
        DataSize = X86DataSize.DWord;
        Data32Bit = data;
    }

    public X86Variable(string name, params ushort[] data)
    {
        Name = name;
        DataSize = X86DataSize.Word;
        Data16Bit = data;
    }

    public X86Variable(string name, params byte[] data)
    {
        Name = name;
        DataSize = X86DataSize.Byte;
        Data8Bit = data;
    }

    public override string ToString() =>
        DataSize switch
        {
            X86DataSize.Byte => $"{Name} db {Convert.ToHexString(Data8Bit)}",
            X86DataSize.Word => $"{Name} dw {Convert.ToHexString(ToBytes(Data16Bit))}",
            X86DataSize.DWord => $"{Name} dw {Convert.ToHexString(ToBytes(Data32Bit))}",
            X86DataSize.QWord => $"{Name} dw {Convert.ToHexString(ToBytes(Data64Bit))}",
            _ => throw new ArgumentOutOfRangeException()
        };

    private static byte[] ToBytes(ushort[] c)
    {
        byte[] target = new byte[c.Length * 2];
        Buffer.BlockCopy(c, 0, target, 0, c.Length * 2);
        return target;
    }

    private static byte[] ToBytes(ulong[] c)
    {
        byte[] target = new byte[c.Length * 8];
        Buffer.BlockCopy(c, 0, target, 0, c.Length * 8);
        return target;
    }

    private static byte[] ToBytes(uint[] c)
    {
        byte[] target = new byte[c.Length * 4];
        Buffer.BlockCopy(c, 0, target, 0, c.Length * 4);
        return target;
    }
}
