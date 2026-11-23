# Test Thorium Inbox

Open a successful installer run and download the artifact for your machine from the **Artifacts** section. Sign in to GitHub to download it. Extract the ZIP to get the installer and `SHA256SUMS.txt`. [GitHub Actions runs](../../../actions/workflows/ci.yml)

| Platform | Artifact | Install |
| --- | --- | --- |
| Windows x64 | `thorium-inbox-windows-x64-unsigned-test` | Run `ThoriumInbox-Setup-x64-Unsigned-Test.exe` and select a dedicated test folder when setup asks. Setup installs Thorium Reader if it is missing. |
| Debian or Ubuntu x64 | `thorium-inbox-linux-x64-unsigned-test` | From the extracted directory, run `sudo apt install ./ThoriumInbox-linux-x64-Unsigned-Test.deb`. Install Thorium Reader from EDRLab and launch **Thorium Inbox** from the applications menu. |
| macOS 15+ on Apple Silicon or Intel | `thorium-inbox-macos-arm64-unsigned-test` or `thorium-inbox-macos-x64-unsigned-test` | Open the matching `.pkg` on a Mac that permits unsigned test packages. Install Thorium Reader from EDRLab and open **Thorium Inbox** from Applications. |

Use a test machine or VM whose policy permits unsigned builds. To verify the download, compare the result of `Get-FileHash .\ThoriumInbox-Setup-x64-Unsigned-Test.exe` with the accompanying checksum on Windows. Run `sha256sum -c SHA256SUMS.txt` on Linux or `shasum -a 256 -c SHA256SUMS.txt` on macOS. [Thorium Reader downloads](https://www.thoriumreader.com/)

## Check conversion and import

1. In Thorium Inbox, use **Change folder** to select an empty test folder. Choose the Thorium executable with **Choose Reader** if automatic detection fails.
2. Copy a small, shareable PDF and an EPUB into the watched folder. Wait for both books to appear in the Thorium Reader library.
3. Use **Open EPUBs** to find the converted PDF. Open it in Reader and check the page order and zoom. The original PDF should still be in your inbox.
4. With Thorium Reader open, copy the same two files in again. Check that the activity log and library show one import per book.

## Check login startup and updates

1. Enable **Start at login**, sign out and back in, then copy a different PDF into the inbox. The background app should process it. On desktops without a tray area, launch Thorium Inbox from the applications menu to reopen settings.
2. Select **Check for updates** to exercise the GitHub connection. With no newer platform release, the app reports that it is up to date for that platform.
3. Select **Preview update** to open sample release notes. Choose **Later**, reopen the preview, then choose **Install now**. The preview reports your choice and finishes without launching an installer.

Use **View activity** when reporting an issue. Include your operating system, the Actions run, and the Thorium Reader version. Remove private paths and filenames from shared logs.

## Build test installers locally

Use the SDK pinned by `global.json`. Run the target script on the matching operating system. The installers and checksums appear under `artifacts/test-installer`.

- Windows x64 with Inno Setup 6. Run `./scripts/Build-Installer.ps1 -UnsignedTestBuild`.
- Debian/Ubuntu x64. Run `./scripts/Build-LinuxInstaller.sh`.
- Apple Silicon or Intel Mac. Run `./scripts/Build-MacInstaller.sh arm64` or `./scripts/Build-MacInstaller.sh x64`, respectively.
