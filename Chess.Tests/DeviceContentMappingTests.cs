using Chess.Lib.UI;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

/// <summary>
/// Pure-math tests for <see cref="DeviceContentMapping"/> — the host's device→content mapping for
/// safe-area insets and the display-cutout rect. The 180° hot-seat flip is the wired case: the camera
/// notch must land on the content's BOTTOM edge when the frame faces the far player, or the board would
/// slide UNDER the punch-hole the moment the frame rotates.
/// </summary>
public sealed class DeviceContentMappingTests
{
    [Fact]
    public void Identity_leaves_insets_untouched()
    {
        DeviceContentMapping.ToContentInsets((10, 20, 30, 40), DeviceTransform.Identity)
            .ShouldBe((10, 20, 30, 40));
    }

    [Theory]
    // (deviceL, deviceT, deviceR, deviceB) -> expected content (L, T, R, B) per rotation.
    [InlineData(Rotation90.Cw90, 20, 30, 40, 10)]  // device top -> content left, and so on around
    [InlineData(Rotation90.Half, 30, 40, 10, 20)]  // top<->bottom and left<->right
    [InlineData(Rotation90.Cw270, 40, 10, 20, 30)]
    public void Rotation_permute_insets_onto_the_edges_they_obscure(
        Rotation90 rotation, int left, int top, int right, int bottom)
    {
        // Distinct depths so a wrong edge assignment cannot pass by symmetry.
        DeviceContentMapping.ToContentInsets((10, 20, 30, 40), new DeviceTransform(rotation, 1f, 0f, 0f))
            .ShouldBe((left, top, right, bottom));
    }

    [Fact]
    public void Identity_leaves_rect_untouched()
    {
        DeviceContentMapping.ToContentRect((4, 6, 20, 14), DeviceTransform.Identity)
            .ShouldBe((4, 6, 20, 14));
    }

    [Fact]
    public void Half_mirrors_rect_about_the_surface_centre()
    {
        // 64x32 surface flipped 180°: device (4,6)-(20,14) -> content (64-20, 32-14)-(64-4, 32-6).
        var half = DeviceTransform.CenteredRotation(Rotation90.Half, 64, 32);
        DeviceContentMapping.ToContentRect((4, 6, 20, 14), half)
            .ShouldBe((44, 18, 60, 26));
    }

    [Fact]
    public void Half_maps_the_full_surface_onto_itself()
    {
        // The whole-device rect inverted through the centred flip is the whole content rect — the
        // mapping covers the surface exactly (no drift), which is what keeps draw and hit-test aligned.
        var half = DeviceTransform.CenteredRotation(Rotation90.Half, 64, 32);
        DeviceContentMapping.ToContentRect((0, 0, 64, 32), half)
            .ShouldBe((0, 0, 64, 32));
    }
}
