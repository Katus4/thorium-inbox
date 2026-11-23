# Contributing to Thorium Inbox

Use GitHub Issues for bugs and suggestions. Include your operating system and Thorium versions, the steps you took, and the relevant entry from **View activity**. A small redistributable test file is helpful for conversion bugs. [Issue forms](../../issues/new/choose)

## Fork and open a pull request

1. Fork the repository on GitHub and clone your fork.
2. Add the original repository as `upstream` and fetch its branches.
3. Create your working branch from `upstream/develop`.
4. Run the build and tests, then push the branch to your fork.
5. Open a pull request with upstream `develop` as the base. Describe the change and the checks you ran.

```powershell
git fetch upstream
git switch --create my-fix upstream/develop
dotnet restore ThoriumInbox.slnx --locked-mode
dotnet test ThoriumInbox.slnx --no-restore
```

If you need to add `upstream`, run `git remote add upstream` followed by the clone URL of this repository. `main` holds released code and accepts release pull requests from upstream `develop`. Fork pull requests run read-only checks. A maintainer may need to approve the first workflow run, and a maintainer reviews contributions before merging.

## Build the desktop app

Use the .NET 10 SDK in [global.json](global.json), then run `dotnet run --project src/ThoriumInbox` to open the shared Avalonia app. Build the installer on its target operating system. [Installer and testing instructions](docs/testing.md)

By submitting a pull request, you license your contribution under the repository’s [GNU General Public License version 3](LICENSE) (GPL-3.0-only).
