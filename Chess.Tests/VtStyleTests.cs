using Console.Lib;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public sealed class VtStyleTests
{
    [Fact]
    public void SgrColor_ToRgba_ReturnsExpectedValues()
    {
        SgrColor.Black.ToRgba().ShouldBe(new RGBAColor32(0x00, 0x00, 0x00, 0xff));
        SgrColor.BrightWhite.ToRgba().ShouldBe(new RGBAColor32(0xff, 0xff, 0xff, 0xff));
        SgrColor.Red.ToRgba().ShouldBe(new RGBAColor32(0xaa, 0x00, 0x00, 0xff));
    }

    [Fact]
    public void NearestSgrColor_ExactMatch_ReturnsOriginal()
    {
        foreach (SgrColor c in Enum.GetValues<SgrColor>())
        {
            SgrColorExtensions.NearestSgrColor(c.ToRgba()).ShouldBe(c);
        }
    }

    [Fact]
    public void NearestSgrColor_ArbitraryColor_ReturnsClosest()
    {
        SgrColorExtensions.NearestSgrColor(new RGBAColor32(0xfe, 0xfe, 0xfe, 0xff))
            .ShouldBe(SgrColor.BrightWhite);

        SgrColorExtensions.NearestSgrColor(new RGBAColor32(0x90, 0x10, 0x10, 0xff))
            .ShouldBe(SgrColor.Red);
    }

    [Fact]
    public void Apply_Sgr16_ProducesSgrCodes()
    {
        var style = new VtStyle(SgrColor.BrightWhite, SgrColor.Black);
        style.Apply(ColorMode.Sgr16).ShouldBe("\e[97;40m");
    }

    [Fact]
    public void Apply_Sgr16_WithRgba_FallsBackToNearest()
    {
        var style = new VtStyle(new RGBAColor32(0xff, 0xff, 0xff, 0xff), new RGBAColor32(0x00, 0x00, 0x00, 0xff));
        style.Apply(ColorMode.Sgr16).ShouldBe("\e[97;40m");
    }

    [Fact]
    public void Apply_TrueColor_EmitsTruecolorSequence()
    {
        var style = new VtStyle(new RGBAColor32(0x12, 0x34, 0x56, 0xff), new RGBAColor32(0x78, 0x9a, 0xbc, 0xff));
        style.Apply(ColorMode.TrueColor).ShouldBe("\e[38;2;18;52;86;48;2;120;154;188m");
    }

    [Fact]
    public void Apply_TrueColor_WithSgrColors_UsesRgbaValues()
    {
        var style = new VtStyle(SgrColor.BrightWhite, SgrColor.Blue);
        style.Apply(ColorMode.TrueColor).ShouldBe("\e[38;2;255;255;255;48;2;0;0;170m");
    }

    [Fact]
    public void ToString_DefaultsToSgr16()
    {
        var style = new VtStyle(SgrColor.BrightWhite, SgrColor.Black);
        style.ToString().ShouldBe("\e[97;40m");
    }
}
