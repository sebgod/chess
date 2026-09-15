#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reports the Google-side state this repository depends on, and can create the parts that are
    creatable from a CLI.

.DESCRIPTION
    Everything chess needs from Google is one Firebase project on the Spark plan plus, later, a
    Play listing. Almost all of it is reachable from `firebase` and `gh`; what is not is listed at
    the end of the run and in docs/google-ops.md.

    The default run is READ-ONLY. It never prints an API key, a database URL or a secret value --
    the config is deliberately not in this repository (docs/correspondence-play.md, "Why the config
    is a secret"), and an audit that pasted it into a terminal scrollback would undo that. Where a
    value must be inspected, only the verdict is printed.

    -Provision creates what is missing and is safe to re-run; it cannot mint credentials, because
    both remaining ones (a CI token, an upload keystore) need consent or a passphrase.

.EXAMPLE
    pwsh scripts/google-audit.ps1
    pwsh scripts/google-audit.ps1 -Provision
    pwsh scripts/google-audit.ps1 -Strict     # non-zero exit when something required is missing
#>
[CmdletBinding()]
param(
    [string] $ProjectId      = 'chess-app-bce7a',
    [string] $Location       = 'europe-west1',
    [string] $AndroidPackage = 'org.sebgod.chess',
    [switch] $Provision,
    [switch] $Strict
)

$ErrorActionPreference = 'Stop'

$repoRoot    = Split-Path -Parent $PSScriptRoot
$firebaseDir = Join-Path $repoRoot 'firebase'
$findings    = [System.Collections.Generic.List[pscustomobject]]::new()

# OK      = as intended.
# TODO    = not done yet, and known not to be -- a later phase, not a regression.
# MISSING = something this repository already assumes is there. -Strict fails on these.
# UNKNOWN = not checkable from here; the note says what would check it.
function Add-Finding([string] $Area, [string] $State, [string] $Detail) {
    $findings.Add([pscustomobject]@{ Area = $Area; State = $State; Detail = $Detail })
}

function Test-Tool([string] $Name) {
    $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

# The CLI is a devDependency of firebase/package.json, so --no-install keeps this honest: it uses
# the pinned copy or fails, rather than silently fetching some other version from the network.
function Invoke-FirebaseJson {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)

    Push-Location $firebaseDir
    try {
        $raw = & npx --no-install firebase @Arguments --project $ProjectId --json 2>$null
    }
    finally {
        Pop-Location
    }
    if (-not $raw) { return $null }
    try { return ($raw -join "`n" | ConvertFrom-Json) } catch { return $null }
}

function Invoke-Firebase {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)

    Push-Location $firebaseDir
    try {
        & npx --no-install firebase @Arguments --project $ProjectId --non-interactive
    }
    finally {
        Pop-Location
    }
}

Write-Host ''
Write-Host "Google-side audit for $ProjectId" -ForegroundColor Cyan
Write-Host ('-' * 72)

# ---------------------------------------------------------------- tooling ----
foreach ($tool in @('node', 'npx', 'gh')) {
    if (Test-Tool $tool) { Add-Finding 'tooling' 'OK' "$tool on PATH" }
    else { Add-Finding 'tooling' 'MISSING' "$tool is not on PATH; this script needs it" }
}
$hasGcloud = Test-Tool 'gcloud'
if ($hasGcloud) {
    Add-Finding 'tooling' 'OK' 'gcloud on PATH (API-key restrictions and the billing check are readable)'
}
else {
    Add-Finding 'tooling' 'UNKNOWN' 'gcloud is not installed; API-key restrictions and the billing plan cannot be read from here'
}

# ---------------------------------------------------------------- project ----
$projects = Invoke-FirebaseJson 'projects:list'
if (-not $projects -or $projects.status -ne 'success') {
    Add-Finding 'project' 'MISSING' 'firebase projects:list failed -- not logged in? run: npx firebase login'
}
else {
    $project = $projects.result | Where-Object { $_.projectId -eq $ProjectId }
    if ($project) { Add-Finding 'project' 'OK' "project $ProjectId exists" }
    else {
        Add-Finding 'project' 'MISSING' "no project $ProjectId on this account"
        if ($Provision) {
            Write-Host "  creating project $ProjectId ..." -ForegroundColor Yellow
            Invoke-Firebase 'projects:create' $ProjectId
        }
    }
}

