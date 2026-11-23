# Thorium Inbox

Thorium Inbox sends selected PDFs and EPUBs to Thorium Reader on Windows, Linux, and macOS. Put a PDF from any source into your dedicated inbox. The app waits for the file to finish downloading, converts it locally, and adds the EPUB to Thorium. [Thorium Reader](https://www.thoriumreader.com/)

## Install

Test installers are built in GitHub Actions for Windows x64, Debian/Ubuntu x64, and Intel and Apple Silicon Macs running macOS 15 or newer. Each build has its own download and SHA-256 checksum. [Download and test](docs/testing.md)

Windows setup can install Thorium Reader and lets you choose the watched folder. On Linux and macOS, install Thorium Reader from EDRLab and open Thorium Inbox once after installing it. Use **Start at login** in settings to control background startup. [Official Thorium Reader downloads](https://www.thoriumreader.com/)

Versioned, signed downloads will be published on [GitHub Releases](../../releases).

## Use

1. Open **Thorium Inbox** from the application launcher, tray, or menu bar and select **Open inbox**. The default is `Thorium Inbox/Inbox` in your Documents folder.
2. Save, copy, or move the PDFs and EPUBs you want to read into that folder. Existing EPUBs go straight to Thorium. Your original PDFs stay in the inbox.
3. Use **Open EPUBs** to find your converted copies under `Thorium Inbox/EPUBs` in Documents. Use **View activity** to check imports and see details of any failures.

The EPUB format preserves page layout as images. It works for magazine columns, scans, and other fixed pages, with zoom in Thorium. Keep the original PDF if you need to search or select its text.

The app checks GitHub Releases after startup and once a day. **Check for updates** runs a check on demand. A newer version shows its GitHub release notes. **Install now** downloads and verifies the signed update before opening the platform installer. **Later** dismisses the prompt.

## Privacy

Thorium Inbox processes your files locally and keeps your filenames, file contents, and import history on your device. Windows setup can download Thorium Reader from GitHub. The app requests public release information and any updates you choose to install from GitHub.

## Develop

Contributors work on a fork and open pull requests against `develop`. The complete released source lives on `main`. See the [contribution guide](CONTRIBUTING.md), [issue forms](../../issues/new/choose), and [release instructions](docs/releasing.md). VPS-hosted app updates are on the [roadmap](docs/roadmap.md).

Thorium Inbox is open source under the [GNU General Public License version 3](LICENSE) (GPL-3.0-only). [Third-party notices](THIRD-PARTY-NOTICES.md) cover the bundled libraries. Thorium Reader is developed by EDRLab.
