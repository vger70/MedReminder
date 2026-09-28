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
MedReminder.Infrastructure.Portable.Tests
MedReminder.Infrastructure.Tests
MedReminder.UI.Tests
MedReminder.DataImporter.Tests
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
.github/workflows/dotnet-desktop.yml
```

It is triggered when a tag of the form `vX.Y.Z` is pushed; any other
`v*` tag fails at the version check.

Examples:

```text
v1.0.0
v1.2.3
v2.0.0
```

The release pipeline performs the following steps:

```text
Git tag vX.Y.Z
   |
   v
Checkout repository, install .NET 10
   |
   v
Extract X.Y.Z from the tag (Version, AssemblyVersion, FileVersion, ...)
   |
   v
dotnet restore (solution and WiX project)
   |
   v
dotnet publish, self-contained      --> MedReminder-win-x64-net10.zip
   |
   v
dotnet publish, framework-dependent --> MedReminder-win-x64.zip
   |
   v
WiX build over the framework-dependent output --> MedReminder-win-x64.msi
   |
   v
Create the GitHub Release with the three assets
```

The workflow does not run the tests: run `dotnet test` (section 4)
before pushing the tag. The two `dotnet publish` steps receive the
Google Drive client id and secret from repository secrets (section 24).

## 13. Release Workflow

The file itself is the reference; its comments explain each step. The
points that matter when editing it:

- The version comes only from the tag, through MSBuild properties;
  nothing is committed back.
- `MEDREMINDER_GOOGLE_CLIENT_ID` and `MEDREMINDER_GOOGLE_CLIENT_SECRET`
  are set with `env:` on both `dotnet publish` steps, from the
  repository secrets of the same names. A new step that compiles the
  application needs them too, or its output has no Google Drive.
- The MSI is built from the framework-dependent publish folder, so the
  MSI and `MedReminder-win-x64.zip` contain the same binaries.

## 14. GitHub Release

A successful pipeline creates a GitHub Release associated with the tag.

For example:

```text
v1.1.0
```

The release assets are:

```text
MedReminder-win-x64-net10.zip   self-contained (includes the .NET runtime)
MedReminder-win-x64.zip         framework-dependent (.NET 10 Desktop Runtime required)
MedReminder-win-x64.msi         installer, framework-dependent
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

## 23. Donation / Support Development configuration (maintainer)

The "Support Development" feature (A6) is **off by default** and ships
with no real links. It reads its configuration from a shared,
admin-managed file at the root of the local application data folder:

```text
%LOCALAPPDATA%\MedReminder\donations.settings.json
```

A template with placeholder URLs (`donations.settings.json`) is copied
into the build output next to `appsettings.json`. The application does
**not** read the template from the install directory — copy it to the
data-folder root above and edit it there. If the file is missing, the
`Donations` section is absent, or `Enabled` is `false`, the feature
stays silently off and the **Help → Support development…** menu entry
does not appear.

The file holds only **public payment URLs**. It must never contain a
secret — no Stripe secret/restricted key, no PayPal client secret, no
access token, no private key. It is therefore plain JSON, not
DPAPI-encrypted.

### File shape

```json
{
  "Donations": {
    "Enabled": true,
    "Currency": "EUR",
    "Stripe": {
      "Enabled": true,
      "PaymentLinks": {
        "2":  "https://donate.stripe.com/...",
        "5":  "https://donate.stripe.com/...",
        "10": "https://donate.stripe.com/...",
        "20": "https://donate.stripe.com/...",
        "custom": "https://donate.stripe.com/..."
      }
    },
    "PayPal": {
      "Enabled": true,
      "PaymentLinks": {
        "2":  "https://www.paypal.com/donate/?hosted_button_id=...",
        "5":  "https://www.paypal.com/donate/?hosted_button_id=...",
        "10": "https://www.paypal.com/donate/?hosted_button_id=...",
        "20": "https://www.paypal.com/donate/?hosted_button_id=...",
        "custom": "https://www.paypal.com/donate/?hosted_button_id=..."
      }
    }
  }
}
```

- Keys `"2"`, `"5"`, `"10"`, `"20"` are the fixed tiers. Each must be
  its own **fixed-amount** Payment Link — the app never appends an
  amount to a URL.
