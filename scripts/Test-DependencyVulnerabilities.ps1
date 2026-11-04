[CmdletBinding()]
param(
	[string] $ReportPath
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot '../Cya2.sln'
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
	$ReportPath = Join-Path $PSScriptRoot '../TestResults/dependency-vulnerabilities.json'
}
$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Force -Path (Split-Path $ReportPath -Parent) | Out-Null

# Force auditing of every dependency, including transitive packages and cached restores.
& dotnet restore $solution --force-evaluate '-p:NuGetAudit=true' '-p:NuGetAuditMode=all' '-p:NuGetAuditLevel=low' '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905'
$restoreExitCode = $LASTEXITCODE

$stderr = [System.IO.Path]::GetTempFileName()
try {
	$json = & dotnet package list --project $solution --vulnerable --include-transitive --format json --output-version 1 --no-restore --source https://api.nuget.org/v3/index.json 2> $stderr
	$auditExitCode = $LASTEXITCODE
	$json -join [Environment]::NewLine | Set-Content -LiteralPath $ReportPath -Encoding utf8
	if ($auditExitCode -ne 0 -or (Get-Item $stderr).Length -gt 0) {
		throw "NuGet vulnerability query failed. Inspect the SDK output and $ReportPath; do not treat this as a clean scan."
	}

	$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
	if ($report.version -ne 1 -or @($report.projects).Count -eq 0 -or @($report.problems).Where({ $null -ne $_ }).Count -gt 0) {
		throw 'NuGet returned an incomplete report or reported problems obtaining vulnerability data.'
	}

	$findings = @(
		foreach ($project in $report.projects) {
			foreach ($framework in $project.frameworks) {
				foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
					foreach ($vulnerability in $package.vulnerabilities) {
						[PSCustomObject]@{
							Project = [System.IO.Path]::GetFileName($project.path)
							Package = $package.id
							Version = $package.resolvedVersion
							Severity = $vulnerability.severity
							Advisory = $vulnerability.advisoryurl
						}
					}
				}
			}
		}
	)
	if ($findings.Count -gt 0) {
		$findings | Format-Table -AutoSize | Out-Host
		throw "Found $($findings.Count) NuGet vulnerability finding(s). Report: $ReportPath"
	}
	if ($restoreExitCode -ne 0) {
		throw 'Dependency restore/audit failed. Inspect the restore errors; the scan is not successful.'
	}
	Write-Host "No known NuGet vulnerabilities found in $(@($report.projects).Count) projects (direct and transitive dependencies). Report: $ReportPath"
}
finally {
	Remove-Item -LiteralPath $stderr -ErrorAction SilentlyContinue
}
