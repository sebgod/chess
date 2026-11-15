# Renders the pages of a PDF to PNG with the Windows built-in PDF engine (Windows.Data.Pdf), so the
# annotated worksheet can be LOOKED AT without pdftoppm / pymupdf / Ghostscript (none are installed).
#
# Run with WINDOWS PowerShell 5.1 (powershell.exe), not pwsh 7: 7 cannot project WinRT types.
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File render-pdf-page.ps1 -Pdf in.pdf -OutDir out [-Width 1600]
# Writes <OutDir>\page-1.png, page-2.png, ...
#
# NB: this engine draws annotation APPEARANCE STREAMS only. That is the point: an annotation that
# shows up here has a real /AP and will show up in every viewer.
param(
    [Parameter(Mandatory)] [string] $Pdf,
    [Parameter(Mandatory)] [string] $OutDir,
    [int] $Width = 1600
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]

$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
    Select-Object -First 1
$asTaskAction = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } |
    Select-Object -First 1

# Blocks on the task's wait handle (an event), never a polling loop.
function Await($op, [Type] $resultType) {
    $task = $asTask.MakeGenericMethod($resultType).Invoke($null, @($op))
    $task.Wait() | Out-Null
    $task.Result
}
function AwaitAction($op) {
    $asTaskAction.Invoke($null, @($op)).Wait() | Out-Null
}

$Pdf = (Resolve-Path $Pdf).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Pdf)) ([Windows.Storage.StorageFile])
$doc = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
for ($i = 0; $i -lt $doc.PageCount; $i++) {
    $page = $doc.GetPage($i)
    $opts = [Windows.Data.Pdf.PdfPageRenderOptions]::new()
    $opts.DestinationWidth = $Width
    $stream = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
    AwaitAction ($page.RenderToStreamAsync($stream, $opts))
    $out = Join-Path $OutDir ("page-{0}.png" -f ($i + 1))
    $net = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream.GetInputStreamAt(0))
    $fs = [System.IO.File]::Create($out)
    try { $net.CopyTo($fs) } finally { $fs.Dispose(); $net.Dispose(); $stream.Dispose(); $page.Dispose() }
    Write-Output $out
}
