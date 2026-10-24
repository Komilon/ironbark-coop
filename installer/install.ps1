param([string]$GamePath)
$ErrorActionPreference = 'Stop'
$exeName = 'Fears to Fathom - Ironbark Lookout.exe'
$package = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Game {
    $candidates = New-Object System.Collections.Generic.List[string]
    # Steam libraries listed by the Steam client.
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($match in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $candidates.Add((Join-Path ($match.Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Fears to Fathom - Ironbark Lookout'))
            }
        }
    }
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem).Root) {
        foreach ($sub in 'Games\Fears to Fathom Ironbark Lookout', 'Games\Fears to Fathom - Ironbark Lookout', 'SteamLibrary\steamapps\common\Fears to Fathom - Ironbark Lookout') {
            $candidates.Add((Join-Path $drive $sub))
        }
    }
    foreach ($path in $candidates) { if (Test-Path (Join-Path $path $exeName)) { return $path } }
    return $null
}

Write-Host 'Ironbark Coop __VERSION__ - install' -ForegroundColor Cyan
if (!$GamePath) { $GamePath = Find-Game }
if (!$GamePath -or !(Test-Path (Join-Path $GamePath $exeName))) {
    $GamePath = (Read-Host 'Game folder not found. Paste the path to the folder with "Fears to Fathom - Ironbark Lookout.exe"').Trim('"', ' ')
}
if (!(Test-Path (Join-Path $GamePath $exeName))) { throw "Game executable not found: $GamePath" }
$full = (Resolve-Path $GamePath).Path
if (Get-Process -Name 'Fears to Fathom - Ironbark Lookout' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($full, [StringComparison]::OrdinalIgnoreCase) }) { throw 'Close the game first.' }

Write-Host "Game folder: $GamePath"
Copy-Item -Path (Join-Path $package 'game\*') -Destination $GamePath -Recurse -Force
$assembly = Join-Path $GamePath 'Fears to Fathom - Ironbark Lookout_Data\Managed\Assembly-CSharp.dll'
$hash = (Get-FileHash $assembly -Algorithm SHA256).Hash
Write-Host 'Installed.' -ForegroundColor Green
Write-Host ''
Write-Host "Game build code: $($hash.Substring(0, 12))" -ForegroundColor Yellow
Write-Host 'Compare it with your friend: it must match or the mod will refuse to connect.'
if ($hash -ne '3590DA9516B67E36E18112E725B165F316757F7D0F4140C4CDAA4E263B9E96B7') {
    Write-Host 'Note: this game build differs from the tested one; it may work but is not verified.' -ForegroundColor DarkYellow
}
Write-Host ''
Write-Host 'Start the game and open Multiplayer in the main menu.'