# The plan is the whole safety model: Spark refuses rather than bills. Only the Cloud Billing API
# can answer it, so without gcloud this stays UNKNOWN rather than being guessed at.
if ($hasGcloud) {
    $billing = (& gcloud beta billing projects describe $ProjectId --format='value(billingEnabled)' 2>$null)
    if (-not $billing -or $billing -eq 'False') {
        Add-Finding 'plan' 'OK' 'no billing account attached -- still Spark'
    }
    else {
        Add-Finding 'plan' 'MISSING' 'a billing account IS attached: this is a Blaze project and the free-tier ceiling is gone'
    }
}
else {
    Add-Finding 'plan' 'UNKNOWN' 'check with: gcloud beta billing projects describe <project> --format="value(billingEnabled)" (want False)'
}

# --------------------------------------------------------------- database ----
$dbs = Invoke-FirebaseJson 'database:instances:list'
if (-not $dbs -or $dbs.status -ne 'success') {
    Add-Finding 'database' 'MISSING' 'could not list database instances'
}
else {
    $instances = @($dbs.result)
    if ($instances.Count -eq 0) {
        Add-Finding 'database' 'MISSING' 'no Realtime Database instance'
        if ($Provision) {
            Write-Host "  creating the default database instance in $Location ..." -ForegroundColor Yellow
            Invoke-Firebase 'database:instances:create' "$ProjectId-default-rtdb" '--location' $Location
        }
    }
    else {
        # Spark allows exactly one, and its location is permanent. A second one appearing means
        # someone upgraded the plan.
        if ($instances.Count -gt 1) {
            Add-Finding 'database' 'MISSING' "$($instances.Count) database instances: Spark allows one, so this project is no longer on Spark"
        }
        foreach ($i in $instances) {
            $state = if ($i.location -eq $Location -and $i.state -eq 'ACTIVE') { 'OK' } else { 'MISSING' }
            Add-Finding 'database' $state "$($i.name) [$($i.type)] in $($i.location), $($i.state)"
        }
    }
}

# ------------------------------------------------------------------- apps ----
$apps = Invoke-FirebaseJson 'apps:list'
if (-not $apps -or $apps.status -ne 'success') {
    Add-Finding 'apps' 'MISSING' 'could not list apps'
}
else {
    foreach ($platform in @('WEB', 'ANDROID')) {
        $app = $apps.result | Where-Object { $_.platform -eq $platform } | Select-Object -First 1
        if ($app) {
            Add-Finding 'apps' 'OK' "$platform app registered ($($app.displayName))"
        }
        else {
            Add-Finding 'apps' 'MISSING' "no $platform app -- an app registration is what mints a per-front-end API key"
            if ($Provision) {
                Write-Host "  creating the $platform app ..." -ForegroundColor Yellow
                if ($platform -eq 'ANDROID') {
                    Invoke-Firebase 'apps:create' 'ANDROID' 'chess-android' '--package-name' $AndroidPackage
                }
                else {
                    Invoke-Firebase 'apps:create' 'WEB' 'chess'
                }
            }
        }
    }

    # The Android package name is permanent on both sides (Play will not reissue it, and the key
    # restriction is keyed on it), so it is worth checking that the two agree. The config carries
    # the API key, so only the verdict leaves this block.
    $android = $apps.result | Where-Object { $_.platform -eq 'ANDROID' } | Select-Object -First 1
    if ($android) {
        $cfg = Invoke-FirebaseJson 'apps:sdkconfig' 'ANDROID' $android.appId
        $declared = $null
        if ($cfg -and $cfg.status -eq 'success') {
            try {
                $declared = ($cfg.result.fileContents | ConvertFrom-Json).client[0].client_info.android_client_info.package_name
            }
            catch {
                $declared = $null
            }
        }
        if (-not $declared) {
            Add-Finding 'apps' 'UNKNOWN' 'could not read the Android package name out of the SDK config'
        }
        elseif ($declared -eq $AndroidPackage) {
            Add-Finding 'apps' 'OK' "Android package name matches $AndroidPackage"
        }
        else {
            Add-Finding 'apps' 'MISSING' "Firebase has a different Android package name than $AndroidPackage"
        }
    }
}

