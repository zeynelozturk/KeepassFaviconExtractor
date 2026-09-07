[CmdletBinding()]
param(
	[string]$DestinationPath = (Join-Path $PSScriptRoot '..\.deps\KeePass\2.61'),
	[switch]$Force
)

$ErrorActionPreference = 'Stop'

$version = '2.61'
$archiveName = "KeePass-$version.zip"
$downloadUrl = "https://downloads.sourceforge.net/project/keepass/KeePass%202.x/$version/$archiveName"
$expectedSha256 = 'BD8FE3E19198FF18DAA4D338D008B582E1BF1B00742BA81783FAC5DD4877CFD9'
$destination = [IO.Path]::GetFullPath($DestinationPath)
$keepassExecutable = Join-Path $destination 'KeePass.exe'

if ((Test-Path $keepassExecutable) -and -not $Force) {
	Write-Host "KeePass $version is already available at '$destination'."
	return
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("KeepassFaviconExtractor-" + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $temporaryRoot $archiveName
$extractPath = Join-Path $temporaryRoot 'extracted'

try {
	New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

	Write-Host "Downloading KeePass $version..."
	Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing -UserAgent 'curl/8.0'

	$actualSha256 = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash
	if ($actualSha256 -ne $expectedSha256) {
		throw "Checksum verification failed for '$archiveName'. Expected $expectedSha256, received $actualSha256."
	}

	Expand-Archive -Path $archivePath -DestinationPath $extractPath
	if (-not (Test-Path (Join-Path $extractPath 'KeePass.exe'))) {
		throw "The downloaded archive does not contain KeePass.exe."
	}

	if (Test-Path $destination) {
		Remove-Item -Path $destination -Recurse -Force
	}

	New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
	Move-Item -Path $extractPath -Destination $destination

	Write-Host "KeePass $version installed at '$destination'."
}
finally {
	if (Test-Path $temporaryRoot) {
		Remove-Item -Path $temporaryRoot -Recurse -Force
	}
}
