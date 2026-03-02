using BenchmarkDotNet.Attributes;
using Chess.Lib;
using Chess.Lib.UI;
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
        _renderer = new MagickImageRenderer();
        _surface = new MagickImage(MagickColors.White, 800, 800);
        var game = new Game();
        _gameUI = new GameUI(game, 800, 800);
        var squareSize = _gameUI.SquareSize;
        _fullClip = new RectInt(new PointInt(squareSize * 10, squareSize * 10), new PointInt(0, 0));
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
        _gameUI.Render(_renderer, _surface, _fullClip);
    }
}