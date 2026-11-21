# Contributing to Thorium Inbox

Use [Issues](../../issues/new/choose) for bugs and suggestions. Include your Windows and Thorium versions, the steps you took, and the relevant entry from **View activity**. A small redistributable test file is helpful for conversion bugs.

## Fork and open a pull request

1. Fork the repository on GitHub and clone your fork.
2. Add the original repository as `upstream` and fetch its branches.
3. Create your working branch from `upstream/develop`.
4. Run the Windows build and tests, then push the branch to your fork.
5. Open a pull request with upstream `develop` as the base. Describe the change and the checks you ran.

```powershell
git fetch upstream
git switch --create my-fix upstream/develop
dotnet restore ThoriumInbox.slnx --locked-mode
dotnet test ThoriumInbox.slnx --no-restore
```

If you need to add `upstream`, run `git remote add upstream` followed by the clone URL of this repository. `main` holds released code and accepts release pull requests from upstream `develop`. Fork pull requests run read-only checks. A maintainer may need to approve the first workflow run, and a maintainer reviews contributions before merging.

## Build the desktop app

Use Windows x64 and the .NET 10 SDK specified by [global.json](global.json). Run `dotnet run --project src/ThoriumInbox` to open the app. With [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed, run `./scripts/Build-Installer.ps1 -UnsignedTestBuild` to build a local test installer. See [release instructions](docs/releasing.md) for signed builds.

By submitting a pull request, you license your contribution under the repository’s [PolyForm Noncommercial 1.0.0 license](LICENSE).
