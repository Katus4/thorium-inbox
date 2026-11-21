[CmdletBinding()]
param([Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$')] [string] $Repository)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function GitHub([string[]] $Arguments) {
    & gh @Arguments
    if ($LASTEXITCODE) { throw "GitHub command failed ($($Arguments[0]), exit $LASTEXITCODE)." }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$release = Join-Path $root 'artifacts/release'
$name = 'ThoriumInbox-Setup-x64.exe'
$installer = Join-Path $release $name
$sums = Join-Path $release 'SHA256SUMS.txt'
if (!(Test-Path -LiteralPath $installer) -or !(Test-Path -LiteralPath $sums)) {
    throw 'Build the signed release installer first.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) {
    throw 'The release requires a publicly trusted, timestamped Authenticode signature.'
}
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-Content -LiteralPath $sums -Raw).Trim() -ne "$hash  $name") { throw 'Installer SHA-256 does not match.' }

$version = ([xml](Get-Content -LiteralPath "$root/Directory.Build.props" -Raw)).Project.PropertyGroup.Version
$commit = (Get-Content -LiteralPath "$release/source-commit.txt" -Raw).Trim()
$head = git -C $root rev-parse HEAD
if ($LASTEXITCODE -or $head -ne $commit) { throw 'Check out the source commit used to build the installer.' }
$published = (GitHub @('api', "repos/$Repository/git/ref/heads/main", '--jq', '.object.sha')).Trim()
if ($published -ne $commit) { throw 'The installer must match the current commit on GitHub main.' }
$visibility = (GitHub @('repo', 'view', $Repository, '--json', 'visibility', '--jq', '.visibility')).Trim()
if ($visibility -ne 'PUBLIC') { throw 'The release repository must be public.' }

$tag = "v$version"
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

GitHub @('release', 'create', $tag, $installer, $sums, '--repo', $Repository, '--verify-tag',
    '--title', "Thorium Inbox $version", '--notes-file', "$root/docs/releases/$tag.md", '--draft')
$releases = GitHub @('api', "repos/$Repository/releases") | ConvertFrom-Json
$draft = $releases | Where-Object { $_.tag_name -eq $tag -and $_.draft } | Select-Object -First 1
if (!$draft -or $name -notin $draft.assets.name -or 'SHA256SUMS.txt' -notin $draft.assets.name) {
    throw 'The draft release is missing an expected asset.'
}
$remoteDigest = ($draft.assets | Where-Object name -eq $name).digest
$remoteSumsDigest = ($draft.assets | Where-Object name -eq 'SHA256SUMS.txt').digest
$sumsHash = (Get-FileHash -LiteralPath $sums -Algorithm SHA256).Hash.ToLowerInvariant()
if ($remoteDigest -ne "sha256:$hash" -or $remoteSumsDigest -ne "sha256:$sumsHash") {
    throw 'The uploaded release digests did not match.'
}
GitHub @('release', 'edit', $tag, '--repo', $Repository, '--draft=false', '--latest')
