# Thorium Inbox contributor instructions

## Keep C# readable

- Give each `.cs` file one clear responsibility. Keep the tray, settings window, inbox processing, EPUB packaging, and Thorium integration in their own files.
- If a file starts to mix concerns or becomes hard to scan, split it at a boundary readers can name. Keep related code together when a split would make it harder to follow.
- Use names that explain the work. Prefer clear control flow and small methods over dense expressions, deep nesting, and helpers that only rename an operation.
- Keep comments for decisions the code cannot explain, such as Windows file sharing, EPUB archive requirements, and Thorium activation behavior.

## Development and checks

- Use the .NET SDK specified in `global.json`. Run `dotnet restore ThoriumInbox.slnx --locked-mode`, then `dotnet test ThoriumInbox.slnx --no-restore` on Windows x64. Build the WinForms project separately when changing the executable or installer inputs.
- Test observable behavior when changing download settling, file conversion, import receipts, retry behavior, or installer integration. Use shareable PDFs and a disposable Thorium profile for testing.
- Preserve the original PDF, process only the configured inbox, and record a successful import after Thorium accepts it. Keep the fixed-layout EPUB readable in page order.
- Use the native Windows controls and wording described in `DESIGN.md`. Keep the tray useful at sign-in and the settings window usable with the keyboard.

## Collaboration and releases

- Submit changes from a fork to upstream `develop`. Promote `develop` to `main` for a release.
- Keep fork checks read-only and run signing with the trusted release process. Keep signing keys, authentication credentials, and private sample files out of Git.
- Maintain `LICENSE` and third-party notices. Add VPS update work to the roadmap until it is implemented and verified.
