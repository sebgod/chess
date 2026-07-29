# Resize the console window hosting Chess.Console, so the app sees a real terminal resize
# (WINDOW_BUFFER_SIZE_EVENT) exactly as dragging the frame by hand would produce one. The inspector
# has no `resize` verb -- a resize is a property of the WINDOW, not of the app -- so this is how the
# resize path gets driven without asking the user to drag anything.
#
# The window is owned by WindowsTerminal.exe, NOT by Chess.Console.exe and NOT by its cmd.exe parent,
# so Get-Process -Name Chess.Console | MainWindowHandle is IntPtr.Zero and looking for a cmd window
# finds nothing either. Match on the TITLE that `start "Chess TUI"` set, whoever happens to own it.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File resize_window.ps1 -Width 1000 -Height 760
#
# Afterwards check `size` through the inspector (columns/rows should have changed) and read
# inspector.log. A LIVE app answers `ping`; a crashed one leaves a stack trace in the log.

param(
    [int]$Width = 1000,
    [int]$Height = 760,
    [string]$Title = '*Chess TUI*'
)

Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool repaint);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@

$app = Get-Process -Name Chess.Console -ErrorAction SilentlyContinue
if (-not $app) { Write-Output 'NO-PROCESS: the app is not running (launch it first)'; exit 1 }

$win = Get-Process | Where-Object { $_.MainWindowTitle -like $Title } | Select-Object -First 1
if (-not $win) { Write-Output "NO-WINDOW: no window titled $Title"; exit 2 }
Write-Output ("window owner: {0} (pid {1}), app pid {2}" -f $win.ProcessName, $win.Id, $app.Id)

$r = New-Object W.U+RECT
[void][W.U]::GetWindowRect($win.MainWindowHandle, [ref]$r)
Write-Output ('before: {0}x{1} at {2},{3}' -f ($r.R - $r.L), ($r.B - $r.T), $r.L, $r.T)

[void][W.U]::MoveWindow($win.MainWindowHandle, $r.L, $r.T, $Width, $Height, $true)
Start-Sleep -Milliseconds 1200

[void][W.U]::GetWindowRect($win.MainWindowHandle, [ref]$r)
Write-Output ('after:  {0}x{1}' -f ($r.R - $r.L), ($r.B - $r.T))

# Liveness is the point of the test: the resize path rebuilds the renderer surface and re-arranges the
# frame, which is where a crash would land.
$still = Get-Process -Id $app.Id -ErrorAction SilentlyContinue
Write-Output ('app alive after resize: {0}' -f [bool]$still)
if (-not $still) { exit 3 }
