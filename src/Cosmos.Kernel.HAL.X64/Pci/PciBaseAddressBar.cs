namespace Cosmos.Kernel.HAL.X64.Pci;

public class PciBaseAddressBar
{
    private ushort _prefetchable;
    private ushort _type;

    public PciBaseAddressBar(uint raw)
    {
        IsIo = (raw & 0x01) == 1;

        if (IsIo)
        {
            BaseAddress = raw & 0xFFFFFFFC;
        }
        else
        {
            _type = (ushort)((raw >> 1) & 0x03);
            _prefetchable = (ushort)((raw >> 3) & 0x01);
            BaseAddress = _type switch
            {
                0x00 or 0x01 => raw & 0xFFFFFFF0,
                _ => BaseAddress
            };
        }
    }

    public uint BaseAddress { get; }

    public bool IsIo { get; }
}
