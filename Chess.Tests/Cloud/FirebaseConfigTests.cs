using Chess.Net.Cloud;
using Shouldly;
using Xunit;

namespace Chess.Tests.Cloud;

/// <summary>
/// The config the deployment is handed, parsed. Most of this is shape-checking, but the key selection
/// is the one piece with a failure mode worth a test of its own: picking the wrong key does not throw,
/// it returns a config that works everywhere except a real native client, where it 403s at sign-in
/// inside a retry loop and reads like a broken auth implementation.
/// </summary>
public class FirebaseConfigTests
{
    private const string Live = """
        {
          "projectId": "chess-app-bce7a",
          "databaseURL": "https://chess-app-bce7a-default-rtdb.europe-west1.firebasedatabase.app",
          "apiKey": "browser-key",
          "apiKeyNative": "native-key",
          "authDomain": "chess-app-bce7a.firebaseapp.com"
        }
        """;

    /// <summary>
    /// The whole reason there are two keys: the browser's carries an HTTP-referrer restriction, and a
    /// referrer restriction refuses a request that sends no <c>Referer</c> — which is every native
    /// client. Chess.Net must therefore take the override whenever the config offers one.
    /// </summary>
    [Fact]
    public void NativeKey_IsPreferredOverTheBrowserOne()
    {
        FirebaseConfig.TryParse(Live)!.ApiKey.ShouldBe("native-key");
    }

    /// <summary>A config written before the override existed — or a fork's own project with one
    /// unrestricted key — still works, which is why this is an override and not a second field the
    /// client requires.</summary>
    [Fact]
    public void WithNoOverride_TheOrdinaryKeyIsUsed()
    {
        var json = Live.Replace("\"apiKeyNative\": \"native-key\",", "");
        FirebaseConfig.TryParse(json)!.ApiKey.ShouldBe("browser-key");
    }

    [Fact]
    public void TrailingSlashOnTheDatabaseUrl_IsNotDoubledIntoEveryRequest()
    {
        var json = Live.Replace(".app\"", ".app/\"");
        FirebaseConfig.TryParse(json)!.DatabaseUrl.ShouldEndWith(".firebasedatabase.app");
    }

    /// <summary>
    /// Absent is a normal state, not an error — a fork, a PR build, any checkout that has not opted in
    /// — so every "no cloud" case has to come back null rather than throw. The HTML one is real: a dev
    /// server with no such file answers 200 and its index page.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<!DOCTYPE html><html><body>not a config</body></html>")]
    [InlineData("{ this is not json")]
    [InlineData("[]")]
    [InlineData("""{"projectId":"chess-app-bce7a"}""")]                       // no key, no database
    [InlineData("""{"apiKey":"k","databaseURL":"https://x.firebaseio.com"}""")] // no project
    public void NoUsableConfig_IsNullRatherThanAThrow(string? json)
    {
        FirebaseConfig.TryParse(json).ShouldBeNull();
    }

    /// <summary>An emulator config carries no key because there is no project to have one, but the
    /// auth emulator still refuses an empty <c>key=</c> with a 403 that names a missing API key. Any
    /// non-empty string satisfies it; the browser half passes "demo", so this half does too.</summary>
    [Fact]
    public void EmulatorConfig_NeedsNoKeyButNeverSendsAnEmptyOne()
    {
        var config = FirebaseConfig.TryParse("""
            {
              "projectId": "demo-chess",
              "emulator": { "auth": "http://127.0.0.1:9099", "host": "127.0.0.1", "port": 9000 }
            }
            """);

        config.ShouldNotBeNull();
        config.IsEmulator.ShouldBeTrue();
        config.ApiKey.ShouldNotBeEmpty();
    }
}
