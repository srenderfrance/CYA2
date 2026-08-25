[CmdletBinding()]
param(
	[Parameter(Mandatory = $false)]
	[string]$LogPath = ".\baseline.log",

	[Parameter(Mandatory = $false)]
	[string]$ResultsPath = ".\ui-cache-baseline.csv",

	[Parameter(Mandatory = $false)]
	[string]$Commit = "working-tree",

	[Parameter(Mandatory = $false)]
	[ValidateSet("before", "after", "manual")]
	[string]$Baseline = "manual"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Replace these placeholders when the test accounts and fundraising accounts are known.
$TestData = [ordered]@{
	AdminUser       = "saul.renderfrance@scene8.net"
	RegularUser     = "srendfrance@gmail.com"
	DefaultAccount  = "none>"
	AccountA        = "Renderfrance, Saul and Oliva, Soledad : RenderfranceSS12"
	AccountB        = "Payne Medina, Stephanie : PayneS13"
	AccountC        = "Niewoehner, Laurie and Will : NiewoehnerLW98"
	AccountD        = "Young, Dean and Paige : YoungDP00"
	AccountE        = "Helt, Dale : HeltD11"
	AccountF        = "Intern: Marcus Liu"
}

$Scenarios = @(
	[pscustomobject]@{ Id = "HOME-COLD"; Name = "Home initial load"; Mode = "cold"; Action = "Restart app, open Home once" }
	[pscustomobject]@{ Id = "DONATIONS-COLD"; Name = "Donations initial load"; Mode = "cold"; Action = "Restart app, open Donations once" }
	[pscustomobject]@{ Id = "EXPENSES-COLD"; Name = "Expenses initial load"; Mode = "cold"; Action = "Restart app, open Expenses once" }
	[pscustomobject]@{ Id = "DONORS-COLD"; Name = "Donors initial load"; Mode = "cold"; Action = "Restart app, open Donors once" }
	[pscustomobject]@{ Id = "NAVIGATION-WARM"; Name = "Repeated page navigation"; Mode = "warm"; Action = "Home -> Donations -> Expenses -> Donors -> Donations" }
	[pscustomobject]@{ Id = "ACCOUNT-SWITCH"; Name = "Account switching"; Mode = "warm"; Action = "Select Account A -> B -> A" }
	[pscustomobject]@{ Id = "ADMIN-STARTUP"; Name = "Admin startup"; Mode = "cold"; Action = "Restart app, open Admin" }
	[pscustomobject]@{ Id = "ADMIN-POPUPS"; Name = "Admin popup opening"; Mode = "warm"; Action = "Open each Admin popup twice" }
	[pscustomobject]@{ Id = "IMPORT-RECOVERY"; Name = "Import recovery"; Mode = "recovery"; Action = "Load summary in two circuits, import, reopen summary" }
	[pscustomobject]@{ Id = "ROLLBACK-RECOVERY"; Name = "Rollback recovery"; Mode = "recovery"; Action = "Load summary in two circuits, rollback, reopen summary" }
)

function Show-TestData {
	Write-Host "Test data placeholders:" -ForegroundColor Cyan
	$TestData.GetEnumerator() | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.Key, $_.Value) }
	Write-Host ""
}

function Show-Checklist {
	Write-Host "Manual UI cache baseline checklist" -ForegroundColor Green
	Write-Host "Baseline: $Baseline    Commit: $Commit"
	Write-Host "Log file: $LogPath"
	Write-Host ""
	foreach ($scenario in $Scenarios) {
		Write-Host ("[{0}] {1} ({2})" -f $scenario.Id, $scenario.Name, $scenario.Mode) -ForegroundColor Yellow
		Write-Host ("  Action: {0}" -f $scenario.Action)
		Write-Host "  Record the scenario ID, run number, and start/end timestamps in the CSV."
	}
	Write-Host ""
}

