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

function Get-AssemblyVersionFromAssemblyInfo {
	param(
		[string]$AssemblyInfoPath
	)

	if (-not (Test-Path $AssemblyInfoPath)) {
		throw "Assembly info file not found: $AssemblyInfoPath"
	}

	$content = Get-Content $AssemblyInfoPath -Raw
	$match = [regex]::Match($content, 'AssemblyVersion\("([^"]+)"\)')
	if (-not $match.Success) {
		throw "AssemblyVersion attribute not found in $AssemblyInfoPath"
	}

	return $match.Groups[1].Value
}

function Get-ProjectAssemblyName {
	param(
		[string]$ProjectFilePath
	)

	[xml]$projectXml = Get-Content $ProjectFilePath
	$projectNs = New-Object Xml.XmlNamespaceManager($projectXml.NameTable)
	$projectNs.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
	$assemblyNameNode = $projectXml.SelectSingleNode('/m:Project/m:PropertyGroup/m:AssemblyName', $projectNs)

	if ($assemblyNameNode -and -not [string]::IsNullOrWhiteSpace($assemblyNameNode.InnerText)) {
		return $assemblyNameNode.InnerText.Trim()
	}

	return [IO.Path]::GetFileNameWithoutExtension($ProjectFilePath)
}

function Resolve-LocalManagedDependencyClosure {
	param(
		[string]$BuildOutputPath,
		[string]$EntryAssemblyPath,
		[string[]]$ExcludeAssemblyNames = @()
	)

	$reflectionOnlyLoadMethod = [Reflection.Assembly].GetMethod('ReflectionOnlyLoadFrom', [Type[]]@([string]))
	if ($reflectionOnlyLoadMethod -eq $null) {
		throw 'Reflection-only assembly loading is not available in this PowerShell runtime.'
	}

	$exclude = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
	foreach ($name in $ExcludeAssemblyNames) {
		if (-not [string]::IsNullOrWhiteSpace($name)) {
			$null = $exclude.Add($name)
		}
	}

	$localDllByName = @{}
	Get-ChildItem $BuildOutputPath -File -Filter '*.dll' | ForEach-Object {
		$localDllByName[$_.BaseName] = $_.FullName
	}

	$entryAssemblyName = [IO.Path]::GetFileNameWithoutExtension($EntryAssemblyPath)
	if (-not $localDllByName.ContainsKey($entryAssemblyName)) {
		throw "Entry assembly '$entryAssemblyName' was not found in '$BuildOutputPath'."
	}

	$queue = New-Object 'System.Collections.Generic.Queue[string]'
	$visited = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
	$selectedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
	$queue.Enqueue($entryAssemblyName)

	while ($queue.Count -gt 0) {
		$assemblyName = $queue.Dequeue()
		if (-not $visited.Add($assemblyName)) {
			continue
		}

		if ($exclude.Contains($assemblyName)) {
			continue
		}

		if (-not $localDllByName.ContainsKey($assemblyName)) {
			continue
		}

		$assemblyPath = $localDllByName[$assemblyName]
		$null = $selectedPaths.Add($assemblyPath)

		try {
			$assembly = $reflectionOnlyLoadMethod.Invoke($null, [object[]]@($assemblyPath))
		}
		catch {
			throw "Failed to inspect assembly '$assemblyPath'. $($_.Exception.Message)"
		}

		foreach ($reference in $assembly.GetReferencedAssemblies()) {
			if (-not $visited.Contains($reference.Name)) {
				$queue.Enqueue($reference.Name)
			}
		}
	}

	return @($selectedPaths)
}

