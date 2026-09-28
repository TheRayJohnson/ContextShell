<#
.SYNOPSIS
  End-to-end installer test: upgrade from the previous release, repair, uninstall, and a
  msiexec install with custom properties. Runs in CI on a clean Windows runner (as admin).

.PARAMETER Dist
  Folder with setup-x64.exe and setup-x64.msi from build-installer.ps1.
#>
param(
	[Parameter(Mandatory)] [string]$Dist,
	[string]$Repository = 'TheRayJohnson/ContextShell',
	[string]$PreviousTag = 'v1.9.19'
)

$ErrorActionPreference = 'Stop'
$Dist = (Resolve-Path $Dist).Path
$setup = Join-Path $Dist 'setup-x64.exe'
$msi = Join-Path $Dist 'setup-x64.msi'
$logs = Join-Path $env:TEMP 'cs-test-logs'
New-Item -ItemType Directory -Force $logs | Out-Null

$wxi = Get-Content (Join-Path $PSScriptRoot 'wix\var.wxi') -Raw
$null = $wxi -match "Version\s*=\s*'([0-9.]+)'"
$version = $Matches[1]
$defaultFolder = Join-Path $env:ProgramFiles 'ContextShell'
$clsidKey = 'HKLM:\SOFTWARE\Classes\CLSID\{3F580C96-2A74-458D-8FBB-80DAC41FC5D1}'
$runKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$startMenuLnk = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\ContextShell.lnk'
$desktopLnk = Join-Path $env:PUBLIC 'Desktop\ContextShell.lnk'

function Products {
	@(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
		Get-ItemProperty | Where-Object { $_.DisplayName -eq 'ContextShell' })
}

function Check([bool]$condition, [string]$what) {
	if(-not $condition) { throw "FAILED: $what" }
	Write-Host "  ok  $what" -ForegroundColor Green
}

function Run([string]$exe, [string]$arguments, [int[]]$expected = @(0, 3010), [string]$log) {
	Write-Host "> $([IO.Path]::GetFileName($exe)) $arguments" -ForegroundColor Cyan
	$p = Start-Process $exe -ArgumentList $arguments -PassThru
	# Anything waiting on a hidden prompt would hang CI; fail with the log instead.
	if(-not $p.WaitForExit(300000)) {
		$p | Stop-Process -Force
		if($log -and (Test-Path $log)) { Get-Content $log -Tail 80 }
		throw "$exe $arguments timed out after 5 minutes"
	}
	if($p.ExitCode -notin $expected) {
		if($log -and (Test-Path $log)) { Get-Content $log -Tail 80 }
		throw "$exe $arguments exited with $($p.ExitCode)"
	}
	return $p.ExitCode
}

function LinkTarget([string]$lnk) { (New-Object -ComObject WScript.Shell).CreateShortcut($lnk).TargetPath }

