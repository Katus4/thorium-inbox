# Roadmap

## First release

Ship a signed Windows x64 installer and a dedicated inbox that converts PDFs to fixed-layout EPUBs and imports them into Thorium. Keep install instructions and issue forms in the public repository.

## Updates from the VPS

Host a static HTTPS update feed and signed installer assets on the project’s VPS. Add a tray notification for new versions and an **Install update** action. Use NetSparkle with strict Ed25519 verification of the feed and installer, and verify the Windows publisher signature before running setup. Store the signing keys in the release environment. Keep the watched folder and import history through upgrades.
