<#
.SYNOPSIS
    Checks the native DLLs the patcher ships against the pinned hashes in NOVR.Patcher/CopyToGame/native-dlls.sha256.

.DESCRIPTION
    Those DLLs run as native code inside the game, so a changed binary must be a deliberate, reviewed update.
    Exits non-zero if any listed file is missing or its SHA-256 doesn't match, or if an unlisted DLL is present.
#>
$ErrorActionPreference = 'Stop'

$root = Join-Path $PSScriptRoot '..\NOVR.Patcher\CopyToGame'
$list = Join-Path $root 'native-dlls.sha256'
$failed = $false
$listed = @{}

foreach ($line in Get-Content $list) {
    if ($line -match '^\s*(#|$)') { continue }
    $hash, $relative = $line -split '\s+', 2
    $path = Join-Path $root $relative
    $listed[(Resolve-Path $path -ErrorAction SilentlyContinue).Path] = $true
    if (-not (Test-Path $path)) {
        Write-Host "MISSING  $relative"
        $failed = $true
        continue
    }
    $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $hash.ToLowerInvariant()) {
        Write-Host "CHANGED  $relative  expected $hash, got $actual"
        $failed = $true
    } else {
        Write-Host "OK       $relative"
    }
}

foreach ($dll in Get-ChildItem $root -Recurse -Filter *.dll) {
    if (-not $listed.ContainsKey($dll.FullName)) {
        Write-Host "UNLISTED $($dll.FullName.Substring($root.Length + 1))"
        $failed = $true
    }
}

if ($failed) {
    Write-Error 'Native DLLs do not match native-dlls.sha256. Verify any updated DLL against its official source and update the list.'
    exit 1
}
