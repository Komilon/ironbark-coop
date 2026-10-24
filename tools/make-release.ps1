param(
    [Parameter(Mandatory)][string]$Version,
    # An unpacked official BepInEx 5.4.23.5 x64 release (winhttp.dll, doorstop_config.ini, BepInEx\core\...).
    [string]$LoaderDir
)
$ErrorActionPreference = 'Stop'
# Assembles dist/IronbarkCoop-<version>.zip from the current Release build (dotnet build src -c Release).
$repo = Split-Path $PSScriptRoot
if (!$LoaderDir) { $LoaderDir = Join-Path $repo '..\..\work\loader' }
$bin = Join-Path $repo 'src\bin\Release\netstandard2.1'
$rel = Join-Path $repo "dist\IronbarkCoop-$Version"
if (!(Test-Path (Join-Path $LoaderDir 'winhttp.dll'))) { throw "BepInEx not found in $LoaderDir (pass -LoaderDir)" }
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $rel 'game\BepInEx\plugins\IronbarkCoop'), (Join-Path $rel 'licenses') | Out-Null
Copy-Item (Join-Path $LoaderDir '*') (Join-Path $rel 'game') -Recurse -Force
$files = 'IronbarkCoop.dll','Concentus.dll','System.Buffers.dll','System.Memory.dll','System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll','AssetsTools.NET.dll','classdata.tpk'
foreach ($file in $files) { Copy-Item (Join-Path $bin $file) (Join-Path $rel "game\BepInEx\plugins\IronbarkCoop\$file") }
foreach ($file in 'install.ps1','uninstall.ps1','install.bat','uninstall.bat') { Copy-Item (Join-Path $repo "installer\$file") (Join-Path $rel $file) }
Copy-Item (Join-Path $repo 'licenses\*') (Join-Path $rel 'licenses')
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $rel 'licenses\IronbarkCoop-LICENSE.txt')
Copy-Item (Join-Path $repo 'README.md'), (Join-Path $repo 'CHANGELOG.md') $rel
# The installer prints the version it installs.
$path = Join-Path $rel 'install.ps1'
$text = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8).Replace('__VERSION__', $Version)
[IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($true)))
# Lint: nothing from either game, no decompiled sources, no lab or Scratch Creek cache may ship.
$bad = Get-ChildItem $rel -Recurse -File | Where-Object { $_.Name -match '^(Assembly-CSharp|UnityEngine|Unity\.|Facepunch|Newtonsoft|DOTween)' -or $_.Extension -in '.cs','.pdb','.json','.png','.log','.bin','.assets','.resS' -or $_.FullName -match 'decompiled|scratch-|sc-cache' }
if ($bad) { throw "Release lint failed: $($bad.Name -join ', ')" }
Get-ChildItem $rel -Recurse -File | Where-Object Name -ne 'SHA256-manifest.txt' |
    ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16), $_.FullName.Substring($rel.Length + 1) } |
    Set-Content (Join-Path $rel 'licenses\SHA256-manifest.txt') -Encoding utf8
$zip = "$rel.zip"
Compress-Archive -Path "$rel\*" -DestinationPath $zip -Force
"{0:N0} KB  {1}" -f ((Get-Item $zip).Length / 1KB), $zip
# The one-window installer: the same game\ files inside an exe, compiled with the C# compiler that ships with
# Windows (.NET Framework 4), so nothing has to be installed to build or run it.
$build = Join-Path $repo "dist\setup-build"
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
New-Item -ItemType Directory -Force $build | Out-Null
Compress-Archive -Path (Join-Path $rel 'game\*') -DestinationPath (Join-Path $build 'payload.zip') -Force
Set-Content (Join-Path $build 'Version.cs') "namespace IronbarkCoopSetup { static class Version { public const string Value = `"$Version`"; } }" -Encoding utf8
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $repo "dist\IronbarkCoop-Setup-$Version.exe"
& $csc -nologo -target:winexe "-out:$exe" -codepage:65001 -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll `
    "-win32manifest:$(Join-Path $repo 'installer\setup.manifest')" "-resource:$(Join-Path $build 'payload.zip'),payload.zip" `
    (Join-Path $repo 'installer\Setup.cs') (Join-Path $build 'Version.cs')
if ($LASTEXITCODE -ne 0) { throw 'Setup exe build failed' }
Remove-Item $build -Recurse -Force
"{0:N0} KB  {1}" -f ((Get-Item $exe).Length / 1KB), $exe
