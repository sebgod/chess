using BenchmarkDotNet.Attributes;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.ImageMagickSixelRenderer;
using DIR.Lib;
using ImageMagick;
using Microsoft.VSDiagnostics;

namespace Chess.Console.Benchmarks;
[CPUUsageDiagnoser]
public class RenderBoardBenchmark
{
    private MagickImageRenderer _renderer = null!;
    private MagickImage _surface = null!;
    private GameUI _gameUI = null!;
    private RectInt _fullClip;
    [GlobalSetup]
    public void Setup()
    {
        _surface = new MagickImage(MagickColors.White, 800, 800);
        _renderer = new MagickImageRenderer(_surface);
        var game = new Game();
        _gameUI = new GameUI(game, 800, 800);
        var squareSize = _gameUI.SquareSize;
        _fullClip = new RectInt((squareSize * 10, squareSize * 10), PointInt.Origin);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _surface.Dispose();
        _renderer.Dispose();
    }

    [Benchmark]
    public void RenderFullBoard()
    {
        _gameUI.Render<MagickImage, MagickImageRenderer>(_renderer, _fullClip);
    }
}