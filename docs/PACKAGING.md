# MedReminder — Packaging and Distribution

This document describes how to build, package, verify, and distribute MedReminder for end users.

## 1. Distribution Policy

The official MedReminder distribution channel is:

- **ZIP** — official distribution channel.

The alternative distribution channel is:

- **MSI** — optional alternative installer.

**MSIX is not used.**

The official production package is a Windows x64, .NET 10, self-contained build.

## 2. Requirements

To build MedReminder locally, the following are required:

- Windows 10/11 x64;
- .NET 10 SDK;
- access to the NuGet packages used by the solution;
- a cloned copy of the repository.

WinForms and Windows-specific APIs used by MedReminder require a Windows build environment.

Verify the installed SDK with:

```powershell
dotnet --version
dotnet --list-sdks
```

## 3. Restore and Build

Restore the solution:

```powershell
dotnet restore MedReminder.sln
```

Build the Release configuration:

```powershell
dotnet build MedReminder.sln -c Release
```

## 4. Run the Tests

Before creating a distributable package, run the complete test suite:

```powershell
dotnet test MedReminder.sln -c Release
```

The current test projects include:

```text
MedReminder.Domain.Tests
MedReminder.Application.Tests
MedReminder.Infrastructure.Tests
```

The release pipeline must stop if any test fails.

## 5. Production Windows x64 Publish

The official production build is:

- Windows x64;
- `Release`;
- `net10.0-windows`;
- self-contained.

Publish with:

```powershell
dotnet publish `
    src/MedReminder.UI/MedReminder.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o publish
```

The output is generated in:

```text
publish/
```

### Self-contained deployment

The production package uses:

```text
--self-contained true
```

The .NET runtime required by the application is therefore included in the package.

The end user does not need to install the .NET 10 Desktop Runtime separately.

### Runtime

The current production runtime is:

```text
win-x64
```

The package is intended for 64-bit Windows systems using the x64 architecture.

### Trimming and AOT

Trimming is disabled unless explicitly validated.

`PublishTrimmed=false` is the safe default because MedReminder uses WinForms, Entity Framework Core, reflection, data binding, and other components that may depend on runtime-discovered types.

Native AOT is not part of the current production distribution.

## 6. Package Contents

The final package must contain the published application and all required runtime dependencies.

Typical contents include:

```text
MedReminder.exe
MedReminder.dll
MedReminder.*.dll
MailKit.dll
MimeKit.dll
Microsoft.EntityFrameworkCore.*.dll
Microsoft.Data.Sqlite.dll
Serilog.dll
runtimes/
appsettings.json
```

Development files, local databases, logs, credentials, temporary files, and other machine-specific data must not be included.

## 7. Official ZIP Package

After publishing, create the official ZIP package:

```powershell
Compress-Archive `
    -Path publish\* `
    -DestinationPath MedReminder-win-x64.zip
```

The resulting package is:

```text
MedReminder-win-x64.zip
```

The asset name is intentionally stable and does not contain the application version.

This allows the latest-release download URL to remain stable:

```text
https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip
```

## 8. Alternative MSI Package

An MSI installer may be provided as an alternative distribution channel.

The MSI must install the same production application produced by the official Windows x64 self-contained publish.

The MSI is **not** the primary distribution channel. The ZIP package remains the official distribution format.

MSIX is not part of the MedReminder distribution strategy.

## 9. Complete Local Packaging Test

The complete process can be tested locally with:

```powershell
dotnet restore MedReminder.sln

dotnet test MedReminder.sln -c Release --no-restore

dotnet publish `
    src/MedReminder.UI/MedReminder.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o publish

Compress-Archive `
    -Path publish\* `
    -DestinationPath MedReminder-win-x64.zip
```

The resulting package is:

```text
MedReminder-win-x64.zip
```

Extract the ZIP into a clean local directory and verify that:

```text
MedReminder.exe
```

starts correctly.

For the final release, verification should preferably also be performed on a Windows machine separate from the development environment.

## 10. Versioning

Released versions are identified by Git tags using Semantic Versioning:

```text
vMAJOR.MINOR.PATCH
```

Examples:

```text
v1.0.0
v1.1.0
v1.1.1
v2.0.0
```

The Git tag identifies the application version associated with the release.

## 11. Creating a Release

After completing and verifying the changes:

```powershell
git status
git add .
git commit -m "Prepare release v1.1.0"
git push
```

Create the release tag:

```powershell
git tag v1.1.0
```

Push the tag:

```powershell
git push origin v1.1.0
```

The tag push starts the GitHub Actions release workflow.

## 12. GitHub Actions

The release workflow is located at:

```text
.github/workflows/release.yml
```

It is triggered when a tag beginning with `v` is pushed.

Examples:

```text
v1.0.0
v1.2.3
v2.0.0
```

The release pipeline performs the following steps:

```text
Git tag
   |
   v
Checkout repository
   |
   v
Install .NET 10
   |
   v
dotnet restore
   |
   v
dotnet test
   |
   +---- FAIL ----> workflow terminated
   |
   v
dotnet publish
   |
   v
Create ZIP
   |
   v
Create GitHub Release
   |
   v
Upload MedReminder-win-x64.zip
```

