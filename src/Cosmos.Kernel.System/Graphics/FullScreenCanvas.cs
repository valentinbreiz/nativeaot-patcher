using Cosmos.Kernel.Core;
using Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.Pci.Enums;

namespace Cosmos.Kernel.System.Graphics;

/// <summary>
/// Provides functionality to fetch canvases that write directly to the
/// underlying display device.
/// </summary>
public static class FullScreenCanvas
{
    /// <summary>
    /// Whether the CGS (Cosmos Graphics Subsystem) is currently in use.
    /// </summary>
    public static bool IsInUse { get; private set; }

    /// <summary>
    /// Disables the specified graphics driver used, and returns to VGA text mode 80x25.
    /// </summary>
    public static void Disable()
    {
        if (IsInUse)
        {
            s_videoDriver!.Disable();
            IsInUse = false;
        }
    }

    private static Canvas? s_videoDriver = null;

    /// <summary>
    /// Gets a <see cref="Canvas"/> instance, using an implementation based on
    /// the currently used video driver.
    /// </summary>
    private static Canvas GetVideoDriver()
    {
        return CreateVideoDriver(null);
    }

    /// <summary>
    /// Gets a <see cref="Canvas"/> instance, using an implementation based on
    /// the currently used video driver, constructing the canvas with the given
    /// <paramref name="mode"/>.
    /// </summary>
    private static Canvas GetVideoDriver(Mode mode)
    {
        return CreateVideoDriver(mode);
    }

    /// <summary>
    /// Creates the canvas matching the detected display device. On the VMware
    /// SVGA II adapter the canvas type depends on whether the device
    /// negotiated 3D support, so users can discover 3D capability with
    /// <c>canvas is Canvas3D</c>.
    /// </summary>
    private static Canvas CreateVideoDriver(Mode? mode)
    {
        if (CosmosFeatures.PCIEnabled)
        {
            PciDevice? svgaDevice = PciManager.GetDevice(VendorId.VmWare, DeviceId.SvgaiiAdapter);
            if (svgaDevice is not null)
            {
                SvgaIIDriver driver = new SvgaIIDriver(svgaDevice);
                Mode svgaMode = mode ?? SvgaIIRender.DefaultMode;

                return driver.Is3DEnabled
                    ? new SvgaII3DCanvas(driver, svgaMode)
                    : new SvgaIICanvas(driver, svgaMode);
            }
        }

        return mode is null ? new GopCanvas() : new GopCanvas(mode.Value);
    }

    /// <summary>
    /// Gets the screen display canvas. The canvas's <see cref="Canvas.Mode"/> reflects the
    /// actual framebuffer resolution (set by the driver at construction); subsequent calls
    /// return the same canvas without resetting the mode, so callers always see the real
    /// screen width/height.
    /// </summary>
    public static Canvas GetFullScreenCanvas()
    {
        if (!Cosmos.Kernel.Core.CosmosFeatures.GraphicsEnabled)
        {
            throw new InvalidOperationException("Graphics support is disabled. Set CosmosEnableGraphics=true in your csproj to enable it.");
        }

        s_videoDriver ??= GetVideoDriver();

        IsInUse = true;
        return s_videoDriver;
    }

    /// <summary>
    /// Gets a screen display canvas, and changes the display mode to the given <paramref name="mode"/>.
    /// </summary>
    public static Canvas GetFullScreenCanvas(Mode mode)
    {
        if (!Cosmos.Kernel.Core.CosmosFeatures.GraphicsEnabled)
        {
            throw new InvalidOperationException("Graphics support is disabled. Set CosmosEnableGraphics=true in your csproj to enable it.");
        }

        if (s_videoDriver == null)
        {
            s_videoDriver = GetVideoDriver(mode);
        }
        else
        {
            s_videoDriver.Mode = mode;
        }

        IsInUse = true;
        return s_videoDriver;
    }

    /// <summary>
    /// Attempts to get a screen display canvas, and changes the display mode to the default.
    /// </summary>
    /// <returns><see langword="true"/> if the operation was successful; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetFullScreenCanvas(Mode mode, out Canvas? canvas)
    {
        try
        {
            canvas = GetFullScreenCanvas(mode);
            IsInUse = true;
            return true;
        }
        catch
        {
        }

        canvas = null;
        return false;
    }

    /// <summary>
    /// Gets the currently used screen display canvas.
    /// </summary>
    public static Canvas? GetCurrentFullScreenCanvas()
    {
        return s_videoDriver;
    }

    /// <summary>
    /// Gets the screen display canvas as a <see cref="Canvas3D"/>, using the
    /// default graphics mode.
    /// </summary>
    /// <exception cref="NotSupportedException">The display device does not support 3D rendering.</exception>
    /// <exception cref="InvalidOperationException">Graphics support is disabled.</exception>
    public static Canvas3D GetFullScreenCanvas3D()
    {
        return GetFullScreenCanvas() as Canvas3D
            ?? throw new NotSupportedException("The display device does not support 3D rendering.");
    }

    /// <summary>
    /// Gets the screen display canvas as a <see cref="Canvas3D"/>, and
    /// changes the display mode to the given <paramref name="mode"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">The display device does not support 3D rendering.</exception>
    /// <exception cref="InvalidOperationException">Graphics support is disabled.</exception>
    public static Canvas3D GetFullScreenCanvas3D(Mode mode)
    {
        return GetFullScreenCanvas(mode) as Canvas3D
            ?? throw new NotSupportedException("The display device does not support 3D rendering.");
    }

    /// <summary>
    /// Attempts to get the screen display canvas as a <see cref="Canvas3D"/>,
    /// changing the display mode to the given <paramref name="mode"/>. Fails
    /// when the display device does not support 3D rendering.
    /// </summary>
    /// <returns><see langword="true"/> if the operation was successful; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetFullScreenCanvas3D(Mode mode, out Canvas3D? canvas)
    {
        try
        {
            canvas = GetFullScreenCanvas3D(mode);
            return true;
        }
        catch
        {
        }

        canvas = null;
        return false;
    }
}
