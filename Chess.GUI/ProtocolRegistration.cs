using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Chess.GUI;

/// <summary>
/// Registers (and unregisters) this executable as the handler for <c>chess://</c> links.
///
/// <para><b>Always an explicit action, never a silent write on first run.</b> Chess ships as a
/// published folder rather than an installer, so there is no install step that could reasonably claim
/// a URL scheme on the user's behalf — and a program that quietly makes itself the handler for
/// something is a program people uninstall. Hence a <c>--register-protocol</c> /
/// <c>--unregister-protocol</c> pair: visible, reversible, and reporting what it did.</para>
///
/// <para>Per-user in both places, so neither needs elevation and neither can affect another account.</para>
/// </summary>
internal static class ProtocolRegistration
{
    public const string Scheme = "chess";

    private const string DesktopFileName = "sharpastro-chess.desktop";

    /// <summary>Registers the scheme. Returns an exit code: 0 on success, 1 on failure.</summary>
    public static int Register()
    {
        // Environment.ProcessPath is the running executable -- the AOT binary itself, not a host --
        // which is exactly what the handler has to point at. It is null only in exotic hosting
        // situations that do not apply to a published app.
        if (Environment.ProcessPath is not { } exe)
        {
            Console.Error.WriteLine("Can't determine this executable's path, so there is nothing to register.");
            return 1;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return RegisterWindows(exe);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return RegisterLinux(exe);

            // macOS associates schemes through CFBundleURLTypes in an .app bundle's Info.plist, and
            // chess does not produce a bundle. Saying so is better than writing something that has no
            // effect and reporting success.
            Console.Error.WriteLine(
                "Registering chess:// on macOS needs a real .app bundle, which this build doesn't produce.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Couldn't register chess://: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Removes the registration. Absent is success — this is meant to be safe to repeat.</summary>
    public static int Unregister()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Scheme}", throwOnMissingSubKey: false);
                Console.WriteLine("chess:// links are no longer handled by this app.");
                return 0;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var path = Path.Combine(ApplicationsDir, DesktopFileName);
                if (File.Exists(path)) File.Delete(path);
                UpdateDesktopDatabase();
                Console.WriteLine("chess:// links are no longer handled by this app.");
                return 0;
            }

            Console.Error.WriteLine("Nothing to unregister on this platform.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Couldn't unregister chess://: {ex.Message}");
            return 1;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int RegisterWindows(string exe)
    {
        // HKCU\Software\Classes, not HKCR or HKLM: per-user, no elevation, and removable by the same
        // user who added it. The empty "URL Protocol" VALUE is what marks the key as a scheme handler
        // -- its presence is the signal, its content is irrelevant, and omitting it leaves a key that
        // looks right and does nothing.
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
        key.SetValue("", "URL:Chess correspondence link");
        key.SetValue("URL Protocol", "");

        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exe}\" \"%1\"");

        Console.WriteLine($"chess:// links now open {exe}");
        return 0;
    }

    private static int RegisterLinux(string exe)
    {
        Directory.CreateDirectory(ApplicationsDir);
        var path = Path.Combine(ApplicationsDir, DesktopFileName);

        // %u, not %U: this handler takes exactly one URL. Exec entries are field-code sensitive and a
        // desktop file with the wrong arity is a launcher that silently passes nothing.
        File.WriteAllText(path, string.Join('\n',
        [
            "[Desktop Entry]",
            "Type=Application",
            "Name=Chess",
            "Comment=Correspondence chess",
            $"Exec={exe} %u",
            "Terminal=false",
            "NoDisplay=true",
            $"MimeType=x-scheme-handler/{Scheme};",
            "",
        ]));

        UpdateDesktopDatabase();
        Console.WriteLine($"chess:// links now open {exe}");
        return 0;
    }

    private static string ApplicationsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "applications");

    /// <summary>
    /// Refreshes the desktop MIME cache. Best-effort on purpose: the file is what matters, the cache
    /// is an index some environments rebuild themselves, and a missing
    /// <c>update-desktop-database</c> must not turn a successful registration into a failure.
    /// </summary>
    private static void UpdateDesktopDatabase()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "update-desktop-database",
                ArgumentList = { ApplicationsDir },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"(note) couldn't refresh the desktop database: {ex.Message}");
        }
    }
}
