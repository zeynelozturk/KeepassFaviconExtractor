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
$projectFile = Join-Path $repoRoot 'KeepassFaviconExtractor.csproj'

if (-not (Test-Path $projectFile)) {
	throw "Project file not found: $projectFile"
}

[xml]$projectXml = Get-Content $projectFile
$projectNs = New-Object Xml.XmlNamespaceManager($projectXml.NameTable)
$projectNs.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
$compileIncludes = $projectXml.SelectNodes('//m:Compile[@Include]', $projectNs) |
	ForEach-Object { $_.GetAttribute('Include') } |
	Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
	Sort-Object -Unique

if ($compileIncludes.Count -eq 0) {
	throw 'No Compile Include items were found in KeepassFaviconExtractor.csproj.'
}

$sourceFiles = @((Join-Path $repoRoot 'plgx\FaviconExtractor.csproj'))

foreach ($include in $compileIncludes) {
	$sourceFiles += Join-Path $repoRoot $include
}

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

	foreach ($include in $compileIncludes) {
		$sourcePath = Join-Path $repoRoot $include
		$destinationPath = Join-Path $stageProjectDir $include
		$destinationDir = Split-Path $destinationPath -Parent
		New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
		Copy-Item $sourcePath $destinationPath
	}

	$stagePlgxProjectPath = Join-Path $stageProjectDir 'FaviconExtractor.csproj'
	Copy-Item (Join-Path $repoRoot 'plgx\FaviconExtractor.csproj') $stagePlgxProjectPath

	[xml]$plgxXml = Get-Content $stagePlgxProjectPath
	$plgxNs = New-Object Xml.XmlNamespaceManager($plgxXml.NameTable)
	$plgxNs.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
	$compileItemGroup = $plgxXml.SelectSingleNode('//m:ItemGroup[m:Compile]', $plgxNs)
	if ($compileItemGroup -eq $null) {
		$compileItemGroup = $plgxXml.CreateElement('ItemGroup', $plgxXml.DocumentElement.NamespaceURI)
		$null = $plgxXml.DocumentElement.AppendChild($compileItemGroup)
	}

	$existingCompileNodes = @($compileItemGroup.SelectNodes('m:Compile', $plgxNs))
	foreach ($node in $existingCompileNodes) {
		$null = $compileItemGroup.RemoveChild($node)
	}

	foreach ($include in $compileIncludes) {
		$compileNode = $plgxXml.CreateElement('Compile', $plgxXml.DocumentElement.NamespaceURI)
		$compileNode.SetAttribute('Include', $include)
		$null = $compileItemGroup.AppendChild($compileNode)
	}

	$settings = New-Object Xml.XmlWriterSettings
	$settings.Indent = $true
	$settings.IndentChars = "`t"
	$settings.NewLineChars = "`r`n"
	$settings.NewLineHandling = [Xml.NewLineHandling]::Replace
	$settings.Encoding = New-Object Text.UTF8Encoding($false)
	$writer = [Xml.XmlWriter]::Create($stagePlgxProjectPath, $settings)
	try {
		$plgxXml.Save($writer)
	}
	finally {
		$writer.Dispose()
	}

	$hintPaths = $plgxXml.SelectNodes('//m:Reference[m:HintPath]/m:HintPath', $plgxNs) |
		ForEach-Object { $_.InnerText } |
		Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
		Sort-Object -Unique

	foreach ($hintPath in $hintPaths) {
		$sourceDependencyPath = Join-Path $repoRoot $hintPath
		if (-not (Test-Path $sourceDependencyPath)) {
			throw "Referenced dependency not found: $sourceDependencyPath"
		}

		$stageDependencyPath = Join-Path $stageProjectDir $hintPath
		$stageDependencyDir = Split-Path $stageDependencyPath -Parent
		New-Item -ItemType Directory -Path $stageDependencyDir -Force | Out-Null
		Copy-Item $sourceDependencyPath $stageDependencyPath -Force
	}

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
