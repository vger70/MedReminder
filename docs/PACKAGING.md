# MedReminder — Packaging and Distribution

This document describes how to build, package, verify, and distribute MedReminder for end users.

## 1. Distribution Policy

The official MedReminder distribution channel is:

- **ZIP** — official distribution channel.

The alternative distribution channels are:

- **MSI** — optional alternative installer.
- **Microsoft Store** — the self-contained MSI, submitted to Partner
  Center as an MSI/EXE app (section 26).

**MSIX is not used**, also for the Microsoft Store.

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

Two MSIs are built from the same WiX project and UpgradeCode:

- `MedReminder-win-x64.msi`: framework-dependent, same binaries as
  `MedReminder-win-x64.zip`;
- `MedReminder-win-x64-net10.msi`: self-contained, same binaries as
  `MedReminder-win-x64-net10.zip`; the package submitted to the
  Microsoft Store (section 26).

Either one upgrades the other: installing one after the other replaces
the installed files.

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
WiX build over the self-contained output --> MedReminder-win-x64-net10.msi
   |
   v
Create the GitHub Release with the four assets
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
- The Microsoft Store MSI is built from the self-contained publish
  folder with `--no-incremental`: both MSI builds share
  `packaging/wix/obj`, and an incremental build could reuse the harvest
  of the other folder.

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
MedReminder-win-x64-net10.msi   installer, self-contained (Microsoft Store)
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
                  +-----------+-----------+-----------------------+
                  |                       |                       |
                  v                       v                       v
             Official ZIP            Alternative MSI     Self-contained MSI
                  |                       |                       |
                  v                       v                       v
        GitHub Releases             GitHub Releases    GitHub Releases, URL
                  |                                    submitted to the
                  |                                    Microsoft Store
                  v
       /releases/latest/download/
       MedReminder-win-x64.zip
```

The ZIP package is the canonical distribution artifact.

The MSI packages are optional convenience installers and must contain the same production application.

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

## 25. Code Signing (Certum SimplySign)

Release binaries are signed with a Certum Open Source Code Signing
certificate held in Certum's cloud HSM (SimplySign). No private key
exists on disk, so there is no `.pfx` to protect.

### Local signed build

Prerequisites (Windows):

- .NET 10 SDK and the Windows SDK (`signtool.exe`);
- SimplySign Desktop installed and connected (token from the
  SimplySign mobile app); the certificate then appears in
  `Cert:\CurrentUser\My`;
- the certificate SHA-1 thumbprint:
  `Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Format-List Subject, Thumbprint, NotAfter`.

Command:

```powershell
.\release.ps1 1.2.0 -LocalBuild -CertificateThumbprint <sha1>
# or: $env:CERTUM_CERT_THUMBPRINT = '<sha1>'; .\release.ps1 1.2.0 -LocalBuild
```

The local build mirrors the CI packaging and writes to `dist\<version>\`
(ignored by Git): both ZIP packages, both MSIs and `SHA256SUMS.txt`.
Only MedReminder's own binaries (`MedReminder*.exe`, `MedReminder*.dll`)
are signed, before being zipped or harvested into the MSI; the MSI is
signed afterwards. The Microsoft Store MSI is the exception: it is
harvested from a copy of the self-contained output in which every
`.exe`/`.dll` without a valid signature is also signed (section 26);
the ZIPs keep the vendors' files untouched. `-NoSign` produces an
unsigned build to test the chain; `-SkipMsi` skips both WiX builds.

Every signature uses SHA-256 and an RFC 3161 timestamp
(`http://time.certum.pl` by default, `-TimestampUrl` to override), so
signed files remain valid after the certificate expires.

Verification:

```powershell
signtool verify /pa /v dist\1.2.0\MedReminder-win-x64.msi
```

The output must report "Successfully verified" and "The signature is
timestamped".

### Relationship with CI

