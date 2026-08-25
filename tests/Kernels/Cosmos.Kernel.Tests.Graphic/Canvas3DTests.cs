using System.Numerics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.TestRunner.Framework;

namespace Cosmos.Kernel.Tests.Graphic;

/// <summary>
/// Tests for the public <see cref="Canvas3D"/> API surface that hold on every
/// CI display device: neither QEMU profile negotiates SVGA3D (and GOP has no
/// 3D at all), so discovery must fail truthfully, and the camera struct must
/// be usable from its default state. Actual rendering through
/// <see cref="Canvas3D"/> needs real VMware and is exercised manually.
/// </summary>
public static class Canvas3DTests
{
    /// <summary>
    /// A default-initialized or partially-initialized <see cref="Camera3D"/>
    /// must fall back to a usable up direction and field of view.
    /// </summary>
    public static void TestCamera3DDefaults()
    {
        Camera3D unset = default;
        Assert.True(unset.Up == Vector3.UnitY, "default camera up falls back to +Y");
        Assert.True(unset.FovY == 60f, "default camera fov falls back to 60 degrees");

        Camera3D partial = new Camera3D { Position = new Vector3(1f, 2f, 3f), Target = Vector3.Zero };
        Assert.True(partial.Up == Vector3.UnitY, "object initializer keeps the up fallback");
        Assert.True(partial.FovY == 60f, "object initializer keeps the fov fallback");

        Camera3D full = new Camera3D(new Vector3(0f, 1.5f, 3f), Vector3.Zero, Vector3.UnitZ, 45f);
        Assert.True(full.Up == Vector3.UnitZ, "explicit up is kept");
        Assert.True(full.FovY == 45f, "explicit fov is kept");
        Assert.True(full.Position == new Vector3(0f, 1.5f, 3f), "position is kept");
    }

    /// <summary>
    /// 3D discovery must fail without throwing on a display device that
    /// cannot render 3D, and the 2D canvas handed out instead must not
    /// masquerade as a <see cref="Canvas3D"/>.
    /// </summary>
    public static void TestCanvas3DDiscovery()
    {
        bool has3D = FullScreenCanvas.TryGetFullScreenCanvas3D(new Mode(640, 480, ColorDepth.ColorDepth32), out Canvas3D? canvas3D);

        Assert.False(has3D, "no CI display device negotiates 3D");
        Assert.Null(canvas3D, "no canvas is returned when 3D is unavailable");
        Assert.True(FullScreenCanvas.GetCurrentFullScreenCanvas() is not Canvas3D, "the 2D canvas does not report Canvas3D");
    }
}
