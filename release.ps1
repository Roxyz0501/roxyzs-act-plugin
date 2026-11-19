param([Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') {throw 'Invalid version'}
$dll=Get-Item (Join-Path $PSScriptRoot 'src/bin/Release/net48/RoxyzsActPlugin.dll')
$name=[Reflection.AssemblyName]::GetAssemblyName($dll.FullName)
if ($name.Name -ne 'RoxyzsActPlugin' -or $name.Version.ToString(3) -ne $Version) {throw 'DLL identity mismatch'}
$release=New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot release) -Force
$zip=Join-Path $release.FullName "RoxyzsActPlugin-v$Version.zip"
Compress-Archive -LiteralPath $dll.FullName,(Join-Path $PSScriptRoot README.md) -DestinationPath $zip -Force
$hash=(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256","$hash  "+[IO.Path]::GetFileName($zip)+[Environment]::NewLine)

