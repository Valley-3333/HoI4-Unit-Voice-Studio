param([string]$TestRoot)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testBoundary = [IO.Path]::GetFullPath((Join-Path $repoRoot 'tests')) + [IO.Path]::DirectorySeparatorChar
if (-not $TestRoot) { $TestRoot = Join-Path $repoRoot ('tests/run-installer-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$TestRoot = [IO.Path]::GetFullPath($TestRoot)
if (-not $TestRoot.StartsWith($testBoundary,[StringComparison]::OrdinalIgnoreCase)) { throw 'Installer tests must stay inside the workspace tests directory.' }
if (Test-Path -LiteralPath $TestRoot) { throw 'Use a fresh test directory.' }
New-Item -ItemType Directory -Path $TestRoot | Out-Null
$installRoot = Join-Path $TestRoot 'インストール先 with spaces'
$userRoot = Join-Path $TestRoot 'OneDrive - テスト/ドキュメント/Paradox Interactive/Hearts of Iron IV'
$installerPath = Join-Path $repoRoot 'dist/HoI4-Unit-Voice-Studio-Setup-1.0.0.exe'
$results = [Collections.Generic.List[string]]::new()
function Assert-Test([bool]$Condition,[string]$Label) { if (-not $Condition) { throw "FAIL: $Label" }; $results.Add("PASS: $Label") }
function Install-Test {
    $run = Start-Process -FilePath $installerPath -ArgumentList ('/S /TESTMODE /D=' + $installRoot) -WindowStyle Hidden -Wait -PassThru
    Assert-Test ($run.ExitCode -eq 0) 'Installer exits successfully'
}
Install-Test
$appPath = Join-Path $installRoot 'HOI4VoiceStudio.exe'
Assert-Test (Test-Path -LiteralPath $appPath) 'Installer extracts the actual application'
Assert-Test ((Get-Item -LiteralPath $appPath).VersionInfo.FileVersion -eq '1.0.0.0') 'Installed executable reports version 1.0.0'
Assert-Test ((Get-Item -LiteralPath $appPath).VersionInfo.CompanyName -eq 'Valley') 'Installed executable identifies Valley'
Assert-Test ((Get-Item -LiteralPath $appPath).VersionInfo.ProductName -eq 'HoI4 Unit Voice Studio') 'Installed executable uses full product name'
foreach ($relativeFile in @('配布案内.txt','THIRD_PARTY_NOTICES.txt','licenses/NSIS-COPYING.txt')) {
    Assert-Test (Test-Path -LiteralPath (Join-Path $installRoot $relativeFile)) ("Installer includes " + $relativeFile)
}
Assert-Test (-not (Test-Path -LiteralPath (Join-Path $installRoot '利用条件案.txt'))) 'No application terms draft is installed'
$noticeSource = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'licenses/NSIS-COPYING.txt') -Algorithm SHA256).Hash
Assert-Test ((Get-FileHash -LiteralPath (Join-Path $installRoot 'licenses/NSIS-COPYING.txt') -Algorithm SHA256).Hash -eq $noticeSource) 'Third-party license text is unchanged'
Assert-Test (-not (Test-Path -LiteralPath (Join-Path $installRoot 'data'))) 'Installer includes no user recordings, catalog or personal configuration'
New-Item -ItemType Directory -Force (Join-Path $installRoot 'data/takes'),(Join-Path $installRoot 'UnitVoiceMod/sound'),(Join-Path $userRoot 'mod') | Out-Null
Set-Content -LiteralPath (Join-Path $userRoot 'settings.txt') -Value '# isolated installer test'
$waveStream = [IO.MemoryStream]::new()
$waveWriter = [IO.BinaryWriter]::new($waveStream)
$waveWriter.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $waveWriter.Write([int]8856)
$waveWriter.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $waveWriter.Write([int]16)
$waveWriter.Write([int16]1); $waveWriter.Write([int16]1); $waveWriter.Write([int]44100); $waveWriter.Write([int]88200); $waveWriter.Write([int16]2); $waveWriter.Write([int16]16)
$waveWriter.Write([Text.Encoding]::ASCII.GetBytes('data')); $waveWriter.Write([int]8820); $waveWriter.Write([byte[]]::new(8820))
$keepPath = Join-Path $installRoot 'data/takes/keep.wav'
[IO.File]::WriteAllBytes($keepPath,$waveStream.ToArray()); $waveWriter.Dispose(); $waveStream.Dispose()
Copy-Item -LiteralPath $keepPath -Destination (Join-Path $installRoot 'UnitVoiceMod/sound/keep.wav')
Set-Content -LiteralPath (Join-Path $installRoot 'data/replacements.json') -Value '[]' -Encoding utf8
@{ UserDataPath=$userRoot; UserDataManual=$true; InitialSetupComplete=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installRoot 'data/setup.json') -Encoding utf8
Set-Content -LiteralPath (Join-Path $installRoot 'UnitVoiceMod/descriptor.mod') -Value 'name="HoI4 Unit Voice Studio"' -Encoding utf8
$descriptorPath = Join-Path $userRoot 'mod/UnitVoiceStudio.mod'
$modPath = (Join-Path $installRoot 'UnitVoiceMod').Replace('\','/')
[IO.File]::WriteAllText($descriptorPath,('name="HoI4 Unit Voice Studio"' + "`n" + 'path="' + $modPath + '"'),[Text.UTF8Encoding]::new($false))
$neighborPath = Join-Path $userRoot 'mod/OtherMod.mod'
Set-Content -LiteralPath $neighborPath -Value 'name="Keep Other Mod"'
$otherNoticePath = Join-Path $installRoot 'licenses/keep-user-file.txt'
Set-Content -LiteralPath $otherNoticePath -Value 'Keep user file'
$takeHash = (Get-FileHash -LiteralPath $keepPath -Algorithm SHA256).Hash
$settingsHash = (Get-FileHash -LiteralPath (Join-Path $installRoot 'data/setup.json') -Algorithm SHA256).Hash
Install-Test
Assert-Test ((Get-FileHash -LiteralPath $keepPath -Algorithm SHA256).Hash -eq $takeHash) 'Upgrade preserves recorded audio bytes'
Assert-Test ((Get-FileHash -LiteralPath (Join-Path $installRoot 'data/setup.json') -Algorithm SHA256).Hash -eq $settingsHash) 'Upgrade preserves setup configuration'
Assert-Test (Test-Path -LiteralPath (Join-Path $installRoot 'UnitVoiceMod/sound/keep.wav')) 'Upgrade preserves generated Mod audio'
$uninstallRun = Start-Process -FilePath (Join-Path $installRoot 'Uninstall.exe') -ArgumentList '/S' -WindowStyle Hidden -Wait -PassThru
Assert-Test ($uninstallRun.ExitCode -eq 0) 'Uninstaller exits successfully'
Assert-Test (-not (Test-Path -LiteralPath $appPath)) 'Uninstaller removes application binary'
Assert-Test (-not (Test-Path -LiteralPath $descriptorPath)) 'Uninstaller removes only its own launcher registration'
Assert-Test (-not (Test-Path -LiteralPath (Join-Path $installRoot 'licenses/NSIS-COPYING.txt'))) 'Uninstaller removes its installed notice'
Assert-Test (Test-Path -LiteralPath $otherNoticePath) 'Uninstaller preserves unrelated files in the notices folder'
Assert-Test (Test-Path -LiteralPath $neighborPath) 'Uninstaller preserves another Mod registration'
Assert-Test ((Get-FileHash -LiteralPath $keepPath -Algorithm SHA256).Hash -eq $takeHash) 'Uninstaller preserves recorded audio bytes'
Assert-Test (Test-Path -LiteralPath (Join-Path $installRoot 'UnitVoiceMod/sound/keep.wav')) 'Uninstaller preserves Mod files'
Install-Test
Assert-Test ((Get-FileHash -LiteralPath $keepPath -Algorithm SHA256).Hash -eq $takeHash) 'Reinstallation retains previous recording history'
$results.Add('INFO: Isolated /TESTMODE skips real Start Menu and uninstall-registry entries.')
$results | Set-Content -LiteralPath (Join-Path $TestRoot 'installer-results.txt') -Encoding utf8
$results
Write-Output "Test root: $TestRoot"