function Remove-PathWithRetry {
	param(
		[string]$Path,
		[int]$MaxAttempts = 5,
		[int]$DelayMilliseconds = 300
	)

	if (-not (Test-Path $Path)) {
		return
	}

	for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
		try {
			Remove-Item $Path -Recurse -Force -ErrorAction Stop
			return
		}
		catch {
			if ($attempt -eq $MaxAttempts) {
				throw "Failed to remove '$Path' after $MaxAttempts attempts. $($_.Exception.Message)"
			}

			Start-Sleep -Milliseconds $DelayMilliseconds
		}
	}
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$projectFile = [IO.Path]::GetFullPath($ProjectPath)
$outputBase = [IO.Path]::GetFullPath($OutputRoot)
$buildOutput = Join-Path $repoRoot ("bin\$Configuration")
$dllOutDir = Join-Path $outputBase 'dll\FaviconExtractor'
$plgxOutDir = Join-Path $outputBase 'plgx'
$zipOutDir = Join-Path $outputBase 'zip'
$symbolsOutDir = Join-Path $outputBase 'symbols'
$packageName = [IO.Path]::GetFileNameWithoutExtension($projectFile)
$assemblyInfoPath = Join-Path $repoRoot 'Properties\AssemblyInfo.cs'
$assemblyVersion = Get-AssemblyVersionFromAssemblyInfo $assemblyInfoPath
$projectAssemblyName = Get-ProjectAssemblyName $projectFile
$dllPackageFolderName = Split-Path $dllOutDir -Leaf

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
	Remove-PathWithRetry $outputBase
}

New-Item -ItemType Directory -Path $dllOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $symbolsOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $plgxOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $zipOutDir -Force | Out-Null


$allRuntimeDlls = Get-ChildItem $buildOutput -File | Where-Object {
	$_.Extension -ieq '.dll' -and
	($_.Name -ne 'KeePass.exe')
}

$runtimeDlls = $allRuntimeDlls
$entryAssemblyPath = Join-Path $buildOutput ($projectAssemblyName + '.dll')

if (Test-Path $entryAssemblyPath) {
	try {
		$resolvedDllPaths = Resolve-LocalManagedDependencyClosure -BuildOutputPath $buildOutput -EntryAssemblyPath $entryAssemblyPath -ExcludeAssemblyNames @('KeePass')
		if ($resolvedDllPaths.Count -gt 0) {
			$runtimeDlls = $resolvedDllPaths | ForEach-Object { Get-Item $_ }
			Write-Host "Resolved local managed dependency closure ($($runtimeDlls.Count) DLLs)."
		}
		else {
			Write-Warning 'Managed dependency closure resolved to zero DLLs. Falling back to full runtime DLL copy.'
		}
	}
	catch {
		Write-Warning "Managed dependency closure detection failed: $($_.Exception.Message) Falling back to full runtime DLL copy."
	}
}
else {
	Write-Warning "Entry assembly '$entryAssemblyPath' was not found. Falling back to full runtime DLL copy."
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

$dllZip = Join-Path $zipOutDir "$packageName-$assemblyVersion.zip"
$plgxZip = Join-Path $zipOutDir 'FaviconExtractor-plgx.zip'
$hybridZip = Join-Path $zipOutDir "FaviconExtractor-$Configuration-hybrid.zip"

$dllZipStage = Join-Path $outputBase '_dll-zip-stage'
$hybridStage = Join-Path $outputBase '_hybrid-stage'
New-Item -ItemType Directory -Path (Join-Path $dllZipStage $dllPackageFolderName) -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $hybridStage 'dll') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $hybridStage 'plgx') -Force | Out-Null
Copy-Item (Join-Path $dllOutDir '*') (Join-Path $dllZipStage $dllPackageFolderName) -Recurse -Force
Copy-Item (Join-Path $dllOutDir '*') (Join-Path $hybridStage 'dll') -Recurse -Force
Copy-Item $plgxPath (Join-Path $hybridStage 'plgx\FaviconExtractor.plgx') -Force

Compress-Archive -Path (Join-Path $dllZipStage '*') -DestinationPath $dllZip -Force
Compress-Archive -Path $plgxPath -DestinationPath $plgxZip -Force
Compress-Archive -Path (Join-Path $hybridStage '*') -DestinationPath $hybridZip -Force
Remove-PathWithRetry $dllZipStage
Remove-PathWithRetry $hybridStage

Write-Host "DLL package: $dllOutDir"
Write-Host "PLGX package: $plgxPath"
Write-Host "ZIP artifacts: $zipOutDir"