The tag-triggered workflow (§12) still publishes unsigned packages.
Until CI signing is enabled, replace the release assets with the
locally signed ones from `dist\<version>\`.

`publish-signed-release.ps1` runs the whole sequence:

```powershell
.\publish-signed-release.ps1 2.12.1
```

1. checks that `gh` is logged in, the certificate is in the store, the
   working tree is clean and the tag is unused;
2. `git checkout main`, `git pull`, `.\release.ps1 <version>` (commit,
   push, tag);
3. waits for the CI run on the tag commit (`gh run watch`), then checks
   out the tag;
4. `.\release.ps1 <version> -LocalBuild` and `signtool verify` on both
   MSIs;
5. `gh release upload --clobber` of the signed ZIPs, MSIs and
   `SHA256SUMS.txt`, keeping the release and its notes;
6. publishes the signed Store MSI on the `store` branch served by GitHub
   Pages (section 26), unless `-SkipStorePages`;
7. returns to `main`, also on failure.

If signing or upload fails after the tag is pushed, fix the cause and
resume with `-SkipGitRelease`, which starts from the tag checkout. Run
`.\publish-signed-release.ps1 -Help` for all options.

## 26. Microsoft Store (MSI/EXE submission)

MedReminder is listed in the Microsoft Store as an **MSI/EXE app**: the
Store does not host the package, it downloads the MSI from a URL given
in Partner Center. That URL must answer without redirection, so it
cannot be a GitHub release asset: `releases/download/...` answers 302
to a signed URL that expires after a few minutes, and Partner Center
rejects it ("The package URL redirects to another URL"). The Store MSI
is served by GitHub Pages instead. MSIX is not used (section 1): it would virtualize
writes to `%LOCALAPPDATA%\MedReminder\` and `HKCU\...\Run`, and the
Store would replace the GitHub update check.

### Store requirements and how they are met

| Requirement (Partner Center, MSI/EXE apps) | MedReminder |
| --- | --- |
| Versioned HTTPS URL without redirection, binary unchanged after submission | `https://vger70.github.io/MedReminder/X.Y.Z/MedReminder-win-x64-net10.msi` (GitHub Pages, below) |
| Standalone, offline installer | Self-contained publish: no separate .NET runtime |
| Silent install (a UAC prompt is allowed) | Windows Installer `/qn`; per-user install, no UAC |
| `.msi` or `.exe` only | `.msi` |
| The installer and every PE file in it signed by a CA of the Microsoft Trusted Root Program | Certum signature on the MSI and on every unsigned `.exe`/`.dll` (section 25) |

The Store MSI is the only package in which third-party files are
signed with the Certum certificate: a file that already carries a valid
vendor signature (for example the .NET runtime) keeps it; the others
(native SQLite, NuGet libraries without Authenticode) are signed, since
the Store rejects unsigned PE files.

The CI asset `MedReminder-win-x64-net10.msi` on the GitHub release is
unsigned until `publish-signed-release.ps1` replaces it; only the signed
MSI goes to GitHub Pages.

### GitHub Pages hosting

`publish-signed-release.ps1` (step 6, section 25) publishes the signed
Store MSI on the `store` branch:

```text
store
├── .nojekyll
├── 2.16.0/MedReminder-win-x64-net10.msi
└── 2.17.0/MedReminder-win-x64-net10.msi
```

- The branch holds a single parentless commit, replaced with
  `--force-with-lease` at each publish, like the `feeds` branch
  (`docs/CATALOGUE-DATA.md` §1.1): the repository does not grow by one
  MSI per release. It keeps the new version and the previous one,
  whose URL may still be in a submission under certification.
- A version already on the branch is never overwritten: the same MSI is
  skipped, a different one stops the script, since the Store requires
  the file behind a submitted URL not to change. Re-signing produces a
  different file, so a failed certification that needs a new MSI needs
  a new version (patch release).
- After the push the script waits up to 5 minutes for the URL to
  answer 200, and warns on a redirect.
- Git refuses files over 100 MB; the self-contained MSI must stay
  below that size.

One-time setup, in GitHub → Settings → Pages:

1. Source: **Deploy from a branch**, branch `store`, folder `/ (root)`.
   The branch exists after the first `publish-signed-release.ps1` run
   (`-SkipGitRelease` on an existing tag is enough).
2. No custom domain on `vger70.github.io`: with one, GitHub answers 301
   to the custom domain. In that case pass the custom domain's URL as
   `-StorePagesUrl`.

Check the URL before submitting it:

```powershell
curl.exe -sI https://vger70.github.io/MedReminder/X.Y.Z/MedReminder-win-x64-net10.msi
```

The first line must report status 200, not 301 or 302.

### Partner Center

One-time setup:

1. Open a developer account at `storedeveloper.microsoft.com`. An
   individual account is free and needs identity verification (an ID
   document and a selfie); a company account has a one-time fee.
2. Partner Center → Apps and games → New product → **EXE or MSI
   app**, then reserve the name `MedReminder`.

Every release:

1. Run `publish-signed-release.ps1 <version>` and check that the
   release contains the signed `MedReminder-win-x64-net10.msi`.
2. Start a new submission and fill in the pages:
   - **Pricing and availability**: free, markets.
   - **Properties**: category (Health & fitness or Productivity),
     privacy policy URL (MedReminder stores email settings and
     syncs to OneDrive / Google Drive).
   - **Age ratings**: the IARC questionnaire.
   - **Packages**: the GitHub Pages URL of the version, architecture x64,
     languages en, it, fr, es, de. For an MSI the Store installs with
     `/qn`; no other installer parameter is needed.
   - **Store listings**, one per language: description, at least one
     screenshot, logo. State that MedReminder is not a medical device.
   - **Submission options**, notes for certification: the app runs in
     the tray; donations (section 23) use third-party payment providers
     (Stripe, PayPal), which the Store policy allows for voluntary
     donations in non-game apps as long as the donation unlocks
     nothing. If Partner Center asks for this declaration on another
     page, give it there.
3. Submit and wait for certification.
4. Never replace the MSI of a submitted tag (see above).

### Updates

The Store does not update MSI/EXE apps by itself. Every release needs
a new submission with the new version's GitHub Pages URL. The in-app update check
(GitHub Releases) keeps working for Store installations; it may report
a version before the Store has certified it.

