# Shared core: CI and release discipline

The shared core is the set of projects this repository shares with the
mobile apps, which live in a private repository (Android plan A7,
`docs/analysis/ANALYSIS-B1-ANDROID-PLAN.md` §4.8). Backlog B0-04.

| Project | Target |
|---|---|
| `src/MedReminder.Domain` | `net10.0` |
| `src/MedReminder.Application` | `net10.0` |
| `src/MedReminder.Infrastructure.Portable` | `net10.0` |

`MedReminder.SharedCore.slnf` lists these projects and their test
projects. It builds on any OS without `EnableWindowsTargeting`, which
keeps the Windows projects (`MedReminder.Infrastructure`,
`MedReminder.UI`) out of the core.

## CI

`.github/workflows/shared_core.yaml` runs
`dotnet test MedReminder.SharedCore.slnf -c Release` on Linux:

- on every pull request and push to `main` that touches the shared-core
  projects, their tests, the files they embed or copy
  (`scripts/feeds/regional_services_it.json`,
  `assets/localization/**`), `Directory.Build.props`, the filter or the
  workflow;
- on every `core-v*` tag, after checking the tag format and that the
  tagged commit is on `main`.

A shared-core pull request merges only with this check green. The
Windows suites (`Infrastructure.Tests`, `UI.Tests`) still run locally
on Windows (CLAUDE.md §3).

## Versions and tags

- Shared-core versions are tags `core-vMAJOR.MINOR.PATCH` on `main`,
  independent of the desktop's `vX.Y.Z`. They must not start with `v`:
  the desktop release workflow (`dotnet-desktop.yml`) triggers on `v*`.
- Core tags never get a GitHub Release. The desktop update check reads
  `releases/latest`, so a core release would be offered to desktop
  users as an update.
- Tags are annotated; the message lists the pull requests since the
  previous core tag.
- Version rules, for the public API of the three projects, the
  database schema and the formats (`EXPORT-FORMAT.md`,
  `SYNC-FORMAT.md`):
  - **MAJOR**: a breaking change: a removed or changed public member,
    a schema change an older app cannot open, a format change an older
    reader rejects.
  - **MINOR**: additions that keep existing callers and data working.
  - **PATCH**: fixes with no API, schema or format change.
- While the major version is `0` (until the first public Android
  release), a MINOR may break; the tag message says so.

## Release procedure

1. Merge the shared-core change to `main` through a pull request, with
   the Shared core check green.
2. Tag the merge commit and push the tag:

   ```bash
   git fetch origin main
   git tag -a core-v0.2.0 origin/main -m "core-v0.2.0: #221, #222"
   git push origin core-v0.2.0
   ```

3. Wait for the Shared core workflow on the tag to pass.
4. In the private repository, move the submodule to the tag and open a
   pull request there:

   ```bash
   git -C external/MedReminder fetch --tags origin
   git -C external/MedReminder checkout core-v0.2.0
   git add external/MedReminder
   ```

   The mobile CI rejects a submodule revision that is not a `core-v*`
   tag.

A change the app needs in the shared core is always made here first, in
a public pull request; the private repository never patches the
submodule.
