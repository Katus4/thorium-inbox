[CmdletBinding()]
param(
    [switch] $UnsignedTestBuild,
    [string] $CertificateThumbprint,
    [string] $ArtifactSigningDlib,
    [string] $ArtifactSigningMetadata,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [string] $DotNetPath = 'dotnet',
    [string] $InnoCompiler,
    [string] $SignTool
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Checked([string] $Program, [string[]] $Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed with exit code $LASTEXITCODE." }
}

function Confirm-Signature([string] $File) {
    $signature = Get-AuthenticodeSignature -LiteralPath $File
    if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) {
        throw "A trusted, timestamped signature is required for $File."
    }
    Invoke-Checked $SignTool @('verify', '/pa', '/tw', $File)
}

$programFiles32 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
if (!$UnsignedTestBuild) {
    $usesCertificate = ![string]::IsNullOrWhiteSpace($CertificateThumbprint)
    $usesArtifactSigning = $ArtifactSigningDlib -and $ArtifactSigningMetadata
    if ($usesCertificate -eq $usesArtifactSigning) {
        throw 'Provide a certificate thumbprint or an Artifact Signing client and profile. Use -UnsignedTestBuild for local QA.'
    }
    if (!$SignTool) {
        $sdkBin = Join-Path $programFiles32 'Windows Kits/10/bin'
        $SignTool = Get-ChildItem -LiteralPath $sdkBin -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version] $_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (!$SignTool) { throw 'Install the Windows SDK or pass the path to SignTool.exe.' }
    $SignTool = (Resolve-Path -LiteralPath $SignTool).Path
}

if (!$InnoCompiler) {
    $registeredCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $InnoCompiler = if ($registeredCompiler) { $registeredCompiler.Source } else {
        Join-Path $programFiles32 'Inno Setup 6/ISCC.exe'
    }
}
$InnoCompiler = (Resolve-Path -LiteralPath $InnoCompiler).Path

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$version = ([xml](Get-Content -LiteralPath "$root/Directory.Build.props" -Raw)).Project.PropertyGroup.Version
$commit = ''
if (!$UnsignedTestBuild) {
    $pendingChanges = @(git -C $root status --porcelain)
    if ($LASTEXITCODE -or $pendingChanges.Count) { throw 'Build the signed installer from a clean checkout.' }
    $commit = git -C $root rev-parse HEAD
    if ($LASTEXITCODE) { throw 'Could not identify the release commit.' }
}
$work = Join-Path $root "artifacts/staging/$([guid]::NewGuid())"
$payload = Join-Path $work 'app'
$output = Join-Path $root $(if ($UnsignedTestBuild) { 'artifacts/test-installer' } else { 'artifacts/release' })
$fileBase = if ($UnsignedTestBuild) { 'ThoriumInbox-Setup-x64-Unsigned-Test' } else { 'ThoriumInbox-Setup-x64' }
New-Item -ItemType Directory -Force -Path $work, $output | Out-Null

Push-Location $root
try {
    Invoke-Checked $DotNetPath @('publish', 'src/ThoriumInbox/ThoriumInbox.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
        '-p:RestoreLockedMode=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $payload)

    Copy-Item -LiteralPath "$root/LICENSE", "$root/NOTICE", "$root/THIRD-PARTY-NOTICES.md" -Destination $payload
    Copy-Item -LiteralPath "$root/licenses" -Destination $payload -Recurse
    $installerArgs = @('/Qp', "/DAppVersion=$version", "/DPayloadDir=$payload", "/DInstallerName=$fileBase", "/O$output",
        "$root/installer/ThoriumInbox.iss")

    if (!$UnsignedTestBuild) {
        $signScript = Join-Path $PSScriptRoot 'Sign-File.ps1'
        $signArgs = @('-SignTool', $SignTool, '-TimestampUrl', $TimestampUrl)
        $innoSign = '/SthoriumSign=$q{0}$q -NoProfile -NonInteractive -File $q{1}$q -SignTool $q{2}$q -TimestampUrl $q{3}$q' -f
            (Get-Process -Id $PID).Path, $signScript, $SignTool, $TimestampUrl
        if ($usesCertificate) {
            if ($CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Use a 40-character certificate thumbprint.' }
            $signArgs += @('-CertificateThumbprint', $CertificateThumbprint)
            $innoSign += " -CertificateThumbprint $CertificateThumbprint"
        } else {
            $dlib = (Resolve-Path -LiteralPath $ArtifactSigningDlib).Path
            $profile = (Resolve-Path -LiteralPath $ArtifactSigningMetadata).Path
            $signArgs += @('-ArtifactSigningDlib', $dlib, '-ArtifactSigningMetadata', $profile)
            $innoSign += ' -ArtifactSigningDlib $q{0}$q -ArtifactSigningMetadata $q{1}$q' -f $dlib, $profile
        }

        foreach ($name in @('ThoriumInbox.exe', 'ThoriumInbox.dll', 'ThoriumInbox.Core.dll')) {
            $binary = Join-Path $payload $name
            Invoke-Checked (Get-Process -Id $PID).Path (@('-NoProfile', '-NonInteractive', '-File', $signScript) +
                $signArgs + @('-InputPath', $binary))
        }
        # Inno invokes the signer for its temporary uninstaller before embedding it.
        $installerArgs = @('/DRequireSigning=1', ($innoSign + ' -InputPath $f')) + $installerArgs
    }

    Invoke-Checked $InnoCompiler $installerArgs
    $installer = Join-Path $output "$fileBase.exe"
    if (!$UnsignedTestBuild) { Confirm-Signature $installer }

    $sha = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$sha  $fileBase.exe" + [Environment]::NewLine)
    if (!$UnsignedTestBuild) {
        [IO.File]::WriteAllText((Join-Path $output 'source-commit.txt'), $commit + [Environment]::NewLine)
    }
    Write-Output $installer
}
finally {
    Pop-Location
}
