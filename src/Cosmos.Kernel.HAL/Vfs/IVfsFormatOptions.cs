// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Marker for filesystem-specific format parameters. Each driver casts to
/// its own concrete type (e.g. <c>FatFormatOptions</c>); a null value means
/// "use driver defaults."
/// </summary>
public interface IVfsFormatOptions
{
}