function Assert-Installed([string]$folder, [string]$expectedVersion) {
	$p = Products
	Check ($p.Count -eq 1) "exactly one ContextShell entry in Apps (found $($p.Count))"
	Check ($p[0].DisplayVersion -eq $expectedVersion) "installed version is $expectedVersion (found $($p[0].DisplayVersion))"
	foreach($f in 'shell.dll', 'shell.exe', 'ContextShell.exe', 'shell.nss', 'imports\theme.nss', 'imports\themes\liquid-glass.nss') {
		Check (Test-Path (Join-Path $folder $f)) "$f installed"
	}
	Check (Test-Path $clsidKey) 'context menu handler registered'
	Check ((Get-ItemProperty 'HKLM:\SOFTWARE\TheRayJohnson\ContextShell\Setup').InstallFolder.TrimEnd('\') -eq $folder.TrimEnd('\')) 'install folder recorded'
}

try {
	# 1. Previous release, installed the old way.
	$old = Join-Path $env:TEMP 'cs-old'
	gh release download $PreviousTag -R $Repository -p '*-x64.msi' -D $old --clobber
	$oldMsi = Get-ChildItem $old -Filter '*-x64.msi' | Select-Object -First 1
	Write-Host "== Install $PreviousTag" -ForegroundColor Yellow
	Run 'msiexec.exe' "/i `"$($oldMsi.FullName)`" /qn /l*v `"$logs\old.log`"" -log "$logs\old.log" | Out-Null
	Check ((Products).Count -eq 1) "$PreviousTag installed"

	# 2. Upgrade with the new setup EXE.
	Write-Host "== Upgrade to $version with setup.exe /quiet" -ForegroundColor Yellow
	Run $setup "/quiet /log `"$logs\upgrade.log`"" -log "$logs\upgrade.log" | Out-Null
	Assert-Installed $defaultFolder $version
	Check ((Get-ItemProperty $runKey -ErrorAction SilentlyContinue).'ContextShell Updates' -like '*ContextShell.exe*--background*') 'sign-in update check registered'
	Check ((LinkTarget $startMenuLnk) -like '*\ContextShell.exe') 'Start menu shortcut opens ContextShell Settings'
	Check (-not (Test-Path $desktopLnk)) 'no desktop shortcut by default'

	# 3. Repair restores a deleted file.
	Write-Host '== Repair' -ForegroundColor Yellow
	Remove-Item (Join-Path $defaultFolder 'ContextShell.exe') -Force
	Run $setup "/repair /quiet /log `"$logs\repair.log`"" -log "$logs\repair.log" | Out-Null
	Check (Test-Path (Join-Path $defaultFolder 'ContextShell.exe')) 'repair restored ContextShell.exe'
	Check (Test-Path $clsidKey) 'still registered after repair'

	# 4. Running the same version again repairs instead of installing twice.
	Run $setup "/quiet /log `"$logs\same.log`"" -log "$logs\same.log" | Out-Null
	Check ((Products).Count -eq 1) 'same-version run keeps a single install'

	# 5. Uninstall.
	Write-Host '== Uninstall with setup.exe /uninstall /quiet' -ForegroundColor Yellow
	Run $setup "/uninstall /quiet /log `"$logs\uninstall.log`"" -log "$logs\uninstall.log" | Out-Null
	Check ((Products).Count -eq 0) 'removed from Apps'
	Check (-not (Test-Path (Join-Path $defaultFolder 'ContextShell.exe'))) 'files removed'
	Check (-not (Test-Path $clsidKey)) 'context menu handler unregistered'
	Check (-not (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).'ContextShell Updates') 'update check removed'
	Check (-not (Test-Path $startMenuLnk)) 'Start menu shortcut removed'
	$rc = Run $setup '/uninstall /quiet' -expected @(1605)
	Check ($rc -eq 1605) 'uninstall when not installed returns 1605'

	# 6. msiexec with custom properties (IT deployment path).
	Write-Host '== msiexec with INSTALLFOLDER, ADDDESKTOP, ADDSTARTMENU, AUTOUPDATE' -ForegroundColor Yellow
	$custom = 'C:\CS Test\ContextShell'
	Run 'msiexec.exe' "/i `"$msi`" /qn INSTALLFOLDER=`"$custom\`" ADDDESKTOP=1 ADDSTARTMENU=0 AUTOUPDATE=0 /l*v `"$logs\msiexec.log`"" -log "$logs\msiexec.log" | Out-Null
	Assert-Installed $custom $version
	Check (Test-Path $desktopLnk) 'desktop shortcut created'
	Check (-not (Test-Path $startMenuLnk)) 'no Start menu shortcut when ADDSTARTMENU=0'
	Check (-not (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).'ContextShell Updates') 'no update check when AUTOUPDATE=0'
	Check ((Products)[0].InstallLocation.TrimEnd('\') -eq $custom) 'install location shown in Apps'

	Run $setup "/uninstall /quiet /log `"$logs\uninstall2.log`"" -log "$logs\uninstall2.log" | Out-Null
	Check ((Products).Count -eq 0) 'setup.exe removes an msiexec install'
	Check (-not (Test-Path $desktopLnk)) 'desktop shortcut removed'

	# 7. Extract for admins.
	$extract = Join-Path $env:TEMP 'cs-extract'
	Run $setup "/extract `"$extract`"" | Out-Null
	Check (Test-Path (Join-Path $extract 'ContextShell.msi')) '/extract writes the MSI'

	Write-Host ''
	Write-Host 'All installer tests passed.' -ForegroundColor Green
}
catch {
	Write-Host $_ -ForegroundColor Red
	Get-ChildItem $logs -ErrorAction SilentlyContinue | ForEach-Object {
		Write-Host "---- $($_.Name) (last 40 lines)"
		Get-Content $_.FullName -Tail 40
	}
	exit 1
}
