using System.Numerics;

namespace Cosmos.Kernel.System.Graphics;

/// <summary>
/// Describes the point of view used for 3D drawing on a <see cref="Canvas3D"/>.
/// The aspect ratio always comes from the canvas mode, so a camera only
/// carries where it stands, what it looks at, and its field of view.
/// </summary>
/// <remarks>
/// A default-initialized camera is still usable: <see cref="Up"/> falls back
/// to <see cref="Vector3.UnitY"/> and <see cref="FovY"/> to 60 degrees when
/// left unset, so object initializers may set only the members they care
/// about.
/// </remarks>
public struct Camera3D
{
    private Vector3 _up;
    private float _fovY;

    /// <summary>
    /// The position of the camera in world space.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// The point in world space the camera looks at.
    /// </summary>
    public Vector3 Target { get; set; }

    /// <summary>
    /// The up direction of the camera. Reads as <see cref="Vector3.UnitY"/>
    /// while unset.
    /// </summary>
    public Vector3 Up
    {
        get => _up == default ? Vector3.UnitY : _up;
        set => _up = value;
    }

    /// <summary>
    /// The vertical field of view in degrees. Reads as 60 while unset or
    /// non-positive.
    /// </summary>
    public float FovY
    {
        get => _fovY <= 0f ? 60f : _fovY;
        set => _fovY = value;
    }

    /// <summary>
    /// Creates a camera standing at <paramref name="position"/> looking at
    /// <paramref name="target"/>, with the default up direction and field of
    /// view.
    /// </summary>
    /// <param name="position">The position of the camera in world space.</param>
    /// <param name="target">The point in world space the camera looks at.</param>
    public Camera3D(Vector3 position, Vector3 target)
    {
        Position = position;
        Target = target;
    }

    /// <summary>
    /// Creates a camera standing at <paramref name="position"/> looking at
    /// <paramref name="target"/>.
    /// </summary>
    /// <param name="position">The position of the camera in world space.</param>
    /// <param name="target">The point in world space the camera looks at.</param>
    /// <param name="up">The up direction of the camera.</param>
    /// <param name="fovY">The vertical field of view in degrees.</param>
    public Camera3D(Vector3 position, Vector3 target, Vector3 up, float fovY)
    {
        Position = position;
        Target = target;
        _up = up;
        _fovY = fovY;
    }
}
