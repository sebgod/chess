<#
.SYNOPSIS
    Checks an Android App Bundle for the things Google Play rejects at upload.

.DESCRIPTION
    Cheap, offline, and each check stands for a failure that is otherwise found by a rejected
    upload -- or, worse, by a crash on someone's phone.

    THE ONE THAT MATTERS: 16 KB page alignment. Since November 2025 Play refuses a bundle whose
    64-bit native libraries are not aligned to 16 KB, because Android 15+ devices may use 16 KB
    memory pages and a 4 KB-aligned library cannot be mapped on them. Every .so the .NET Android
    SDK emits is already aligned; the risk is entirely in third-party natives, and chess had
    exactly one -- libSDL3.so from SDL3-CS.Android 3.4.10.5, which was the only misaligned library
    in the whole bundle. The build does warn (XA0141), but a warning in a 90-line publish log is
    not a gate, and the version that fixed it was two pins away from the one we had.

    32-bit ABIs are reported and never failed on: 16 KB pages are a 64-bit concern, and a Release
    build here ships arm64-v8a and x86_64 only.

.EXAMPLE
    ./packaging/android/check-aab.ps1 -Bundle Chess.Droid/bin/Release/net10.0-android/org.sebgod.chess-Signed.aab
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Bundle,

    # The page size every 64-bit native library must be aligned to. A parameter rather than a
    # constant so a future Play requirement is a flag, not an edit.
    [int] $PageSize = 16384,

    # Treat a missing signature as a failure. Opt-in, because a bundle built without any keystore
    # at all is still worth checking for alignment. Note that PRESENT is not the same as
    # ACCEPTABLE: a build with no upload key configured is signed with the auto-generated debug
    # key, which Play refuses by name. Only the job that supplied the key knows which it was.
    [switch] $RequireSignature
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-MinLoadAlignment {
    <#
      Smallest p_align across the PT_LOAD segments of an ELF image, which is what the loader has
      to honour -- one under-aligned segment makes the whole library unmappable on a 16 KB device,
      so the minimum is the number that decides it.
    #>
    param([byte[]] $Bytes)

    if ($Bytes.Length -lt 64) { throw 'not an ELF image (too short)' }
    if ($Bytes[0] -ne 0x7F -or $Bytes[1] -ne 0x45 -or $Bytes[2] -ne 0x4C -or $Bytes[3] -ne 0x46) {
        throw 'not an ELF image (bad magic)'
    }
    $is64 = $Bytes[4] -eq 2
    if ($Bytes[5] -ne 1) { throw 'big-endian ELF is not something Android produces' }

    if ($is64) {
        $phoff     = [BitConverter]::ToUInt64($Bytes, 0x20)
        $phentsize = [BitConverter]::ToUInt16($Bytes, 0x36)
        $phnum     = [BitConverter]::ToUInt16($Bytes, 0x38)
        $alignAt   = 0x30
    } else {
        $phoff     = [BitConverter]::ToUInt32($Bytes, 0x1C)
        $phentsize = [BitConverter]::ToUInt16($Bytes, 0x2A)
        $phnum     = [BitConverter]::ToUInt16($Bytes, 0x2C)
        $alignAt   = 0x1C
    }

    $min = [uint64]::MaxValue
    for ($i = 0; $i -lt $phnum; $i++) {
        $off = [int]$phoff + $i * $phentsize
        if ($off + $phentsize -gt $Bytes.Length) { throw 'program header table runs past end of file' }
        if ([BitConverter]::ToUInt32($Bytes, $off) -ne 1) { continue }   # PT_LOAD only
        $align = if ($is64) { [BitConverter]::ToUInt64($Bytes, $off + $alignAt) }
                 else       { [uint64][BitConverter]::ToUInt32($Bytes, $off + $alignAt) }
        if ($align -lt $min) { $min = $align }
    }
    if ($min -eq [uint64]::MaxValue) { throw 'no PT_LOAD segments' }
    return $min
}

