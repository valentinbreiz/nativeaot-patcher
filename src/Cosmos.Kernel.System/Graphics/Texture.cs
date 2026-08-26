using System;

namespace Cosmos.Kernel.System.Graphics;

/// <summary>
/// An image uploaded to a 3D device, ready to be mapped onto meshes. Created
/// with <see cref="Canvas3D.CreateTexture"/>; dispose it to release the
/// device memory it occupies.
/// </summary>
public sealed class Texture : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// The canvas that created this texture.
    /// </summary>
    internal Canvas3D Owner { get; }

    /// <summary>
    /// Backend-specific resource data, owned by the canvas that created the
    /// texture.
    /// </summary>
    internal object? DriverData { get; }

    internal Texture(Canvas3D owner, int width, int height, object? driverData)
    {
        Owner = owner;
        Width = width;
        Height = height;
        DriverData = driverData;
    }

    /// <summary>
    /// The width of the texture in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// The height of the texture in pixels.
    /// </summary>
    public int Height { get; }

    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Releases the device memory held by this texture. The texture must no
    /// longer be referenced by any mesh that is still drawn.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Owner.DestroyTexture(this);
    }
}
