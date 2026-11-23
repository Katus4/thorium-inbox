#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$')] [string] $Repository,
    [string] $OpenSsl = 'openssl'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function GitHub([string[]] $Arguments) {
    & gh @Arguments
    if ($LASTEXITCODE) { throw "GitHub command failed ($($Arguments[0]), exit $LASTEXITCODE)." }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$release = Join-Path $root 'artifacts/release'
$windowsInstaller = 'ThoriumInbox-Setup-x64.exe'
$installers = @($windowsInstaller, 'ThoriumInbox-linux-x64.deb', 'ThoriumInbox-macos-x64.pkg', 'ThoriumInbox-macos-arm64.pkg')
$manifestName = 'update-manifest.json'
$signatureName = 'update-manifest.sig'
$assetNames = $installers + @($manifestName, $signatureName)
foreach ($name in $assetNames) {
    if (!(Test-Path -LiteralPath (Join-Path $release $name) -PathType Leaf)) {
        throw "Prepare the trusted release assets first. Missing $name."
    }
}
$windowsSignature = Get-AuthenticodeSignature -LiteralPath (Join-Path $release $windowsInstaller)
if ($windowsSignature.Status -ne 'Valid' -or !$windowsSignature.TimeStamperCertificate) {
    throw 'The release requires a publicly trusted, timestamped Authenticode signature.'
}

$version = ([xml](Get-Content -LiteralPath "$root/Directory.Build.props" -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part app version.' }
$tag = "v$version"
$notes = Join-Path $root "docs/releases/$tag.md"
if (!(Test-Path -LiteralPath $notes -PathType Leaf)) { throw "Write the release notes for $tag first." }

$publicKey = $env:ThoriumUpdatePublicKey
if ([string]::IsNullOrWhiteSpace($publicKey)) {
    throw 'Set ThoriumUpdatePublicKey to the public key built into the release binaries.'
}
$manifestPath = Join-Path $release $manifestName
$signaturePath = Join-Path $release $signatureName
$manifestFile = Get-Item -LiteralPath $manifestPath
$signatureFile = Get-Item -LiteralPath $signaturePath
if ($manifestFile.Length -gt 65536 -or $signatureFile.Length -gt 8192) { throw 'The update manifest or signature is too large.' }
$keyPath = Join-Path $release ("update-key-" + [guid]::NewGuid() + ".der")
try {
    [IO.File]::WriteAllBytes($keyPath, [Convert]::FromBase64String($publicKey))
    $keyDetails = & $OpenSsl rsa -pubin -inform DER -in $keyPath -text -noout
    if ($LASTEXITCODE -or ($keyDetails -join "`n") -notmatch '\((\d+) bit\)' -or [int] $Matches[1] -lt 2048) {
        throw 'The update signing key must be RSA with at least 2048 bits.'
    }
    & $OpenSsl dgst -sha256 -verify $keyPath -keyform DER -signature $signaturePath $manifestPath
    if ($LASTEXITCODE) { throw 'The update manifest signature could not be verified.' }
}
finally {
    if (Test-Path -LiteralPath $keyPath) { Remove-Item -LiteralPath $keyPath }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.tag -cne $tag) { throw "The signed manifest must identify $tag." }
$hashes = @{}
foreach ($name in $assetNames) {
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $release $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($name -in $installers -and $hashes[$name] -ne $manifest.sha256[$name]) {
        throw "Installer SHA-256 does not match the signed manifest for $name."
    }
}
$sumsName = 'SHA256SUMS.txt'
$sums = Join-Path $release $sumsName
$checksumLines = foreach ($name in $assetNames) { "$($hashes[$name])  $name" }
[IO.File]::WriteAllLines($sums, [string[]] $checksumLines)
$assetNames += $sumsName
$hashes[$sumsName] = (Get-FileHash -LiteralPath $sums -Algorithm SHA256).Hash.ToLowerInvariant()

$commit = (Get-Content -LiteralPath "$release/source-commit.txt" -Raw).Trim()
$head = git -C $root rev-parse HEAD
if ($LASTEXITCODE -or $head -ne $commit) { throw 'Check out the source commit used to build the installer.' }
$published = (GitHub @('api', "repos/$Repository/git/ref/heads/main", '--jq', '.object.sha')).Trim()
if ($published -ne $commit) { throw 'The installer must match the current commit on GitHub main.' }
$visibility = (GitHub @('repo', 'view', $Repository, '--json', 'visibility', '--jq', '.visibility')).Trim()
if ($visibility -ne 'PUBLIC') { throw 'The release repository must be public.' }

$tagRef = "refs/tags/$tag"
$matchingRefs = GitHub @('api', "repos/$Repository/git/matching-refs/tags/$tag") | ConvertFrom-Json
$tagObject = $matchingRefs | Where-Object ref -eq $tagRef | Select-Object -ExpandProperty object
if (!$tagObject) {
    $tagData = @{ ref = $tagRef; sha = $commit } | ConvertTo-Json -Compress
    $tagResponse = $tagData | & gh api --method POST "repos/$Repository/git/refs" --input '-'
    if ($LASTEXITCODE) { throw "Could not create $tagRef." }
    $tagObject = ($tagResponse | ConvertFrom-Json).object
}
while ($tagObject.type -eq 'tag') {
    $tagObject = (GitHub @('api', "repos/$Repository/git/tags/$($tagObject.sha)") | ConvertFrom-Json).object
}
if ($tagObject.type -ne 'commit' -or $tagObject.sha -ne $commit) { throw "$tag does not point to the build commit." }

$assets = @($assetNames | ForEach-Object { Join-Path $release $_ })
GitHub (@('release', 'create', $tag) + $assets + @('--repo', $Repository, '--verify-tag',
    '--title', "Thorium Inbox $version", '--notes-file', $notes, '--draft'))
$draftFilter = '.[] | select(.tag_name == "{0}" and .draft)' -f $tag
$draft = GitHub @('api', "repos/$Repository/releases", '--paginate', '--jq', $draftFilter) | ConvertFrom-Json
if (!$draft) { throw "Draft release $tag was not found." }
foreach ($name in $assetNames) {
    $asset = $draft.assets | Where-Object { $_.name -ceq $name -and $_.state -eq 'uploaded' } | Select-Object -First 1
    if (!$asset -or $asset.digest -ne "sha256:$($hashes[$name])") {
        throw "GitHub did not return the expected digest for $name."
    }
}
GitHub @('release', 'edit', $tag, '--repo', $Repository, '--draft=false', '--latest')
