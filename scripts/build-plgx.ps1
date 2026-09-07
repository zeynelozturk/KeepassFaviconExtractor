[CmdletBinding()]
param(
	[string]$OutputPath,
	[string]$KeePassExePath
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptRoot)) {
	$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}

if ([string]::IsNullOrEmpty($OutputPath)) {
	$OutputPath = Join-Path $scriptRoot '..\dist\FaviconExtractor.plgx'
}

if ([string]::IsNullOrEmpty($KeePassExePath)) {
	$KeePassExePath = Join-Path $scriptRoot '..\.deps\KeePass\2.61\KeePass.exe'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$outputFile = [IO.Path]::GetFullPath($OutputPath)
$outputDir = Split-Path $outputFile -Parent
$keepassExe = [IO.Path]::GetFullPath($KeePassExePath)

$sourceFiles = @(
	(Join-Path $repoRoot 'FaviconExtractorExt.cs'),
	(Join-Path $repoRoot 'Properties\AssemblyInfo.cs'),
	(Join-Path $repoRoot 'plgx\FaviconExtractor.csproj')
)

foreach ($file in $sourceFiles) {
	if (-not (Test-Path $file)) {
		throw "Required file not found: $file"
	}
}

if (-not (Test-Path $keepassExe)) {
	throw "KeePass.exe was not found at '$keepassExe'. Run scripts\\bootstrap-keepass.ps1 or pass -KeePassExePath."
}

$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ("FaviconExtractor-plgx-" + [Guid]::NewGuid().ToString('N'))
$stageProjectDir = Join-Path $stageRoot 'FaviconExtractor'
$stagePackagePath = "$stageProjectDir.plgx"

try {
	New-Item -ItemType Directory -Path $stageProjectDir | Out-Null
	New-Item -ItemType Directory -Path (Join-Path $stageProjectDir 'Properties') | Out-Null

	Copy-Item (Join-Path $repoRoot 'FaviconExtractorExt.cs') (Join-Path $stageProjectDir 'FaviconExtractorExt.cs')
	Copy-Item (Join-Path $repoRoot 'Properties\AssemblyInfo.cs') (Join-Path $stageProjectDir 'Properties\AssemblyInfo.cs')
	Copy-Item (Join-Path $repoRoot 'plgx\FaviconExtractor.csproj') (Join-Path $stageProjectDir 'FaviconExtractor.csproj')

	$keepassAssembly = [Reflection.Assembly]::LoadFrom($keepassExe)
	$plgxType = $keepassAssembly.GetType('KeePass.Plugins.PlgxPlugin', $true)
	$createMethod = $plgxType.GetMethod('CreateFromDirectory', [Reflection.BindingFlags]'NonPublic,Static', $null, [Type[]]@([string]), $null)
	if ($createMethod -eq $null) {
		throw 'KeePass PLGX generator method was not found.'
	}

	$null = $createMethod.Invoke($null, [object[]]@([string]$stageProjectDir))
	if (-not (Test-Path $stagePackagePath)) {
		throw "KeePass did not produce expected PLGX package '$stagePackagePath'."
	}

	New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

	if (Test-Path $outputFile) {
		Remove-Item $outputFile -Force
	}

	Move-Item -Path $stagePackagePath -Destination $outputFile

	Write-Host "PLGX created: $outputFile"
}
finally {
	if (Test-Path $stageRoot) {
		Remove-Item -Path $stageRoot -Recurse -Force
	}
}
