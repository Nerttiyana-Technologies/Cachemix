#!/usr/bin/env pwsh
# Restore, build, test, and pack Cachemix exactly as CI does.
#
# Usage: ./build/pack.ps1 [-Configuration Release] [-ArtifactsDir artifacts]

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $ArtifactsDir = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host '==> Restoring'
    dotnet restore Cachemix.slnx

    Write-Host "==> Building ($Configuration)"
    dotnet build Cachemix.slnx --configuration $Configuration --no-restore

    Write-Host '==> Testing'
    dotnet test Cachemix.slnx --configuration $Configuration --no-build

    Write-Host "==> Packing -> $ArtifactsDir"
    dotnet pack Cachemix.slnx --configuration $Configuration --no-build --output $ArtifactsDir

    Write-Host "==> Done. Packages written to: $ArtifactsDir"
}
finally {
    Pop-Location
}
