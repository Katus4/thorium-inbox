# Builds and releases

GitHub Actions tests the app and builds unsigned Windows x64, Debian/Ubuntu x64, and macOS Intel and Apple Silicon installers. Successful pushes to `main` and `develop` and manual workflow runs upload the packages with their checksums for 14 days. [Testing instructions](testing.md)

Trusted signing and release publication are on hold. Create one GitHub Release per version after the platform installers are ready and keep the previous releases available. The app reads the current version and notes from that release. Actions uploads test packages only.

## Prepare the release

1. Set the version in `Directory.Build.props` and add `docs/releases/vX.Y.Z.md`. Include **New features**, **Fixes**, and **Other changes** when those sections have entries. Use this file as the GitHub release body.
2. Promote upstream `develop` to `main` through a pull request and wait for CI. Build the release installers using the same `main` commit and the pinned .NET SDK.
3. Set `ThoriumUpdatePublicKey` as an environment variable when building each release binary. Use the base64-encoded DER SubjectPublicKeyInfo for the RSA key that signs update manifests. Keep the private key in the release signing environment.

## Platform packages

- **Windows x64.** With Inno Setup and Windows SDK SignTool installed, run `./scripts/Build-Installer.ps1 -CertificateThumbprint <thumbprint>` in PowerShell. The script signs the app, setup, and uninstaller and verifies trusted timestamps. It also accepts `-ArtifactSigningDlib <dll-path> -ArtifactSigningMetadata <metadata-path> -TimestampUrl http://timestamp.acs.microsoft.com` when using Microsoft Artifact Signing. Store signing credentials outside the repository.
- **Linux x64.** Build a production `.deb` using the Linux package layout. The signed update manifest authenticates the packaged bytes.
- **macOS Intel and Apple Silicon.** Build each production app bundle using the macOS package layout, sign the app with Developer ID Application, and sign the `.pkg` with Developer ID Installer. Verify the package signature, notarization, and stapled ticket on a Mac. [Apple Developer ID](https://developer.apple.com/developer-id/)

The Linux and macOS scripts in CI produce test packages. Build the production payload with `ThoriumTestBuild=false` before applying release signing. Test install, login startup, Reader import, and upgrades on each supported desktop with the production packages.

## Sign the update manifest

Use these exact asset names when the installers are finalized.

| Platform | Asset |
| --- | --- |
| Windows x64 | `ThoriumInbox-Setup-x64.exe` |
| Debian/Ubuntu x64 | `ThoriumInbox-linux-x64.deb` |
| macOS Intel | `ThoriumInbox-macos-x64.pkg` |
| macOS Apple Silicon | `ThoriumInbox-macos-arm64.pkg` |

In `artifacts/release`, write `update-manifest.json` as UTF-8 JSON. The `tag` is the version tag, for example `v0.1.0`. The `sha256` object maps each installer filename to its 64-character SHA-256 digest. Calculate digests after platform signing and notarization. Sign the exact manifest bytes with RSA PKCS#1 v1.5 and SHA-256 using the private key that matches the public key in the shipped apps.

```sh
openssl dgst -sha256 -sign /secure/path/update-key.pem -out update-manifest.sig update-manifest.json
```

Run `./scripts/Publish-Release.ps1 -Repository Katus4/thorium-inbox` from the matching Windows release checkout with PowerShell 7, OpenSSL, and `ThoriumUpdatePublicKey` set. The script validates the Windows signature, confirms the version tag matches the source commit, and verifies the uploaded assets in a draft. After checking signatures, notarization, and the draft release notes, publish the draft as the latest stable release in GitHub. [GitHub Releases](../../../releases)

An installed app with the matching public key verifies the manifest and installer digest before handing the package to the operating system. Builds without an update key can open the release page from the update dialog. Users choose **Install now** or **Later**.

On Linux the app shows where it saved the verified `.deb` so you can install it with your package manager and restart the app.
