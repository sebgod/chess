using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Chess.Lib;
using Chess.UCI;
using SdlVulkan.Renderer.Inspector.Client;
using Shouldly;
using Xunit;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Chess.Tests;

/// <summary>
/// Starts the real Chess.GUI window and drives it through the inspector: the menu panel over the saved game, Esc on the
/// first step resuming that game, F8 back to the menu, the picker marking the game "(current)", Backspace out of it,
/// and the panel's close control closing onto the game.
///
/// <para>Runs only in a GUI lane: a process started with <c>DIR_INSPECTOR=1</c> and a <c>DIR_INSPECTOR_TOKEN</c>, which
/// the GUI inherits (on this machine, <c>pilot run --gui -- dotnet test ... --filter-trait Category=GUI</c>). Anywhere
/// else it is skipped, so CI's plain test run passes over it. Chess.GUI must be built in the same configuration; a
/// screenshot of each step and the GUI's stderr go to <c>PILOT_JOB_ARTIFACTS</c> when the lane sets it.</para>
///
/// <para>The GUI gets a data folder of its own (<c>CHESS_DATA_DIR</c>) seeded with one unfinished game, so it never sees
/// the user's games and the backdrop is known.</para>
/// </summary>
[Trait("Category", "GUI")]
public sealed class GuiSmokeTests : IDisposable
{
    private static readonly TimeSpan StepBudget = TimeSpan.FromSeconds(20);

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "chess-gui-smoke-" + Guid.NewGuid().ToString("N"));
    private readonly string? _artifacts = Environment.GetEnvironmentVariable("PILOT_JOB_ARTIFACTS") is { Length: > 0 } a ? a : null;

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Menu_panel_resumes_the_game_behind_it()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("DIR_INSPECTOR") == "1"
            && Environment.GetEnvironmentVariable("DIR_INSPECTOR_TOKEN") is { Length: > 0 },
            "GUI lane only: needs DIR_INSPECTOR=1 and DIR_INSPECTOR_TOKEN (pilot run --gui).");

        var ct = TestContext.Current.CancellationToken;
        var exe = FindGuiExecutable();

        // One unfinished hot-seat game, three plies in: the backdrop, and what Esc resumes.
        var game = new Game();
        foreach (var move in new[] { "e2e4", "e7e5", "g1f3" })
        {
            game.TryMove(UciMove.Parse(move)).IsMoveOrCapture().ShouldBeTrue(move);
        }
        GameInbox.Save(_dataDir, "smoke", game, Side.None, GameMode.PlayerVsPlayer, "", DateTimeOffset.UtcNow);

        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(exe),
        };
        start.Environment["CHESS_DATA_DIR"] = _dataDir;

        var stderr = new StringBuilder();
        using var gui = new Process { StartInfo = start };
        gui.ErrorDataReceived += (_, e) => { if (e.Data is { } line) lock (stderr) stderr.AppendLine(line); };
        gui.OutputDataReceived += (_, _) => { };
        gui.Start().ShouldBeTrue();
        gui.BeginErrorReadLine();
        gui.BeginOutputReadLine();

        try
        {
            var client = await ConnectAsync(gui, ct);

            await WaitForScreenAsync(client, "menu", ct);
            await SaveScreenshotAsync(client, "1-menu.png", ct);

            // Esc on the first step closes the panel onto the saved game.
            await client.PressKeyAsync("Escape", ct: ct);
            var resumed = await WaitForScreenAsync(client, "game", ct);
            resumed.GetProperty("plyCount").GetInt32().ShouldBe(3);
            await SaveScreenshotAsync(client, "2-game.png", ct);

            // F8 goes back to the menu; its first row is "Continue game", which opens the picker.
            await client.PressKeyAsync("F8", ct: ct);
            await WaitForScreenAsync(client, "menu", ct);
            await client.PressKeyAsync("Enter", ct: ct);
            var picker = await WaitForScreenAsync(client, "picker", ct);
            picker.GetProperty("pickerRows").GetInt32().ShouldBe(1);
            await SaveScreenshotAsync(client, "3-picker.png", ct);

            // Backspace leaves the picker.
            await client.PressKeyAsync("Backspace", ct: ct);
            await WaitForScreenAsync(client, "menu", ct);
            await SaveScreenshotAsync(client, "4-menu-close.png", ct);

            // The panel's close control (top-right on the first step) closes onto the game, as Esc does.
            var (closeX, closeY) = await FindRegionAsync(client, "MenuClose", ct);
            await client.ClickAsync(closeX, closeY, ct: ct);
            var closed = await WaitForScreenAsync(client, "game", ct);
            closed.GetProperty("plyCount").GetInt32().ShouldBe(3);
        }
        finally
        {
            if (!gui.HasExited)
            {
                gui.Kill(entireProcessTree: true);
                await gui.WaitForExitAsync(CancellationToken.None);
            }
            if (_artifacts is not null)
            {
                lock (stderr) File.WriteAllText(Path.Combine(_artifacts, "gui-stderr.log"), stderr.ToString());
            }
        }
    }

    // The app registers once its loop runs, a moment after start; until then the pid is not in the registry.
    private static async Task<InspectorClient> ConnectAsync(Process gui, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            gui.HasExited.ShouldBeFalse($"Chess.GUI exited early with code {(gui.HasExited ? gui.ExitCode : 0)}");
            try
            {
                return await InspectorClient.ConnectAsync(gui.Id, ct: ct);
            }
            catch (InspectorException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250, ct);
            }
        }
    }

    // Input is applied on the render thread, so a screen change shows a frame or two after the key.
    private static async Task<JsonElement> WaitForScreenAsync(InspectorClient client, string screen, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StepBudget;
        var last = "";
        while (true)
        {
            var ui = await client.DescribeUiAsync(ct);
            if (ui.TryGetProperty("appState", out var state)
                && state.TryGetProperty("screen", out var shown))
            {
                last = shown.GetString() ?? "";
                if (last == screen) return state.Clone();
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"waited {StepBudget.TotalSeconds}s for screen '{screen}', still on '{last}'");
            }
            await Task.Delay(100, ct);
        }
    }

    // The centre of the region whose label is <paramref name="label"/> (a ButtonHit's action), from describe.
    private static async Task<(float X, float Y)> FindRegionAsync(InspectorClient client, string label, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StepBudget;
        while (true)
        {
            var ui = await client.DescribeUiAsync(ct);
            if (ui.TryGetProperty("regions", out var regions))
            {
                foreach (var region in regions.EnumerateArray())
                {
                    if (region.TryGetProperty("label", out var l) && l.GetString() == label)
                    {
                        return (region.GetProperty("x").GetSingle() + region.GetProperty("w").GetSingle() / 2f,
                            region.GetProperty("y").GetSingle() + region.GetProperty("h").GetSingle() / 2f);
                    }
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"no region labelled '{label}' within {StepBudget.TotalSeconds}s");
            }
            await Task.Delay(100, ct);
        }
    }

    private async Task SaveScreenshotAsync(InspectorClient client, string name, CancellationToken ct)
    {
        if (_artifacts is null) return;
        var shot = await client.ScreenshotAsync(ct: ct);
        await File.WriteAllBytesAsync(Path.Combine(_artifacts, name), shot.Png, ct);
    }

    // Chess.GUI's output for the configuration this test assembly was built in: <repo>/Chess.GUI/bin/<config>/<tfm>/.
    private static string FindGuiExecutable()
    {
        var testDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var tfm = testDir.Name;
        var config = testDir.Parent?.Name ?? "";
        var repo = testDir;
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Chess.sln")))
        {
            repo = repo.Parent;
        }
        repo.ShouldNotBeNull("Chess.sln not found above the test assembly");

        var name = OperatingSystem.IsWindows() ? "Chess.GUI.exe" : "Chess.GUI";
        var exe = Path.Combine(repo.FullName, "Chess.GUI", "bin", config, tfm, name);
        File.Exists(exe).ShouldBeTrue($"build Chess.GUI -c {config} first: {exe} is missing");
        return exe;
    }
}