function Get-FirstMatch {
	param(
		[string]$Text,
		[string[]]$Patterns
	)

	foreach ($pattern in $Patterns) {
		$match = [regex]::Match($Text, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
		if ($match.Success) { return $match.Value }
	}
	return ""
}

function Get-MetricCount {
	param(
		[string[]]$Lines,
		[string[]]$Patterns
	)

	return @($Lines | Where-Object {
		$line = $_
		$Patterns | Where-Object { $line -match $_ } | Select-Object -First 1
	}).Count
}

function Get-LogMetrics {
	param([string]$Path)

	if (-not (Test-Path -LiteralPath $Path)) {
		Write-Warning "Log file was not found. The CSV template will still be created."
		return @()
	}

	$lines = Get-Content -LiteralPath $Path
	$groups = [ordered]@{}

	foreach ($line in $lines) {
		$operation = Get-FirstMatch -Text $line -Patterns @(
			'(?i)OperationId[=: ]+(?<value>[0-9a-f-]{8,})',
			'(?i)Operation[=: ]+(?<value>[A-Za-z0-9_-]+)'
		)
		$operation = if ($operation) { $operation } else { "unscoped" }

		if (-not $groups.Contains($operation)) { $groups[$operation] = [System.Collections.Generic.List[string]]::new() }
		$groups[$operation].Add($line)
	}

	foreach ($entry in $groups.GetEnumerator()) {
		$operationLines = $entry.Value.ToArray()
		$text = $operationLines -join "`n"
		[pscustomobject]@{
			OperationId    = $entry.Key
			Page           = Get-FirstMatch $text @('(?i)Page[=: ]+[^ ,;]+')
			CircuitId      = Get-FirstMatch $text @('(?i)CircuitId[=: ]+[^ ,;]+')
			ScopeId        = Get-FirstMatch $text @('(?i)ScopeId[=: ]+[^ ,;]+')
			SnapshotLoads  = Get-MetricCount $operationLines @('snapshot miss', 'snapshot load', 'Loaded account snapshot')
			SnapshotHits   = Get-MetricCount $operationLines @('snapshot.*hit', 'snapshot.*reuse', 'snapshot cache hit')
			CacheHits      = Get-MetricCount $operationLines @('cache hit', 'served from cache', 'cache source=cache')
			DatabaseLoads  = Get-MetricCount $operationLines @('source=db', 'repository', 'database', 'loaded from db', 'query')
			Invalidation   = Get-MetricCount $operationLines @('invalidat', 'cache generation')
			LogLines       = $operationLines.Count
		}
	}
}

Show-TestData
Show-Checklist

$metrics = @(Get-LogMetrics -Path $LogPath)
if ($metrics.Count -gt 0) {
	Write-Host "Parsed log metrics:" -ForegroundColor Cyan
	$metrics | Format-Table -AutoSize
}

if (-not (Test-Path -LiteralPath $ResultsPath)) {
	$header = "Baseline,Commit,ScenarioId,Scenario,Run,Mode,Account,User,StartUtc,EndUtc,DurationMs,DatabaseCalls,SnapshotLoads,SnapshotHits,CacheHits,Invalidations,Notes"
	Set-Content -LiteralPath $ResultsPath -Value $header -Encoding UTF8
	Write-Host "Created CSV template: $ResultsPath" -ForegroundColor Green
} else {
	Write-Host "CSV already exists; no rows were changed: $ResultsPath" -ForegroundColor Yellow
}

if ($metrics.Count -gt 0) {
	$metricsPath = [System.IO.Path]::ChangeExtension($ResultsPath, ".parsed-log.csv")
	$metrics | Export-Csv -LiteralPath $metricsPath -NoTypeInformation -Encoding UTF8
	Write-Host "Exported parsed log metrics: $metricsPath" -ForegroundColor Green
}

Write-Host "Next step: replace the placeholder test data, run each scenario five times, and fill one CSV row per run." -ForegroundColor Green