function Read-Entry {
    param([System.IO.Compression.ZipArchive] $Zip, [string] $Name)
    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { return $null }
    $ms = [System.IO.MemoryStream]::new()
    $s = $entry.Open()
    try { $s.CopyTo($ms) } finally { $s.Dispose() }
    return $ms.ToArray()
}

if (-not (Test-Path -LiteralPath $Bundle)) { throw "No such bundle: $Bundle" }
$resolved = (Resolve-Path -LiteralPath $Bundle).Path
$size = [math]::Round((Get-Item -LiteralPath $resolved).Length / 1MB, 1)
Write-Host "Checking $resolved ($size MB)"

$zip = [System.IO.Compression.ZipFile]::OpenRead($resolved)
$failures = New-Object System.Collections.Generic.List[string]

try {
    $names = $zip.Entries.FullName

    # 1. It really is a bundle. An .apk has none of these, and "I uploaded the apk" is a mistake
    #    whose error message in the Play Console does not mention the file format.
    foreach ($required in @('BundleConfig.pb', 'base/manifest/AndroidManifest.xml')) {
        if ($names -notcontains $required) { $failures.Add("Not an app bundle: missing $required") }
    }

    # 2. The native libraries, which is the whole point of this script.
    $libs = @($names | Where-Object { $_ -like 'base/lib/*' -and $_.EndsWith('.so') })
    if ($libs.Count -eq 0) { $failures.Add('No native libraries at all, which cannot be right for this app') }

    $abis = @($libs | ForEach-Object { $_.Split('/')[2] } | Sort-Object -Unique)
    Write-Host "ABIs: $($abis -join ', ')  ($($libs.Count) libraries)"

    $sixtyFour = @('arm64-v8a', 'x86_64', 'riscv64')
    $checked = 0
    foreach ($lib in $libs | Sort-Object) {
        $abi = $lib.Split('/')[2]
        $bytes = Read-Entry -Zip $zip -Name $lib
        try { $align = Get-MinLoadAlignment -Bytes $bytes }
        catch { $failures.Add("$lib : $($_.Exception.Message)"); continue }

        if ($sixtyFour -contains $abi) {
            $checked++
            if ($align -lt $PageSize) {
                $failures.Add("$lib is aligned to $align, needs $PageSize (Play rejects this)")
            }
        } elseif ($align -lt $PageSize) {
            Write-Host "  (32-bit, not required) $lib aligned to $align"
        }
    }
    Write-Host "$checked 64-bit libraries checked for $PageSize-byte page alignment"

    # 3. The SDL pair, reported rather than enforced. The Java half lives in dex by this point and
    #    cannot be read back, but the native half carries its version as a plain string -- enough to
    #    tell at a glance which SDL a bundle was built from when one misbehaves on a device.
    $sdl = @($libs | Where-Object { $_.EndsWith('libSDL3.so') } | Select-Object -First 1)
    if ($sdl.Count -gt 0) {
        $text = [Text.Encoding]::ASCII.GetString((Read-Entry -Zip $zip -Name $sdl[0]))
        $m = [regex]::Match($text, 'SDL-3\.\d+\.\d+[-\w.]*')
        if ($m.Success) { Write-Host "SDL native build: $($m.Value)" }
    }

    # 4. Signature. Play takes an unsigned bundle from nobody: the upload key is how it knows the
    #    upload is yours, even though Play re-signs with the app signing key before shipping.
    $signed = @($names | Where-Object { $_ -like 'META-INF/*' -and ($_.EndsWith('.RSA') -or $_.EndsWith('.EC') -or $_.EndsWith('.DSA')) })
    if ($signed.Count -gt 0) {
        Write-Host "Signed: yes ($($signed[0]))"
    } elseif ($RequireSignature) {
        $failures.Add('Bundle is not signed, so it cannot be uploaded to Play')
    } else {
        Write-Host 'Signed: NO (fine for a CI artifact; Play will refuse it)'
    }
}
finally { $zip.Dispose() }

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "FAILED ($($failures.Count)):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
