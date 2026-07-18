using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;

namespace Chess.Web.E2E.Tests;

/// <summary>
/// Shared setup for the Play-by-Link browser suite: it makes sure a Chess.Web dev server is
/// reachable and launches one headless browser that every test opens a fresh, isolated context on.
///
/// Server: set <c>CHESS_WEB_BASEURL</c> to reuse a dev server you already have running (fast local
/// iteration); otherwise the fixture starts <c>dotnet run --project Chess.Web</c> itself and tears
/// it down at the end (the self-contained path CI uses).
///
/// Browser: bundled Chromium by default (what CI downloads via <c>playwright install</c>); set
/// <c>CHESS_E2E_CHANNEL</c> (e.g. <c>msedge</c>) to drive a system-installed browser instead — the
/// reliable path on win-arm64, where the native Edge avoids the bundled-Chromium download question.
/// </summary>
public sealed class ChessWebFixture : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private Process? _server;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        BaseUrl = (Environment.GetEnvironmentVariable("CHESS_WEB_BASEURL") ?? await StartServerAsync())
            .TrimEnd('/') + "/";

        var channel = Environment.GetEnvironmentVariable("CHESS_E2E_CHANNEL");
        EnsureBrowserInstalled(channel);

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Channel = string.IsNullOrWhiteSpace(channel) ? null : channel,
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_server is { HasExited: false })
        {
            try { _server.Kill(entireProcessTree: true); }
            catch { /* already gone — nothing to clean up */ }
            _server.Dispose();
        }
    }

    /// <summary>Opens a fresh, isolated context (own storage, clipboard granted) and a blank page.</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            // Copy link writes to and the tests read back from the async clipboard.
            Permissions = ["clipboard-read", "clipboard-write"],
            BaseURL = BaseUrl,
        });
        return await context.NewPageAsync();
    }

    // ── server lifecycle ────────────────────────────────────────────────────

    private async Task<string> StartServerAsync()
    {
        var repoRoot = FindRepoRoot();
        // A distinct port from the conventional 5000 dev server, so an auto-started run never
        // collides with one a developer already has up.
        const string url = "http://127.0.0.1:5177";

        _server = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet",
                $"run --project \"{Path.Combine(repoRoot, "Chess.Web")}\" -c Release")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        _server.StartInfo.Environment["ASPNETCORE_URLS"] = url;
        _server.Start();

        await WaitForServerAsync(url, TimeSpan.FromMinutes(3));
        return url;
    }

    private static async Task WaitForServerAsync(string url, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode) return;
            }
            catch { /* not up yet */ }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Chess.Web dev server did not come up at {url} within {timeout}.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Chess.Web", "Chess.Web.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root (no Chess.Web/Chess.Web.csproj above the test binary).");
    }

    // ── browser install ─────────────────────────────────────────────────────

    // A system-channel run (msedge/chrome) uses an already-installed browser — nothing to fetch.
    // For the bundled default, drive Playwright's own installer so a clean checkout needs no manual
    // `playwright install` step before the first run.
    private static void EnsureBrowserInstalled(string? channel)
    {
        if (!string.IsNullOrWhiteSpace(channel)) return;

        var exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exit != 0)
            throw new InvalidOperationException(
                $"`playwright install chromium` failed (exit {exit}). On win-arm64, set " +
                "CHESS_E2E_CHANNEL=msedge to use the system Edge instead.");
    }
}

[CollectionDefinition(Name)]
public sealed class ChessWebCollection : ICollectionFixture<ChessWebFixture>
{
    public const string Name = "chess-web";
}
