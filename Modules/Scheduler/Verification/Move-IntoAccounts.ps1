$ErrorActionPreference = 'Stop'
$root = 'D:\Accounts'
$old = Join-Path $root 'Modules\Scheduler'
$app = Join-Path $root 'Accounts'
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-Source([string]$relative, [string]$namespace, [string]$body, [string]$imports) {
    $path = Join-Path $app $relative
    if (Test-Path -LiteralPath $path) { throw "Destination exists: $path" }
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    [System.IO.File]::WriteAllText($path, "$imports`n`nnamespace $namespace;`n`n$($body.Trim())`n", $utf8)
}
function Section([string]$content,[string]$begin,[string]$end) {
    $start = $content.IndexOf($begin, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing section: $begin" }
    $finish = if ($end) { $content.IndexOf($end,$start+$begin.Length,[StringComparison]::Ordinal) } else { $content.Length }
    if ($finish -lt $start) { throw "Missing end: $end" }
    return $content.Substring($start,$finish-$start).Trim()
}
$models = [IO.File]::ReadAllText((Join-Path $old 'Models.cs'))
$modelNames = @('ScheduleDefinition','ScheduleRun','SchedulerCommand')
$ends = @('public sealed class ScheduleRun','public sealed class SchedulerCommand','public sealed class SchedulerDbContext')
for ($i=0;$i -lt $modelNames.Length;$i++) {
    Write-Source "Models\Scheduler\$($modelNames[$i]).cs" 'Accounts.Models.Scheduler' (Section $models "public sealed class $($modelNames[$i])" $ends[$i]) 'using System.ComponentModel.DataAnnotations;'
}
Write-Source 'Services\Scheduler\SchedulerTenant.cs' 'Accounts.Services.Scheduler' (Section $models 'public sealed class SchedulerTenant' 'public sealed class ScheduleDefinition') 'using System.Security.Claims;'
Write-Source 'Data\SchedulerDbContext.cs' 'Accounts.Data' (Section $models 'public sealed class SchedulerDbContext' 'public sealed class SchedulerContextFactory') "using Accounts.Models.Scheduler;`nusing Accounts.Services.Scheduler;`nusing Microsoft.EntityFrameworkCore;"
Write-Source 'Data\SchedulerContextFactory.cs' 'Accounts.Data' (Section $models 'public sealed class SchedulerContextFactory' 'public sealed record ScheduleEdit') "using Accounts.Services.Scheduler;`nusing Microsoft.EntityFrameworkCore;`nusing Microsoft.EntityFrameworkCore.Design;"
Write-Source 'DTOs\Scheduler\ScheduleEdit.cs' 'Accounts.DTOs.Scheduler' (Section $models 'public sealed record ScheduleEdit' '') ''
$jobs = [IO.File]::ReadAllText((Join-Path $old 'ModuleJobs.cs'))
Write-Source 'Models\Scheduler\ModuleJobInfo.cs' 'Accounts.Models.Scheduler' (Section $jobs 'public sealed record ModuleJobInfo' 'public interface IModuleJob') ''
Write-Source 'Services\Interfaces\IModuleJob.cs' 'Accounts.Services.Interfaces' (Section $jobs 'public interface IModuleJob' 'public sealed class SchedulerHealthJob') 'using Accounts.Models.Scheduler;'
$serviceImports = "using Accounts.Data;`nusing Accounts.Models.Scheduler;`nusing Accounts.Services.Interfaces;`nusing Microsoft.Data.SqlClient;`nusing Microsoft.EntityFrameworkCore;"
$jobNames = @('SchedulerHealthJob','BusinessReadConnection','AttendanceStatusCheck','AssessmentCycleCheck')
for ($i=0;$i -lt $jobNames.Length;$i++) {
    $end = if ($i+1 -lt $jobNames.Length) { "public sealed class $($jobNames[$i+1])" } else { '' }
    Write-Source "Services\Scheduler\$($jobNames[$i]).cs" 'Accounts.Services.Scheduler' (Section $jobs "public sealed class $($jobNames[$i])" $end) $serviceImports
}
$engine = [IO.File]::ReadAllText((Join-Path $old 'SchedulerEngine.cs'))
Write-Source 'Services\Scheduler\ScheduleSynchronizer.cs' 'Accounts.Services.Scheduler' (Section $engine 'public sealed class ScheduleSynchronizer' '[DisallowConcurrentExecution]') "$serviceImports`nusing Quartz;"
Write-Source 'Services\Scheduler\ModuleJobDispatcher.cs' 'Accounts.Services.Scheduler' (Section $engine '[DisallowConcurrentExecution]' '') "$serviceImports`nusing Quartz;"
$timing = [IO.File]::ReadAllText((Join-Path $old 'ScheduleTiming.cs'))
Write-Source 'Services\Scheduler\ScheduleTiming.cs' 'Accounts.Services.Scheduler' (Section $timing 'public static class ScheduleTiming' '') "using Accounts.Models.Scheduler;`nusing Accounts.DTOs.Scheduler;`nusing Quartz;"
$controller = [IO.File]::ReadAllText((Join-Path $old 'SchedulerController.cs'))
$controller = $controller.Replace('namespace Accounts.Scheduler;', 'namespace Accounts.Controllers;')
[IO.File]::WriteAllText((Join-Path $app 'Controllers\SchedulerController.cs'), "using Accounts.Authorization;`nusing Accounts.Data;`nusing Accounts.DTOs.Scheduler;`nusing Accounts.Models.Scheduler;`nusing Accounts.Services.Interfaces;`nusing Accounts.Services.Scheduler;`n"+$controller, $utf8)
$module = [IO.File]::ReadAllText((Join-Path $old 'SchedulerModule.cs'))
Write-Source 'Models\Scheduler\SchedulerStorage.cs' 'Accounts.Models.Scheduler' (Section $module 'public sealed record SchedulerStorage' 'public static class SchedulerModule') ''
$registration = Section $module 'public static class SchedulerModule' 'public sealed class SchedulerAccessFilter'
$registration = $registration.Replace('services.AddControllers().AddApplicationPart(typeof(SchedulerController).Assembly);','// Controllers are discovered in the existing Accounts API assembly.')
Write-Source 'Services\Scheduler\SchedulerModule.cs' 'Accounts.Services.Scheduler' $registration "$serviceImports`nusing Accounts.Authorization;`nusing Quartz;"
Write-Source 'Authorization\SchedulerAccessFilter.cs' 'Accounts.Authorization' (Section $module 'public sealed class SchedulerAccessFilter' '') "using Accounts.Models.Scheduler;`nusing Accounts.Services.Scheduler;`nusing Microsoft.AspNetCore.Mvc;`nusing Microsoft.AspNetCore.Mvc.Filters;`nusing Microsoft.AspNetCore.Mvc.Controllers;`nusing Microsoft.Data.SqlClient;`nusing System.Security.Claims;"
$migrationTarget = Join-Path $app 'Migrations\Scheduler'
[IO.Directory]::CreateDirectory($migrationTarget) | Out-Null
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $old 'Migrations') -File -Filter '*.cs') {
    $text = [IO.File]::ReadAllText($file.FullName).Replace('using Accounts.Scheduler;', 'using Accounts.Data;').Replace('namespace Accounts.Scheduler.Migrations', 'namespace Accounts.Migrations.Scheduler')
    foreach ($name in $modelNames) { $text=$text.Replace("Accounts.Scheduler.$name", "Accounts.Models.Scheduler.$name") }
    [IO.File]::WriteAllText((Join-Path $migrationTarget $file.Name), $text, $utf8)
    Remove-Item -LiteralPath $file.FullName
}
$sqlTarget = Join-Path $app 'Migrations\Scheduler\Sql'
[IO.Directory]::CreateDirectory($sqlTarget) | Out-Null
Move-Item -LiteralPath (Join-Path $old 'Migrations\Sql\Quartz.SqlServer.3.22.0.sql') -Destination $sqlTarget
foreach ($name in @('Models.cs','ModuleJobs.cs','SchedulerEngine.cs','ScheduleTiming.cs','SchedulerController.cs','SchedulerModule.cs','GlobalUsings.cs','Accounts.Scheduler.csproj')) {
    Remove-Item -LiteralPath (Join-Path $old $name)
}
Write-Output 'Scheduler sources moved into the existing Accounts project folders.'
