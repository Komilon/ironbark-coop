param([string]$GamePath)
$ErrorActionPreference = 'Stop'
$exeName = 'Fears to Fathom - Ironbark Lookout.exe'
if (!$GamePath) { $GamePath = (Read-Host 'Game folder').Trim('"', ' ') }
if (!(Test-Path (Join-Path $GamePath $exeName))) { throw "Game executable not found: $GamePath" }
$full = (Resolve-Path $GamePath).Path
if (Get-Process -Name 'Fears to Fathom - Ironbark Lookout' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($full, [StringComparison]::OrdinalIgnoreCase) }) { throw 'Close the game first.' }

$mod = Join-Path $GamePath 'BepInEx\plugins\IronbarkCoop'
$config = Join-Path $GamePath 'BepInEx\config\astra.ironbark.coop.cfg'
Write-Host "Will remove:`n  $mod`n  $config"
if ((Read-Host 'Continue? (y/n)') -notmatch '^[yY]') { return }
Remove-Item -LiteralPath $mod -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $config -Force -ErrorAction SilentlyContinue
Write-Host 'Mod removed.' -ForegroundColor Green

# BepInEx may be used by other mods, so it is removed only on explicit request.
$loader = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt', 'BepInEx')
if ((Read-Host 'Also remove the BepInEx mod loader entirely (other mods will stop working too)? (y/n)') -match '^[yY]') {
    foreach ($item in $loader) { Remove-Item -LiteralPath (Join-Path $GamePath $item) -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host 'BepInEx removed.' -ForegroundColor Green
}
