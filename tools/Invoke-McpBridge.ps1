<#
.SYNOPSIS
    Calls a tool on the NOVR dev bridge (NOVR.McpBridge) running inside the game.

.DESCRIPTION
    The bridge is off by default. Enable it with "Enabled = true" in
    BepInEx/config/deltawing.novr.mcpbridge.cfg and restart the game; it then writes its access token to
    that same file, which this script reads and sends in the X-NOVR-Token header.

.EXAMPLE
    ./tools/Invoke-McpBridge.ps1 -List
    ./tools/Invoke-McpBridge.ps1 get_player_state
    ./tools/Invoke-McpBridge.ps1 inspect_gameobject @{ name = 'targetDesignator' }
    ./tools/Invoke-McpBridge.ps1 get_scene_hierarchy @{ maxDepth = 3 }
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Tool,
    [Parameter(Position = 1)][hashtable]$Arguments = @{},
    [switch]$List,
    [string]$GameDir = $env:NUCLEAR_OPTION_GAME_DIR
)
$ErrorActionPreference = 'Stop'

if (-not $GameDir) {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steam) { $GameDir = Join-Path $steam 'steamapps\common\Nuclear Option' }
}
$config = Join-Path $GameDir 'BepInEx\config\deltawing.novr.mcpbridge.cfg'
if (-not (Test-Path $config)) { throw "Bridge config not found at $config. Start the game once with NOVR installed, or pass -GameDir." }

$settings = @{}
foreach ($line in Get-Content $config) {
    if ($line -match '^\s*([^#=]+?)\s*=\s*(.*)$') { $settings[$Matches[1]] = $Matches[2].Trim() }
}
if ($settings['Enabled'] -ne 'true') { throw "The bridge is disabled. Set 'Enabled = true' in $config and restart the game." }
if (-not $settings['Access Token']) { throw "No access token yet; the game writes one the first time the bridge starts." }

$port = if ($settings['Port']) { $settings['Port'] } else { 3334 }
$headers = @{ 'X-NOVR-Token' = $settings['Access Token'] }
$base = "http://localhost:$port"

if ($List -or -not $Tool) {
    Invoke-RestMethod "$base/tools" -Headers $headers | ForEach-Object { "{0,-24} {1}" -f $_.name, $_.description }
    return
}

$body = @{ tool = $Tool; args = $Arguments } | ConvertTo-Json -Depth 5 -Compress
(Invoke-RestMethod "$base/invoke" -Method Post -Headers $headers -Body $body -ContentType 'application/json').result
