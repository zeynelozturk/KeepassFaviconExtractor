[CmdletBinding()]
param(
	[string]$Configuration = 'Release',
	[string]$ProjectPath,
	[string]$OutputRoot,
	[string]$KeePassExePath,
	[switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptRoot)) {
	$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}

if ([string]::IsNullOrEmpty($ProjectPath)) {
	$ProjectPath = Join-Path $scriptRoot '..\\KeepassFaviconExtractor.csproj'
}

if ([string]::IsNullOrEmpty($OutputRoot)) {
	$OutputRoot = Join-Path $scriptRoot '..\\dist'
}

if ([string]::IsNullOrEmpty($KeePassExePath)) {
	$KeePassExePath = Join-Path $scriptRoot '..\\.deps\\KeePass\\2.61\\KeePass.exe'
}

function Get-MSBuildPath {
	$msbuild = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Source
	if ($msbuild) { return $msbuild }

	$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
	if (Test-Path $vswhere) {
		$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
		if ($msbuild) { return $msbuild }
	}

	throw 'MSBuild.exe not found.'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$projectFile = [IO.Path]::GetFullPath($ProjectPath)
$outputBase = [IO.Path]::GetFullPath($OutputRoot)
$buildOutput = Join-Path $repoRoot ("bin\$Configuration")
$dllOutDir = Join-Path $outputBase 'dll\FaviconExtractor'
$plgxOutDir = Join-Path $outputBase 'plgx'
$zipOutDir = Join-Path $outputBase 'zip'
$symbolsOutDir = Join-Path $outputBase 'symbols'

if (-not (Test-Path $projectFile)) {
	throw "Project file not found: $projectFile"
}

if (-not $SkipBuild) {
	$msbuild = Get-MSBuildPath
	& $msbuild $projectFile /t:Restore,Build /p:Configuration=$Configuration /nologo /verbosity:minimal
	if ($LASTEXITCODE -ne 0) {
		throw "MSBuild failed with exit code $LASTEXITCODE"
	}
}

if (-not (Test-Path $buildOutput)) {
	throw "Build output directory not found: $buildOutput"
}

if (Test-Path $outputBase) {
	Remove-Item $outputBase -Recurse -Force
}

New-Item -ItemType Directory -Path $dllOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $symbolsOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $plgxOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $zipOutDir -Force | Out-Null

$runtimeDlls = Get-ChildItem $buildOutput -File | Where-Object {
	$_.Extension -ieq '.dll' -and
	($_.Name -ne 'KeePass.exe')
}

$runtimePdbs = Get-ChildItem $buildOutput -File | Where-Object { $_.Extension -ieq '.pdb' }

if ($runtimeDlls.Count -eq 0) {
	throw "No runtime DLLs found in $buildOutput"
}

foreach ($file in $runtimeDlls) {
	Copy-Item $file.FullName (Join-Path $dllOutDir $file.Name)
}

foreach ($pdb in $runtimePdbs) {
	Copy-Item $pdb.FullName (Join-Path $symbolsOutDir $pdb.Name)
}

$nativeSourceDir = Join-Path $buildOutput 'native'
if (Test-Path $nativeSourceDir) {
	# Copy the full native folder to keep all native dependencies (e.g., libsharpyuv + libwebp).
	Copy-Item $nativeSourceDir (Join-Path $dllOutDir 'native') -Recurse -Force
}

$plgxPath = Join-Path $plgxOutDir 'FaviconExtractor.plgx'
& (Join-Path $scriptRoot 'build-plgx.ps1') -OutputPath $plgxPath -KeePassExePath $KeePassExePath
if (-not (Test-Path $plgxPath)) {
	throw 'PLGX packaging failed.'
}

$dllZip = Join-Path $zipOutDir "FaviconExtractor-$Configuration-dll.zip"
$plgxZip = Join-Path $zipOutDir 'FaviconExtractor-plgx.zip'
$hybridZip = Join-Path $zipOutDir "FaviconExtractor-$Configuration-hybrid.zip"

$hybridStage = Join-Path $outputBase '_hybrid-stage'
New-Item -ItemType Directory -Path (Join-Path $hybridStage 'dll') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $hybridStage 'plgx') -Force | Out-Null
Copy-Item (Join-Path $dllOutDir '*') (Join-Path $hybridStage 'dll') -Recurse -Force
Copy-Item $plgxPath (Join-Path $hybridStage 'plgx\FaviconExtractor.plgx') -Force

Compress-Archive -Path (Join-Path $dllOutDir '*') -DestinationPath $dllZip -Force
Compress-Archive -Path $plgxPath -DestinationPath $plgxZip -Force
Compress-Archive -Path (Join-Path $hybridStage '*') -DestinationPath $hybridZip -Force
Remove-Item $hybridStage -Recurse -Force

Write-Host "DLL package: $dllOutDir"
Write-Host "PLGX package: $plgxPath"
Write-Host "ZIP artifacts: $zipOutDir"
