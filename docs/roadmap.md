# Roadmap

## First release

Ship trusted installers for Windows x64, Linux x64, and Intel and Apple Silicon Macs. Use a dedicated inbox that converts PDFs to fixed-layout EPUBs and imports them into Thorium.

The C# and Avalonia app has test installers on all three platforms. Test dependency detection, sign-in startup, and upgrades on clean desktops before the first release.

## Updates from GitHub Releases

Keep a separate GitHub Release for each published version. Add its platform installers and a signed update manifest. The app shows the release notes, offers **Install now** and **Later**, and preserves the watched folder and import history through upgrades.

A VPS-hosted update feed is a future option. GitHub Releases will hold the installers and version history.
