param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $CompilerPath) { $CompilerPath = Join-Path $repoRoot 'tools/nsis-3.13/makensis.exe' }
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'NSIS 3.13 is required. See installer/BUILD.md.' }
$payloadDir = Join-Path $repoRoot 'dist/payload'
New-Item -ItemType Directory -Force $payloadDir | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath (Join-Path $payloadDir 'HOI4VoiceStudio.exe')
Copy-Item -LiteralPath (Join-Path $repoRoot 'HOI4VoiceStudio/使い方.txt') -Destination $payloadDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'distribution/配布案内.txt') -Destination $payloadDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.txt') -Destination $payloadDir -Force
New-Item -ItemType Directory -Force (Join-Path $payloadDir 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses/NSIS-COPYING.txt') -Destination (Join-Path $payloadDir 'licenses') -Force
Set-Content -LiteralPath (Join-Path $payloadDir 'installed.flag') -Value 'Unit Voice Studio 1.0.0' -Encoding ascii
& $CompilerPath /INPUTCHARSET UTF8 /V2 (Join-Path $repoRoot 'installer/UnitVoiceStudio.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Get-Item -LiteralPath (Join-Path $repoRoot 'dist/UnitVoiceStudio-Setup-1.0.0.exe') | Select-Object FullName,Length
