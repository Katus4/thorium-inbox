# Publishing a Windows release

Promote upstream `develop` to `main` through a pull request and wait for the Windows and branch checks. Build from the matching commit on `main` using the pinned .NET SDK, Inno Setup 6, and a Windows SDK SignTool.

## Sign the installer

Use a publicly trusted code-signing certificate available to the current Windows user.

```powershell
./scripts/Build-Installer.ps1 -CertificateThumbprint <thumbprint>
```

For Microsoft Artifact Signing, supply both the client DLL and the account/profile metadata file from outside the checkout.

```powershell
./scripts/Build-Installer.ps1 -ArtifactSigningDlib <dll-path> -ArtifactSigningMetadata <metadata-path> -TimestampUrl http://timestamp.acs.microsoft.com
```

The build signs and verifies the app binaries, setup executable, and uninstaller. It writes release files to `artifacts/release`. A build with `-UnsignedTestBuild` goes to `artifacts/test-installer` for local QA.

## Verify and publish

Test the signed setup on a Windows x64 machine with Defender enabled. Exercise a clean install with and without Thorium, install with a custom inbox, sign out and back in, drop a PDF and EPUB in the inbox, then verify uninstall removes the sign-in entry. Confirm the converted pages and Thorium library entry.

Run `./scripts/Publish-Release.ps1 -Repository <owner/name>` from the published `main` commit. It checks the installer signature and SHA-256, creates a draft release, then publishes the verified assets. Keep distribution versions in `Directory.Build.props`. Keep certificate keys and signing profile files outside the repository.