# ------------------------------------------------------------------ rules ----
$rulesPath = Join-Path $firebaseDir 'database.rules.json'
if (Test-Path $rulesPath) {
    try {
        Get-Content $rulesPath -Raw | ConvertFrom-Json | Out-Null
        Add-Finding 'rules' 'OK' 'firebase/database.rules.json is present and parses'
    }
    catch {
        Add-Finding 'rules' 'MISSING' 'firebase/database.rules.json does not parse as JSON'
    }
}
else {
    Add-Finding 'rules' 'MISSING' 'firebase/database.rules.json is absent -- it IS the security boundary'
}
# Reading back what is deployed needs an admin credential this script deliberately does not hold.
# The CI job is the answer to drift: it deploys the same file it just tested, on every push to main.
Add-Finding 'rules' 'UNKNOWN' 'deployed-vs-local drift is not readable without an admin token; the rules CI job is what keeps them equal'

# ------------------------------------------------------- repository secrets ----
$expected = [ordered]@{
    'FIREBASE_CONFIG'           = @{ Required = $true;  Why = 'without it the web and Android builds ship with cloud play absent' }
    'FIREBASE_TOKEN'            = @{ Required = $true;  Why = 'without it the rules job TESTS the rules and does not DEPLOY them' }
    'ANDROID_KEYSTORE_BASE64'   = @{ Required = $false; Why = 'without it the aab job produces a debug-signed bundle Play will refuse' }
    'ANDROID_KEYSTORE_PASSWORD' = @{ Required = $false; Why = 'part of the upload-key set' }
    'ANDROID_KEY_ALIAS'         = @{ Required = $false; Why = 'part of the upload-key set' }
    'ANDROID_KEY_PASSWORD'      = @{ Required = $false; Why = 'part of the upload-key set' }
}
if (Test-Tool 'gh') {
    Push-Location $repoRoot
    try { $present = @(& gh secret list --json name -q '.[].name' 2>$null) } finally { Pop-Location }
    foreach ($name in $expected.Keys) {
        $meta = $expected[$name]
        if ($present -contains $name) {
            Add-Finding 'secrets' 'OK' "$name is set"
        }
        else {
            $state = if ($meta.Required) { 'MISSING' } else { 'TODO' }
            Add-Finding 'secrets' $state "$name is NOT set -- $($meta.Why)"
        }
    }
}

# ------------------------------------------------------------------- play ----
# Nothing here is scriptable before the listing exists: androidpublisher v3 has no
# applications.create, so the package name is claimed by hand exactly once.
Add-Finding 'play' 'TODO' 'the listing is created in the Play Console by hand -- the publishing API cannot create an app (#59)'

# ---------------------------------------------------------------- report -----
$order = @{ 'MISSING' = 0; 'TODO' = 1; 'UNKNOWN' = 2; 'OK' = 3 }
foreach ($group in ($findings | Group-Object Area)) {
    Write-Host ''
    Write-Host $group.Name.ToUpperInvariant() -ForegroundColor Cyan
    foreach ($f in ($group.Group | Sort-Object { $order[$_.State] })) {
        $colour = switch ($f.State) {
            'OK'      { 'Green' }
            'TODO'    { 'Yellow' }
            'UNKNOWN' { 'DarkGray' }
            default   { 'Red' }
        }
        Write-Host ('  {0,-8} {1}' -f $f.State, $f.Detail) -ForegroundColor $colour
    }
}

$missing = @($findings | Where-Object { $_.State -eq 'MISSING' })
$todo    = @($findings | Where-Object { $_.State -eq 'TODO' })
Write-Host ''
Write-Host ('-' * 72)
Write-Host ("{0} checks, {1} missing, {2} still to do" -f $findings.Count, $missing.Count, $todo.Count)
Write-Host ''
Write-Host 'What no script can do for you: docs/google-ops.md, "The irreducible clickops".' -ForegroundColor DarkGray
Write-Host ''

if ($Strict -and $missing.Count -gt 0) { exit 1 }
