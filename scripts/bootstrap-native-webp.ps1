[CmdletBinding()]
param(
	[string]$Version = '1.6.1',
	[string]$DestinationRoot = (Join-Path $PSScriptRoot '..\native'),
	[switch]$Force
)

$ErrorActionPreference = 'Stop'

function Download-Package {
	param(
		[string]$PackageId,
		[string]$Version,
		[string]$OutFile
	)

	$lowerId = $PackageId.ToLowerInvariant()
	$lowerVersion = $Version.ToLowerInvariant()
	$url = "https://api.nuget.org/v3-flatcontainer/$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg"

	Write-Host "Downloading $PackageId $Version..."
	Invoke-WebRequest -Uri $url -OutFile $OutFile -UseBasicParsing -UserAgent 'curl/8.0'
}

function Extract-LibwebpDll {
	param(
		[string]$PackagePath,
		[string]$RuntimeFolder,
		[string]$TargetPath
	)

	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$zip = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
	try {
		$entryPath = "runtimes/$RuntimeFolder/native/libwebp.dll"
		$entry = $zip.Entries | Where-Object { $_.FullName -eq $entryPath } | Select-Object -First 1
		if (-not $entry) {
			throw "Entry '$entryPath' not found in package '$PackagePath'."
		}

		$targetDir = Split-Path -Parent $TargetPath
		New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
		if ((Test-Path $TargetPath) -and -not $Force) {
			Write-Host "Already present: $TargetPath"
			return
		}

		if (Test-Path $TargetPath) {
			Remove-Item -Path $TargetPath -Force
		}

		$stream = $entry.Open()
		try {
			$fileStream = [System.IO.File]::Open($TargetPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
			try {
				$stream.CopyTo($fileStream)
			}
			finally {
				$fileStream.Dispose()
			}
		}
		finally {
			$stream.Dispose()
		}

		Write-Host "Installed: $TargetPath"
	}
	finally {
		$zip.Dispose()
	}
}

$destination = [IO.Path]::GetFullPath($DestinationRoot)
$x86Target = Join-Path $destination 'x86\libwebp.dll'
$x64Target = Join-Path $destination 'x64\libwebp.dll'

if ((Test-Path $x86Target) -and (Test-Path $x64Target) -and -not $Force) {
	Write-Host "Native libwebp DLLs are already present under '$destination'."
	return
}

$tmpRoot = Join-Path ([IO.Path]::GetTempPath()) ("KeepassFaviconExtractor-WebP-" + [Guid]::NewGuid().ToString('N'))
try {
	New-Item -ItemType Directory -Path $tmpRoot -Force | Out-Null

	$x86Pkg = Join-Path $tmpRoot "Imazen.WebP.NativeRuntime.win-x86.$Version.nupkg"
	$x64Pkg = Join-Path $tmpRoot "Imazen.WebP.NativeRuntime.win-x64.$Version.nupkg"

	Download-Package -PackageId 'Imazen.WebP.NativeRuntime.win-x86' -Version $Version -OutFile $x86Pkg
	Download-Package -PackageId 'Imazen.WebP.NativeRuntime.win-x64' -Version $Version -OutFile $x64Pkg

	Extract-LibwebpDll -PackagePath $x86Pkg -RuntimeFolder 'win-x86' -TargetPath $x86Target
	Extract-LibwebpDll -PackagePath $x64Pkg -RuntimeFolder 'win-x64' -TargetPath $x64Target

	Write-Host "Native libwebp bootstrap completed."
}
finally {
	if (Test-Path $tmpRoot) {
		Remove-Item -Path $tmpRoot -Recurse -Force
	}
}
