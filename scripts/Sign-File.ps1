[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InputPath,
    [Parameter(Mandatory)] [string] $SignTool,
    [Parameter(Mandatory)] [string] $TimestampUrl,
    [string] $CertificateThumbprint,
    [string] $ArtifactSigningDlib,
    [string] $ArtifactSigningMetadata
)

$ErrorActionPreference = 'Stop'
$arguments = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256')
if ($CertificateThumbprint) {
    $arguments += @('/sha1', $CertificateThumbprint)
} elseif ($ArtifactSigningDlib -and $ArtifactSigningMetadata) {
    $arguments += @('/dlib', $ArtifactSigningDlib, '/dmdf', $ArtifactSigningMetadata)
} else {
    throw 'No code-signing identity was supplied.'
}

& $SignTool @arguments $InputPath
if ($LASTEXITCODE -ne 0) { throw "SignTool could not sign $InputPath." }
& $SignTool verify /pa /tw $InputPath
if ($LASTEXITCODE -ne 0) { throw "SignTool could not verify $InputPath." }
$signature = Get-AuthenticodeSignature -LiteralPath $InputPath
if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) {
    throw "A trusted, timestamped signature is required for $InputPath."
}
