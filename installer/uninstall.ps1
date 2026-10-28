param([string]$GamePath, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
$exeName = 'Fears to Fathom - Ironbark Lookout.exe'
if (!$GamePath) { $GamePath = (Read-Host 'Game folder').Trim('"', ' ') }
if (!(Test-Path (Join-Path $GamePath $exeName))) { throw "Game executable not found: $GamePath" }
$full = (Resolve-Path $GamePath).Path
# A game under Program Files can only be changed as administrator: ask for it (the UAC prompt) and go on in that window.
$probe = Join-Path $GamePath ('ironbark-coop-write-test-' + [guid]::NewGuid().ToString('N'))
try { [IO.File]::WriteAllText($probe, ''); Remove-Item -LiteralPath $probe -Force }
catch {
    if ($Elevated) { throw "No write access to $GamePath even as administrator." }
    Write-Host 'The game folder needs administrator rights. Confirm the Windows prompt to continue.' -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList ('-NoExit -NoProfile -ExecutionPolicy Bypass -File "{0}" -GamePath "{1}" -Elevated' -f $PSCommandPath, $GamePath.TrimEnd('\'))
    return
}
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