The GitHub Release must not be created if the test stage fails.

## 13. Release Workflow

The `.github/workflows/release.yml` workflow should implement the following logic:

```yaml
name: Build and Release MedReminder

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: write

jobs:
  release:
    name: Build Windows Release
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore MedReminder.sln

      - name: Test
        run: dotnet test MedReminder.sln -c Release --no-restore

      - name: Publish
        run: >
          dotnet publish
          src/MedReminder.UI/MedReminder.UI.csproj
          -c Release
          -r win-x64
          --self-contained true
          -o publish

      - name: Create ZIP
        shell: pwsh
        run: |
          Compress-Archive `
            -Path publish\* `
            -DestinationPath MedReminder-win-x64.zip

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: MedReminder-win-x64.zip
          generate_release_notes: true
```

## 14. GitHub Release

A successful pipeline creates a GitHub Release associated with the tag.

For example:

```text
v1.1.0
```

The release asset should include:

```text
MedReminder-win-x64.zip
```

GitHub may additionally provide its automatically generated source archives.

The releases page is:

```text
https://github.com/vger70/MedReminder/releases
```

The latest release is:

```text
https://github.com/vger70/MedReminder/releases/latest
```

## 15. README Download Link

The `README.md` should provide a direct link to the latest official package.

Example:

```markdown
## Download

**Windows x64**

[Download MedReminder](https://github.com/vger70/MedReminder/releases/latest/download/MedReminder-win-x64.zip)

[View all releases](https://github.com/vger70/MedReminder/releases)
```

Because the asset name remains:

```text
MedReminder-win-x64.zip
```

the README link does not need to change for every release.

GitHub automatically resolves:

```text
/releases/latest/download/MedReminder-win-x64.zip
```

to the asset belonging to the latest release.

## 16. Recommended Release Procedure

For every release:

1. Complete the implementation changes.
2. Run the complete test suite locally.
3. Verify the application in Release configuration.
4. Verify the production publish.
5. Test the generated ZIP package.
6. Update documentation when required.
7. Commit and push the changes.
8. Create the version tag.
9. Push the version tag.
10. Verify the GitHub Actions workflow.
11. Verify the GitHub Release.
12. Verify the public download link.

Example:

```powershell
dotnet test MedReminder.sln -c Release

git add .
git commit -m "Prepare release v1.1.0"
git push

git tag v1.1.0
git push origin v1.1.0
```

The GitHub Actions workflow creates the release automatically after the tag is pushed.

## 17. Package Verification

Before considering a release complete, verify at least:

- the application starts successfully;
- the SQLite database is created/opened correctly;
- the main application features work;
- email sending works when configured;
- DPAPI-protected configuration works on the target machine;
- all required application files are present;
- no development files are included;
- no credentials, passwords, tokens, or secrets are included;
- the ZIP can be extracted on a clean Windows machine;
- the application runs after extraction;
- the latest-release download URL works.

## 18. Local Application Data

The distribution package contains the application and its dependencies.

Data generated during application use must not be committed to Git and must not be included in release packages.

Do not distribute:

- development SQLite databases;
- personal configuration;
- credentials;
- passwords;
- access tokens;
- private keys or secrets;
- temporary files;
- development logs.

User-specific configuration and local application data must be handled according to the user documentation.

## 19. Files That Must Not Be Versioned

The following files and directories should not be committed:

```text
bin/
obj/
publish/
*.user
*.suo
*.db
*.sqlite
*.sqlite3
*.log
```

Files containing credentials or secrets must also be excluded through `.gitignore`.

## 20. Documentation Consistency

For every significant release, verify consistency between:

```text
README.md
docs/USER_GUIDE.en.md
docs/USER_GUIDE.it.md
docs/PACKAGING.md
docs/ANALYSIS.md
```

`README.md` provides concise project information and the download link.

The user guides describe application usage.

`PACKAGING.md` describes build, test, publishing, packaging, and distribution.

`ANALYSIS.md` contains the technical analysis and architecture information.

## 21. Distribution Strategy

The current distribution strategy is intentionally simple:

```text
                         MedReminder
                              |
                              v
                    Windows x64 Release
                              |
                    .NET 10 Self-contained
                              |
                  +-----------+-----------+
                  |                       |
                  v                       v
             Official ZIP            Alternative MSI
                  |                       |
                  v                       v
        GitHub Releases             GitHub Releases
                  |
                  v
       /releases/latest/download/
       MedReminder-win-x64.zip
```

The ZIP package is the canonical distribution artifact.

The MSI package is an optional convenience installer and must contain the same production application.

MSIX is not used.

## 22. Future Enhancements

Possible future improvements include:

- digital signing of executables and installers;
- SHA-256 checksums for release assets;
- customized release notes;
- `win-arm64` builds;
- an automatic update mechanism;
- improved MSI installation and upgrade handling.

These enhancements should preserve the existing stable ZIP download mechanism whenever practical.
