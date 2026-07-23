using System.Numerics;
using DIR.Lib;

namespace Chess.Lib.UI;

/// <summary>
/// Maps host-reported DEVICE-space values (safe-area insets, the display-cutout rect) into content
/// space for a renderer <see cref="DeviceTransform"/> — the layout-side mirror of the renderer folding
/// the same transform into its projection and of pointer input coming back through
/// <see cref="DeviceTransform.Invert"/> at the host boundary. Under a 180° hot-seat flip the camera
/// notch sits on the content's BOTTOM edge, so the insets swap top↔bottom and left↔right; the
/// <see cref="PixelGameDisplay{TSurface}"/> layout just consumes the mapped values and never learns why.
/// </summary>
public static class DeviceContentMapping
{
    /// <summary>
    /// Permutes device safe-area insets onto the content edges they actually obscure. Inset DEPTHS pass
    /// through unchanged (the transform is a 90°-multiple rotation + translation); only which edge each
    /// depth belongs to moves. Assumes the transform maps the content box onto the surface box (as
    /// <see cref="DeviceTransform.CenteredRotation"/> does) — a letterboxed 90°/270° fit would leave
    /// device edges with no content counterpart, and the edge permutation no longer applies there.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) ToContentInsets(
        (int Left, int Top, int Right, int Bottom) deviceInsets, DeviceTransform transform)
        => transform.Rotation switch
        {
            // Cw90: the content's left edge lands on the device top (photo rotated 90° clockwise).
            Rotation90.Cw90 => (deviceInsets.Top, deviceInsets.Right, deviceInsets.Bottom, deviceInsets.Left),
            // Half: the flip — top↔bottom and left↔right.
            Rotation90.Half => (deviceInsets.Right, deviceInsets.Bottom, deviceInsets.Left, deviceInsets.Top),
            // Cw270: the content's left edge lands on the device bottom.
            Rotation90.Cw270 => (deviceInsets.Bottom, deviceInsets.Left, deviceInsets.Top, deviceInsets.Right),
            _ => deviceInsets,
        };

    /// <summary>
    /// Maps a device-space rect (e.g. the display-cutout bounds) into content space by inverting both
    /// corners and re-normalizing — exact for 90°-multiple rotations, where rects stay axis-aligned.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) ToContentRect(
        (int Left, int Top, int Right, int Bottom) deviceRect, DeviceTransform transform)
    {
        if (transform.IsIdentity) return deviceRect;
        var a = transform.Invert(new Vector2(deviceRect.Left, deviceRect.Top));
        var b = transform.Invert(new Vector2(deviceRect.Right, deviceRect.Bottom));
        return ((int)MathF.Round(MathF.Min(a.X, b.X)), (int)MathF.Round(MathF.Min(a.Y, b.Y)),
                (int)MathF.Round(MathF.Max(a.X, b.X)), (int)MathF.Round(MathF.Max(a.Y, b.Y)));
    }
}
