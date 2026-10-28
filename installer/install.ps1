param([string]$GamePath, [switch]$Elevated)
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
                $candidates.Add([IO.Path]::Combine(($match.Groups[1].Value -replace '\\\\', '\'), 'steamapps\common\Fears to Fathom - Ironbark Lookout'))
            }
        }
    }
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem).Root) {
        foreach ($sub in 'Games\Fears to Fathom Ironbark Lookout', 'Games\Fears to Fathom - Ironbark Lookout', 'SteamLibrary\steamapps\common\Fears to Fathom - Ironbark Lookout') {
            $candidates.Add([IO.Path]::Combine($drive, $sub))
        }
    }
    # A library on a drive that is gone (a removed disk) must not stop the search.
    foreach ($path in $candidates) { if (Test-Path -LiteralPath ([IO.Path]::Combine($path, $exeName))) { return $path } }
    return $null
}

Write-Host 'Ironbark Coop __VERSION__ - install' -ForegroundColor Cyan
if (!$GamePath) { $GamePath = Find-Game }
if (!$GamePath -or !(Test-Path (Join-Path $GamePath $exeName))) {
    $GamePath = (Read-Host 'Game folder not found. Paste the path to the folder with "Fears to Fathom - Ironbark Lookout.exe"').Trim('"', ' ')
}
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