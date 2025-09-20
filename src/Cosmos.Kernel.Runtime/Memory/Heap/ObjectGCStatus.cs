namespace Cosmos.Kernel.Core.Memory.Heap;

/// <summary>
/// Flags to track an object status
/// All higher values in the ushort are used to track count of static counts
/// </summary>
public enum ObjectGcStatus : byte
{
    None = 0,
    Hit = 1
}
