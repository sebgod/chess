#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reports the Google-side state this repository depends on, and can create the parts that are
    creatable from a CLI.

.DESCRIPTION
    Everything chess needs from Google is one Firebase project on the Spark plan plus, later, a
    Play listing. The audit itself is SharpAstro/cloud-ops's (firebase/FirebaseOps.psm1), shared with
    every app that has a backend; what is chess's own -- the project, its region, the apps, the rules
    file, the repository secrets and the steps no API can take -- is cloud.json at the repository root.

    The default run is READ-ONLY and never prints an API key, a database URL or a secret value (the
    config is deliberately not in this repository: docs/correspondence-play.md, "Why the config is a
    secret"). -Provision creates what is missing and is safe to re-run.

    cloud-ops is found at ../../sharpastro/cloud-ops beside this checkout, or wherever CLOUD_OPS_ROOT says.

.EXAMPLE
    pwsh scripts/google-audit.ps1
    pwsh scripts/google-audit.ps1 -Provision
    pwsh scripts/google-audit.ps1 -Strict     # non-zero exit when something required is missing
#>
[CmdletBinding()]
param(
    [switch] $Provision,
    [switch] $Strict
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$cloudOps = if ($env:CLOUD_OPS_ROOT) { $env:CLOUD_OPS_ROOT } else { Join-Path $repoRoot '../../sharpastro/cloud-ops' }
$module   = Join-Path $cloudOps 'firebase/FirebaseOps.psm1'
if (-not (Test-Path $module)) {
    Write-Error "cloud-ops not found at ${cloudOps}: clone SharpAstro/cloud-ops there, or set CLOUD_OPS_ROOT"
}

Import-Module $module -Force
exit (Invoke-FirebaseAudit -Manifest (Join-Path $repoRoot 'cloud.json') -Provision:$Provision -Strict:$Strict)
