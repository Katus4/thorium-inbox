# Roadmap

## First release

Ship a signed Windows x64 installer and a dedicated inbox that converts PDFs to fixed-layout EPUBs and imports them into Thorium. Keep install instructions and issue forms in the public repository.

## Linux and macOS

Keep the PDF conversion, inbox logic, and update checks in C#. Use Avalonia for a shared desktop interface as the port matures. Add support for the native Thorium installation on each platform, then verify login startup and packaging for each OS.

## Updates from GitHub Releases

Use a separate GitHub Release for each published version and keep past releases available. Show the new version and its release notes in the app. Offer **Get update** to open the download page for that specific release and **Later** to dismiss the prompt. Keep the watched folder and import history through upgrades.

A VPS-hosted update feed is a future option. GitHub Releases will hold the installers and version history.
