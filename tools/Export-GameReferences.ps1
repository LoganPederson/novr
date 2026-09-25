<#
.SYNOPSIS
    Builds reference-only copies of the Nuclear Option and BepInEx assemblies NOVR compiles against.

.DESCRIPTION
    CI runners don't have the game installed, and the game's assemblies can't be redistributed.
    This strips every method body and private member (JetBrains Refasmer), leaving only the public
    API signatures the compiler needs. A NOVR.dll built against the output is byte-identical to one
    built against the real game.

    The output folder has the same layout as a game install, so it can be passed straight to the
    build:  dotnet build -c Release -p:NuclearOptionGameDir=<OutDir>

    To use it in CI, push the output folder's contents to a private repository and set the
    GAME_REFS_REPO variable and GAME_REFS_SSH_KEY secret (a read-only deploy key; see .github/workflows/ci.yml).
    Re-run this after each game update.

.PARAMETER GameDir
    Nuclear Option install folder. Defaults to the Steam library that contains it.

.PARAMETER OutDir
    Where to write the reference assemblies. Defaults to ./game-refs (git-ignored).
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$OutDir = (Join-Path $PSScriptRoot '..\game-refs')
)

$ErrorActionPreference = 'Stop'

function Find-GameDir {
    $steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steamPath) { return $null }

    $libraries = @($steamPath)
    $vdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' |
            ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }

    foreach ($library in $libraries) {
        $candidate = Join-Path $library 'steamapps\common\Nuclear Option'
        if (Test-Path (Join-Path $candidate 'NuclearOption_Data\Managed')) { return $candidate }
    }
    return $null
}

if (-not $GameDir) { $GameDir = $env:NUCLEAR_OPTION_GAME_DIR }
if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'NuclearOption_Data\Managed'))) {
    throw 'Nuclear Option not found. Pass -GameDir "path\to\Nuclear Option".'
}
if (-not (Test-Path (Join-Path $GameDir 'BepInEx\core'))) {
    throw "BepInEx 5 is not installed in $GameDir."
}

$toolDir = Join-Path ([IO.Path]::GetTempPath()) 'novr-refasmer'
$refasmer = Join-Path $toolDir 'refasmer.exe'
if (-not (Test-Path $refasmer)) {
    Write-Host 'Installing JetBrains.Refasmer.CliTool...'
    dotnet tool install JetBrains.Refasmer.CliTool --tool-path $toolDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to install Refasmer.' }
}

$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }

$sets = @(
    @{ Source = 'NuclearOption_Data\Managed'; Target = 'NuclearOption_Data\Managed' },
    @{ Source = 'BepInEx\core'; Target = 'BepInEx\core' }
)
foreach ($set in $sets) {
    $source = Join-Path $GameDir $set.Source
    $target = Join-Path $OutDir $set.Target
    New-Item -ItemType Directory -Force $target | Out-Null
    Write-Host "Stripping $source"
    & $refasmer -q -c --omit-non-api-members=true -O $target (Get-ChildItem $source -Filter *.dll).FullName
    if ($LASTEXITCODE -ne 0) { throw "Refasmer failed on $source." }
}

$buildHash = Join-Path $GameDir 'build-hash.txt'
if (Test-Path $buildHash) { Copy-Item $buildHash (Join-Path $OutDir 'build-hash.txt') }

$count = (Get-ChildItem $OutDir -Recurse -Filter *.dll).Count
Write-Host "Wrote $count reference assemblies to $OutDir"
