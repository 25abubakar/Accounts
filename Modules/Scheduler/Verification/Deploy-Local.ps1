$ErrorActionPreference = 'Stop'
$taskRoot = 'D:\Accounts'
$source = Join-Path $taskRoot '_dev\scratch\scheduler-integration-build'
$target = Join-Path $taskRoot 'Accounts\bin\Release\net9.0'
$executable = Join-Path $target 'Accounts.exe'
$running = @(Get-CimInstance Win32_Process -Filter "Name='Accounts.exe'")
if ($running.Count -ne 1 -or $running[0].ExecutablePath -ne $executable) { throw 'Unexpected Accounts process; deployment stopped.' }
$files = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in '.dll','.pdb' -or $_.Name -in 'Accounts.deps.json','Accounts.runtimeconfig.json' })
if (!(Test-Path (Join-Path $source 'Quartz.dll'))) { throw 'Scheduler dependencies missing.' }
$backup = Join-Path $taskRoot ('_dev\scratch\scheduler-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($file in $files) {
    $existing = Join-Path $target $file.Name
    if (Test-Path -LiteralPath $existing) { Copy-Item -LiteralPath $existing -Destination $backup }
}
Stop-Process -Id $running[0].ProcessId
Wait-Process -Id $running[0].ProcessId -ErrorAction SilentlyContinue
foreach ($file in $files) {
    $destination = Join-Path $target $file.Name
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw ('Copy mismatch: ' + $file.Name) }
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$logRoot = Join-Path $taskRoot 'Modules\Scheduler\Verification'
$started = Start-Process -FilePath $executable -ArgumentList '--urls','http://localhost:5099;https://localhost:7015' -WorkingDirectory (Join-Path $taskRoot 'Accounts') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot 'server.log') -RedirectStandardError (Join-Path $logRoot 'server-error.log') -PassThru
Write-Output ('DEPLOYED PID=' + $started.Id + '; Backup=' + $backup + '; Files=' + $files.Count)
