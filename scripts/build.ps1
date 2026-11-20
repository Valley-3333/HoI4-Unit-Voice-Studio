param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$outputDir = Join-Path $repoRoot 'HOI4VoiceStudio'
if (-not $OutputPath) { $OutputPath = Join-Path $outputDir 'HOI4VoiceStudio.exe' }
New-Item -ItemType Directory -Force $outputDir | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ("/out:" + $OutputPath) $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $OutputPath
