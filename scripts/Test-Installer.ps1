param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$testDirectory = Join-Path $env:RUNNER_TEMP 'SCNexus-Installer-Test'
if (-not $env:RUNNER_TEMP) { throw 'Run this installation check on an isolated CI runner.' }
$dataDirectory = Join-Path $env:LOCALAPPDATA 'SCNexus'
New-Item -ItemType Directory -Force $dataDirectory | Out-Null
$marker = Join-Path $dataDirectory 'installer-preservation-check.txt'
$value = [guid]::NewGuid().ToString()
[IO.File]::WriteAllText($marker, $value)
1..2 | ForEach-Object {
    $testLanguage = if ($_ -eq 1) { 'english' } else { 'russian' }
    $result = Start-Process -FilePath (Resolve-Path $Installer) -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',"/LANG=$testLanguage","/DIR=`"$testDirectory`"" -Wait -PassThru -WindowStyle Hidden
    if ($result.ExitCode -ne 0) { throw "Installation failed: $($result.ExitCode)" }
    $exe = Join-Path $testDirectory 'SCNexus.exe'
    if (-not (Test-Path $exe)) { throw 'Installed executable is missing.' }
    if ([IO.File]::ReadAllText($marker) -ne $value) { throw 'Upgrade changed user data.' }
    if ((Get-ItemPropertyValue 'HKCU:\Software\SCNexus' -Name SetupLanguage) -ne $testLanguage) { throw 'Installer did not persist the selected language.' }
}
$uninstaller = Join-Path $testDirectory 'unins000.exe'
$result = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru -WindowStyle Hidden
if ($result.ExitCode -ne 0) { throw "Uninstallation failed: $($result.ExitCode)" }
if (Test-Path (Join-Path $testDirectory 'SCNexus.exe')) { throw 'Uninstallation left the application installed.' }
if ([IO.File]::ReadAllText($marker) -ne $value) { throw 'Uninstallation removed user data.' }
Write-Output 'Install, upgrade, uninstall, and user-data preservation passed.'
