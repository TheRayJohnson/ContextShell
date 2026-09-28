<#
.SYNOPSIS
  Builds the ContextShell installer: Settings app -> MSI -> branded setup EXE.

.DESCRIPTION
  Run after the native projects are built (msbuild src/Shell.sln), which puts shell.dll,
  shell.exe and ca.dll into src/bin. This script then:
    1. builds ContextShell.exe (Settings, WPF) into src/bin
    2. signs the binaries (only when signing is configured, see below)
    3. builds src/bin/setup-<arch>.msi with WiX
    4. signs the MSI
    5. builds src/bin/setup-<arch>.exe, which embeds the MSI
    6. signs the setup EXE

  Code signing is optional. Set CS_SIGN_PFX (path to a .pfx) and CS_SIGN_PASSWORD, and
  optionally CS_SIGN_TIMESTAMP (default http://timestamp.digicert.com). signtool.exe comes
  from the Windows SDK.

.EXAMPLE
  msbuild /m /p:Configuration=release /p:Platform=x64 src/Shell.sln
  ./src/setup/build-installer.ps1 -Platform x64
#>
[CmdletBinding()]
param(
	[ValidateSet('x64', 'x86', 'arm64')]
	[string]$Platform = 'x64',
	[string]$Repository = $(if($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'TheRayJohnson/ContextShell' })
)

$ErrorActionPreference = 'Stop'
$src = Resolve-Path (Join-Path $PSScriptRoot '..')
$bin = Join-Path $src 'bin'
$app = Join-Path $src 'app'

# One version for everything: src/setup/wix/var.wxi.
$varWxi = Get-Content (Join-Path $PSScriptRoot 'wix\var.wxi') -Raw
if($varWxi -notmatch "Version\s*=\s*'([0-9.]+)'") { throw 'Version not found in var.wxi' }
$version = $Matches[1]
Write-Host "ContextShell $version ($Platform), updates from $Repository" -ForegroundColor Cyan

foreach($f in 'shell.dll', 'shell.exe', 'ca.dll') {
	if(-not (Test-Path (Join-Path $bin $f))) { throw "$f is missing from src/bin. Build src/Shell.sln for $Platform first." }
}

function Invoke-Sign([string[]]$files) {
	if(-not $env:CS_SIGN_PFX) { return }
	$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
		Sort-Object FullName -Descending | Select-Object -First 1
	if(-not $signtool) { throw 'signtool.exe not found. Install the Windows SDK.' }
	$ts = if($env:CS_SIGN_TIMESTAMP) { $env:CS_SIGN_TIMESTAMP } else { 'http://timestamp.digicert.com' }
	& $signtool.FullName sign /fd SHA256 /f $env:CS_SIGN_PFX /p $env:CS_SIGN_PASSWORD /tr $ts /td SHA256 /d 'ContextShell' @files
	if($LASTEXITCODE) { throw "signtool failed ($LASTEXITCODE)" }
}

function Invoke-DotNet([string[]]$arguments) {
	& dotnet @arguments
	if($LASTEXITCODE) { throw "dotnet $($arguments[0]) failed ($LASTEXITCODE)" }
}

# 1. Settings app
Write-Host '== ContextShell.exe (Settings)' -ForegroundColor Cyan
$settingsOut = Join-Path $app 'Settings\bin\Release'
Invoke-DotNet @('build', (Join-Path $app 'Settings\ContextShell.Settings.csproj'), '-c', 'Release', '-nologo',
	"-p:Version=$version", "-p:UpdateRepository=$Repository")
Copy-Item (Join-Path $settingsOut 'ContextShell.exe'), (Join-Path $settingsOut 'ContextShell.exe.config') $bin -Force

# 2. Sign what goes into the MSI
Invoke-Sign @((Join-Path $bin 'shell.dll'), (Join-Path $bin 'shell.exe'), (Join-Path $bin 'ContextShell.exe'), (Join-Path $bin 'ca.dll'))

# 3. MSI
Write-Host "== setup-$Platform.msi" -ForegroundColor Cyan
$wixPlatform = if($Platform -eq 'arm64') { 'ARM64' } else { $Platform }
Invoke-DotNet @('build', (Join-Path $PSScriptRoot 'wix\setup.wixproj'), '-c', 'Release', '-nologo', "-p:Platform=$wixPlatform")
$msi = Join-Path $bin "setup-$Platform.msi"
if(-not (Test-Path $msi)) { throw "$msi was not produced" }
Remove-Item (Join-Path $bin "setup-$Platform.wixpdb") -ErrorAction SilentlyContinue

# 4. Sign the MSI
Invoke-Sign @($msi)

# 5. Setup EXE with the MSI embedded
Write-Host "== setup-$Platform.exe" -ForegroundColor Cyan
$setupOut = Join-Path $app 'Setup\bin\Release'
Invoke-DotNet @('build', (Join-Path $app 'Setup\ContextShell.Setup.csproj'), '-c', 'Release', '-nologo',
	"-p:Version=$version", "-p:UpdateRepository=$Repository", "-p:MsiPath=$msi")
$setupExe = Join-Path $bin "setup-$Platform.exe"
Copy-Item (Join-Path $setupOut 'ContextShell-Setup.exe') $setupExe -Force

# 6. Sign the setup EXE
Invoke-Sign @($setupExe)

Write-Host ''
Write-Host 'Built:' -ForegroundColor Green
Get-Item $msi, $setupExe | ForEach-Object { '  {0,-24} {1,8:N0} KB' -f $_.Name, ($_.Length / 1KB) }
