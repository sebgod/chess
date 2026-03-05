using System.IO;
using BenchmarkDotNet.Attributes;
using Chess.Lib;
using Console.Lib;
using Chess.Lib.UI;
using ImageMagick;
using Microsoft.VSDiagnostics;

namespace Chess.Console.Benchmarks;

[MemoryDiagnoser]
[CPUUsageDiagnoser]
public class SixelEncoderBenchmark
{
    private MagickImageRenderer _renderer = null!;
    private MagickImage _surface = null!;
    private GameUI _gameUI = null!;
    private RectInt _fullClip;
    private int _partialStartY;
    private uint _partialHeight;

    [GlobalSetup]
    public void Setup()
    {
        _renderer = new MagickImageRenderer();
        _surface = new MagickImage(MagickColors.Black, 800, 800);
        var game = new Game();
        _gameUI = new GameUI(game, 800, 800);
        var squareSize = _gameUI.SquareSize;
        _fullClip = new RectInt((squareSize * 10, squareSize * 10), PointInt.Origin);

        // Render once so the surface has realistic chess board content
        _gameUI.Render(_renderer, _surface, _fullClip);

        // Partial render: middle third of the image (simulates a clip region)
        _partialStartY = (int)(_surface.Height / 3);
        _partialHeight = _surface.Height / 3;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _surface.Dispose();
        _renderer.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void MagickSixel_Full()
    {
        _surface.Write(Stream.Null, MagickFormat.Sixel);
    }

    [Benchmark]
    public void CustomSixel_Full()
    {
        SixelEncoder.Encode(_surface, Stream.Null);
    }

    [Benchmark]
    public void MagickSixel_Partial()
    {
        using var cropped = _surface.Clone();
        cropped.Crop(new MagickGeometry(0, _partialStartY, _surface.Width, _partialHeight));
        cropped.ResetPage();
        cropped.Write(Stream.Null, MagickFormat.Sixel);
    }

    [Benchmark]
    public void CustomSixel_Partial()
    {
        SixelEncoder.Encode(_surface, _partialStartY, _partialHeight, Stream.Null);
    }
}
