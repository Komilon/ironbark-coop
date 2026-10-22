param([string]$GamePath)
$ErrorActionPreference = 'Stop'
$exeName = 'Fears to Fathom - Ironbark Lookout.exe'
if (!$GamePath) { $GamePath = (Read-Host 'Путь к папке игры / Game folder').Trim('"', ' ') }
if (!(Test-Path (Join-Path $GamePath $exeName))) { throw "Game executable not found: $GamePath" }
$full = (Resolve-Path $GamePath).Path
if (Get-Process -Name 'Fears to Fathom - Ironbark Lookout' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($full, [StringComparison]::OrdinalIgnoreCase) }) { throw 'Закройте игру / Close the game first.' }

$mod = Join-Path $GamePath 'BepInEx\plugins\IronbarkCoop'
$config = Join-Path $GamePath 'BepInEx\config\astra.ironbark.coop.cfg'
Write-Host "Будет удалено / Will remove:`n  $mod`n  $config"
if ((Read-Host 'Продолжить? / Continue? (y/n)') -notmatch '^[yYдД]') { return }
Remove-Item -LiteralPath $mod -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $config -Force -ErrorAction SilentlyContinue
Write-Host 'Мод удалён / Mod removed.' -ForegroundColor Green

# BepInEx may be used by other mods, so it is removed only on explicit request.
$loader = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt', 'BepInEx')
if ((Read-Host 'Удалить и загрузчик BepInEx целиком (другие моды тоже перестанут работать)? / Also remove BepInEx entirely? (y/n)') -match '^[yYдД]') {
    foreach ($item in $loader) { Remove-Item -LiteralPath (Join-Path $GamePath $item) -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host 'BepInEx удалён / BepInEx removed.' -ForegroundColor Green
}