- The optional `"custom"` key is a provider-native "choose your amount"
  link. Omit it (or leave it blank) to hide the custom option for that
  provider; the fixed tiers still work. The app opens the `"custom"`
  link **verbatim** — the payer enters the amount on the provider's
  page.
- Every URL must be **HTTPS**. Non-HTTPS or malformed links are rejected
  at launch time and never opened.
- Set a provider's `"Enabled"` to `false` (or leave its `PaymentLinks`
  empty) to hide it in the dialog.

### Creating the links

**Stripe** — in the Stripe Dashboard, create a **Payment Link** for a
product/price per tier:

1. Products → Payment Links → New.
2. For a fixed tier, set a fixed price (e.g. €10) and copy the
   `https://donate.stripe.com/...` URL into the matching key.
3. For the `"custom"` key, create a Payment Link whose price is set to
   **"customer chooses price"**, then copy its URL. No secret key is
   involved — only the public link.

**PayPal** — create a hosted **donate button** per tier:

1. PayPal → Donations / Buttons → create a button.
2. For a fixed tier, set a fixed amount and use the resulting hosted
   `https://www.paypal.com/donate/?hosted_button_id=...` URL.
3. For the `"custom"` key, create a donate button **without** a fixed
   amount so the payer chooses it on PayPal's page, and copy that URL.

Do not embed any API credential — only the hosted button URL belongs in
the file. After editing, restart MedReminder; the menu entry appears
once `Enabled` is `true` and at least one provider has valid links.

## 24. Cloud provider clients (maintainer)

Sync and cloud backup can use OneDrive and Google Drive (B.1 Phase 4a,
4b). Each needs an app registration owned by the maintainer.

**OneDrive**: the Microsoft Entra public client id is in the code
(`MsalCloudAccountService`); a public client has no secret. A different
registration can be set with `OneDrive:ClientId` in `appsettings.json`.

**Google Drive**: a Google Cloud OAuth client of type **Desktop app**,
with the Drive API enabled and the scopes `drive.file` and
`drive.appdata` on the consent screen. Google requires the client
secret in the token request even for installed apps and does not treat
it as confidential, but it is kept **out of the repository**:

1. **Local builds**: set two environment variables in the PowerShell
   session that runs `dotnet build` / `dotnet publish`:

   ```powershell
   $env:MEDREMINDER_GOOGLE_CLIENT_ID = "<client id>.apps.googleusercontent.com"
   $env:MEDREMINDER_GOOGLE_CLIENT_SECRET = "<client secret>"
   ```

   To keep them for new sessions and for Visual Studio, set them as user
   variables (`[Environment]::SetEnvironmentVariable(..., "User")`) and
   restart the terminal or the IDE. `MedReminder.Infrastructure.csproj`
   stamps them into the assembly (`AssemblyMetadata`) only when both are
   set; a build without them does not offer Google Drive. Check the
   result in the app (Tools → Sync… → Enable sync… lists Google Drive),
   not by printing the assembly attributes, which would show the secret.
2. **Releases**: in GitHub → Settings → Secrets and variables →
   Actions, create the repository secrets `MEDREMINDER_GOOGLE_CLIENT_ID`
   and `MEDREMINDER_GOOGLE_CLIENT_SECRET`.
   `.github/workflows/dotnet-desktop.yml` passes them to both
   `dotnet publish` steps (section 13). With the secrets missing the
   workflow still succeeds, and the release has no Google Drive.
3. **Local test without rebuilding**: `GoogleDrive:ClientId` and
   `GoogleDrive:ClientSecret` in the `appsettings.json` of the output
   folder (not `src/MedReminder.UI/appsettings.json`) override the
   stamped values.

The secret ends up inside the shipped assembly, where anyone can read
it: that is inherent to a desktop app, and Google does not treat the
secret of a Desktop client as confidential. Keeping it out of the
repository keeps it out of git history and secret scanners.

Before a release that offers Google Drive, publish the consent screen
(Google Auth Platform → Audience → **In production**): while it is in
Testing, refresh tokens expire after 7 days and users would have to sign
in again every week (spike S7, `ANALYSIS-B1-MOBILE-SYNC.md` §18.7).
