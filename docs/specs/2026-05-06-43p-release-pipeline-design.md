# Stickies-43p — GitHub release pipeline design

**Date:** 2026-05-06
**Bead:** `Stickies-43p` (absorbs `Stickies-94r` MSI work, which was superseded)
**Status:** Design locked, plan pending.
**Repo:** [obselate/Stickies](https://github.com/obselate/Stickies) — public, GitHub Actions free tier (unlimited minutes, no Windows-runner multiplier)

## Goal

Ship Stickies from a tag instead of `dotnet publish` + manual upload. Two halves of one pipeline:

1. **PR/push gating** — every PR and every push to `main` runs the full AOT publish, fails on new warnings or size regressions. "Main is always shippable."
2. **Tag-driven release** — pushing `vX.Y.Z` produces a zipped `Stickies.exe` and a per-user MSI, attached to an auto-generated GitHub Release.

Out of scope for this round: smoke-test of the published binary on a fresh VM, code-signing (no cert yet), auto-update channel (explicitly banned in CLAUDE.md).

## Locked decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Heavy PR gating (Option B).** Every PR runs full AOT publish + size budget + IL warning gate on `windows-latest`. | Public repo → Actions are free, unlimited minutes, no Windows 2x multiplier. AOT regressions and new trim warnings are exactly what should not slip past review. ~3-5 min per PR is acceptable for solo low-volume cadence. |
| 2 | **Tag is the canonical version source (Option A).** csproj has no `<Version>` element. Workflow extracts from `${{ github.ref_name }}` and passes `/p:Version=X.Y.Z` to `dotnet publish` and the WiX build. | Single source of truth at release time. No "did I forget to bump csproj?" failure mode. Local dev builds carry the default 1.0.0.0; that's fine — they never ship. |
| 3 | **First release is `v0.1.0`. Strict SemVer only.** Tag regex `v[0-9]+\.[0-9]+\.[0-9]+`. No pre-release suffixes (`-rc1`, `-beta.2` etc.). | Keeps the pipeline simple. Pre-release flow can be added later if needed; YAGNI for now. |
| 4 | **Size budget: hard fail at 18 MB on `Stickies.exe`, hard fail at 30 MB on total `publish/` directory.** | Current baseline: `Stickies.exe` 15.89 MiB, total ship 28.27 MiB. ~13% headroom on the exe, small headroom on totals. CLAUDE.md target is `< 25 MB` exe; gate fires well before that. When a feature legitimately needs the headroom, bump the threshold in the same PR — that becomes the visible budget conversation. |
| 5 | **Release notes via `gh release create --generate-notes`.** | Solo project, commit-to-main flow; auto-generated notes from commit messages are honest and zero-maintenance. Hand-curate after the fact via `gh release edit` when needed. |
| 6 | **`workflow_dispatch` on release.yml for testing.** Manual trigger from Actions UI runs the whole release flow but skips `gh release create`. Artifacts uploaded as build artifacts (auto-expire 7 days) so you can download and inspect. | Lets us iterate on release.yml without burning real tags or making a public mess. Tiny added branch: `if: github.event_name == 'push'` on the create-release step. |
| 7 | **MSI shape** — WiX v5 SDK, `Scope="perUser"` (no UAC), installs to `%LOCALAPPDATA%\Programs\Stickies\`, single Start Menu shortcut "Stickies", registered uninstaller in per-user Add/Remove Programs, `MajorUpgrade` replaces older versions automatically (`AllowSameVersionUpgrades="no"`), no desktop shortcut, no file associations, no autostart. **User data at `%LOCALAPPDATA%\Stickies\` (the SQLite DB) is never touched by install or uninstall.** | Conservative "boring desktop app installer" — no surprises. Per-user scope avoids UAC. Plain MSI, not MSIX (MSIX explicitly banned in CLAUDE.md). |
| 8 | **IL warning gate strict in CI only (Option A).** csproj stays loose. Workflow passes `/p:TreatWarningsAsErrors=true /p:WarningsNotAsErrors=IL2104`. | Local builds keep fast loose-warning iteration; CI is the strict gate. Same end result for what ships. |

## Architecture

Two GitHub Actions workflows + one WiX MSBuild project. Existing `Stickies.csproj`, source files, `.gitignore`, etc. unchanged.

### File structure

```
.github/
  workflows/
    ci.yml            # PR + push-to-main: build, gate, no artifacts published
    release.yml       # tag v*.*.* + workflow_dispatch: build, gate, package, publish
installer/
  Stickies.wixproj    # WiX v5 MSBuild SDK project
  Product.wxs         # MSI definition
```

### Trigger matrix

| Trigger | Workflow | Publishes GitHub Release? |
|---|---|---|
| PR opened/synchronized targeting `main` | ci.yml | no |
| Push to `main` (post-merge) | ci.yml | no |
| Push tag matching `v[0-9]+.[0-9]+.[0-9]+` | release.yml | yes |
| Manual `workflow_dispatch` on release.yml | release.yml | no — artifacts only |

### Concurrency

- **PR runs**: `concurrency.group: ci-${{ github.ref }}`, `cancel-in-progress: true` — pushing to a PR cancels prior in-flight runs of that PR.
- **Push-to-main runs**: do not cancel each other. Two main-pushes in quick succession both run.
- **Release runs**: do not cancel each other. A second tag push during an in-flight release waits, doesn't preempt.

### Caching

NuGet via `actions/setup-dotnet`'s built-in cache (`cache: true`, `cache-dependency-path: '**/packages.lock.json'` if a lockfile exists; otherwise hash of csproj). AOT compile cache is deferred — only worth it if total job time becomes painful (currently ~3-5 min, acceptable).

## ci.yml

```yaml
name: ci
on:
  pull_request:
    branches: [main]
  push:
    branches: [main]

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: ${{ github.event_name == 'pull_request' }}

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
          cache: true
          cache-dependency-path: 'Stickies.csproj'

      - name: Restore
        run: dotnet restore

      - name: Publish AOT (with warning gate)
        run: |
          dotnet publish -c Release -r win-x64 --no-restore `
            -p:TreatWarningsAsErrors=true `
            -p:WarningsNotAsErrors=IL2104

      - name: Size budget
        shell: pwsh
        run: |
          $publishDir = "bin\Release\net9.0\win-x64\publish"
          $exe = (Get-Item "$publishDir\Stickies.exe").Length
          $dir = (Get-ChildItem $publishDir -Recurse | Measure-Object Length -Sum).Sum
          Write-Host "Stickies.exe: $([math]::Round($exe/1MB,2)) MB  /  publish/: $([math]::Round($dir/1MB,2)) MB"
          if ($exe -gt 18MB) { throw "Stickies.exe = $exe bytes (> 18MB cap)" }
          if ($dir -gt 30MB) { throw "publish/ total = $dir bytes (> 30MB cap)" }

      - name: Build MSI (validation only — no upload)
        run: |
          dotnet build installer/Stickies.wixproj -c Release `
            -p:HarvestPath=bin\Release\net9.0\win-x64\publish
```

### Failure modes that block PR merge

- New compiler warning anywhere → `TreatWarningsAsErrors`
- New IL trim warning (other than IL2104) → `TreatWarningsAsErrors`
- `Stickies.exe > 18 MB` → size budget step throws
- `publish/` total `> 30 MB` → size budget step throws
- WiX project fails to harvest or build → installer build step fails

## release.yml

```yaml
name: release
on:
  push:
    tags: ['v[0-9]+.[0-9]+.[0-9]+']
  workflow_dispatch:

concurrency:
  group: release-${{ github.ref }}
  cancel-in-progress: false

jobs:
  release:
    runs-on: windows-latest
    permissions:
      contents: write   # for gh release create
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0   # gh --generate-notes needs full history + prior tags

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
          cache: true
          cache-dependency-path: 'Stickies.csproj'

      - name: Extract version from tag
        shell: pwsh
        run: |
          $tag = "${{ github.ref_name }}"
          if ($tag -match '^v(\d+\.\d+\.\d+)$') {
            $version = $Matches[1]
          } else {
            # workflow_dispatch path
            $version = "0.0.0-dev"
          }
          Write-Host "VERSION=$version"
          "VERSION=$version" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8

      - name: Restore
        run: dotnet restore

      - name: Publish AOT (with warning gate, version-stamped)
        run: |
          dotnet publish -c Release -r win-x64 --no-restore `
            -p:Version=$env:VERSION `
            -p:TreatWarningsAsErrors=true `
            -p:WarningsNotAsErrors=IL2104

      - name: Size budget
        shell: pwsh
        run: |
          $publishDir = "bin\Release\net9.0\win-x64\publish"
          $exe = (Get-Item "$publishDir\Stickies.exe").Length
          $dir = (Get-ChildItem $publishDir -Recurse | Measure-Object Length -Sum).Sum
          if ($exe -gt 18MB) { throw "Stickies.exe = $exe bytes (> 18MB cap)" }
          if ($dir -gt 30MB) { throw "publish/ total = $dir bytes (> 30MB cap)" }

      - name: Build MSI
        run: |
          dotnet build installer/Stickies.wixproj -c Release `
            -p:Version=$env:VERSION `
            -p:HarvestPath=bin\Release\net9.0\win-x64\publish

      - name: Package zip
        shell: pwsh
        run: |
          $zip = "Stickies-$env:VERSION-win-x64.zip"
          Compress-Archive -Path "bin\Release\net9.0\win-x64\publish\*" -DestinationPath $zip
          "ZIP_PATH=$zip" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
          "MSI_PATH=installer\bin\Release\Stickies-$env:VERSION.msi" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8

      - name: Upload artifacts (always, for inspection)
        uses: actions/upload-artifact@v4
        with:
          name: stickies-${{ env.VERSION }}
          path: |
            ${{ env.ZIP_PATH }}
            ${{ env.MSI_PATH }}
          retention-days: 7

      - name: Create GitHub Release
        if: github.event_name == 'push'
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          gh release create "${{ github.ref_name }}" `
            "$env:ZIP_PATH" `
            "$env:MSI_PATH" `
            --title "Stickies $env:VERSION" `
            --generate-notes
```

### Tag-push vs workflow_dispatch behavior

| Step | tag push | workflow_dispatch |
|---|---|---|
| Version | extracted from tag | `0.0.0-dev` |
| Build | yes | yes |
| Gates | yes | yes |
| Zip + MSI artifacts | yes | yes |
| Upload as Actions artifact (7-day expiry) | yes | yes |
| `gh release create` | **yes** | **skipped** |

### Failure modes that block release

- Same gates as ci.yml (warnings, IL, size)
- WiX project fails to build
- `gh release create` fails — typically because tag already has a Release; resolve by deleting the broken Release and re-pushing the tag (rare; mostly hits during pipeline iteration)

## WiX project

### `installer/Stickies.wixproj`

```xml
<Project Sdk="WixToolset.Sdk/5.0.0">
  <PropertyGroup>
    <OutputType>Package</OutputType>
    <OutputName>Stickies-$(Version)</OutputName>
    <SuppressIces>ICE61</SuppressIces>
  </PropertyGroup>

  <ItemGroup>
    <HarvestDirectory Include="$(HarvestPath)">
      <ComponentGroupName>HarvestedFiles</ComponentGroupName>
      <DirectoryRefId>INSTALLFOLDER</DirectoryRefId>
    </HarvestDirectory>
  </ItemGroup>
</Project>
```

(`SuppressIces=ICE61` lets `MajorUpgrade` allow same-version upgrades being disabled cleanly without an ICE warning. `OutputName` interpolates the version so the MSI filename matches the release version.)

### `installer/Product.wxs`

```xml
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Stickies"
           Manufacturer="obselate"
           Version="$(Version)"
           UpgradeCode="PUT-A-STABLE-GUID-HERE"
           Scope="perUser"
           InstallerVersion="500">

    <MajorUpgrade Schedule="afterInstallInitialize"
                  AllowSameVersionUpgrades="no"
                  DowngradeErrorMessage="A newer version of Stickies is already installed." />

    <StandardDirectory Id="LocalAppDataFolder">
      <Directory Id="ProgramsFolder" Name="Programs">
        <Directory Id="INSTALLFOLDER" Name="Stickies" />
      </Directory>
    </StandardDirectory>

    <StandardDirectory Id="ProgramMenuFolder">
      <Component Id="StartMenuShortcut" Guid="*">
        <Shortcut Id="StickiesShortcut"
                  Name="Stickies"
                  Target="[INSTALLFOLDER]Stickies.exe"
                  WorkingDirectory="INSTALLFOLDER" />
        <RegistryValue Root="HKCU"
                       Key="Software\obselate\Stickies"
                       Name="installed"
                       Type="integer"
                       Value="1"
                       KeyPath="yes" />
      </Component>
    </StandardDirectory>

    <Feature Id="Main">
      <ComponentGroupRef Id="HarvestedFiles" />
      <ComponentRef Id="StartMenuShortcut" />
    </Feature>
  </Package>
</Wix>
```

### `UpgradeCode`

A single GUID generated once at project setup time and committed verbatim to `Product.wxs`. **Never changes across versions.** Windows uses it to recognize "this is the same product, upgrade rather than install side-by-side." Generate with PowerShell `[guid]::NewGuid().ToString().ToUpper()` and paste in.

### Version property

`Version` is supplied by the build via `/p:Version=...`. WiX uses `$(Version)` (MSBuild property substitution). When `dotnet build installer/Stickies.wixproj -p:Version=0.1.0` runs, both the MSI ProductVersion and the output filename `Stickies-0.1.0.msi` get the same value.

### Per-user data preservation

The MSI only installs/removes program files under `%LOCALAPPDATA%\Programs\Stickies\`. The application's data directory `%LOCALAPPDATA%\Stickies\` (containing `notes.db`) is created at runtime by the app and is never referenced by the installer. Uninstall + reinstall preserves all notes.

## Versioning flow

```
Developer pushes tag v0.1.0
        │
        ▼
release.yml on: push tags fires
        │
        ▼
Extract step:  ${{ github.ref_name }} = "v0.1.0"
               regex match → VERSION="0.1.0"  → $env:GITHUB_ENV
        │
        ▼
Publish:    dotnet publish -p:Version=0.1.0
            → Stickies.exe carries 0.1.0 in version resource
        │
        ▼
MSI build:  dotnet build installer/Stickies.wixproj -p:Version=0.1.0
            → Stickies-0.1.0.msi with ProductVersion=0.1.0
        │
        ▼
Release:    gh release create v0.1.0 ... --generate-notes
            → public Release "Stickies 0.1.0" with both assets
```

## Manual verification (no test framework)

After both workflows are in place, validate end-to-end before tagging `v0.1.0`:

1. **CI on PR**: open a no-op PR (e.g. whitespace change). Confirm ci.yml runs, all gates pass, status check appears on the PR.
2. **CI failure path — size**: temporarily lower the size cap to 1MB in a branch, push as PR. Confirm CI fails with the budget message. Revert.
3. **CI failure path — warning**: introduce an unused-variable warning in a branch. Confirm CI fails on `TreatWarningsAsErrors`. Revert.
4. **CI on push to main**: merge the no-op PR. Confirm ci.yml runs again on `main`.
5. **release.yml via workflow_dispatch**: trigger manually from Actions UI. Confirm full pipeline runs, artifacts uploaded, **no GitHub Release created**.
6. **MSI sanity** (download the workflow_dispatch artifact onto a Windows box):
   - Double-click installs without UAC prompt
   - Stickies appears in Start Menu under "Stickies"
   - Add/Remove Programs (per-user list) shows Stickies with version `0.0.0-dev`
   - Launch via shortcut works; existing notes still load
   - Uninstall via Add/Remove Programs removes program files; `%LOCALAPPDATA%\Stickies\notes.db` survives
7. **release.yml via tag push**: push `v0.1.0`. Confirm:
   - Pipeline runs green
   - GitHub Release "Stickies 0.1.0" created with auto-generated notes
   - Both `Stickies-0.1.0-win-x64.zip` and `Stickies-0.1.0.msi` attached
   - MSI ProductVersion = 0.1.0
   - Stickies.exe inside the zip reports 0.1.0 in file properties
8. **Upgrade behavior**: install 0.1.0, then later install 0.1.1 (when it exists). Confirm the older version is removed automatically and no second entry appears in Add/Remove Programs.

## Out of scope / explicitly deferred

- **Smoke-test of published binary on a fresh runner / VM.** Booting the published exe in Actions is doable (start process, wait, kill) but adds complexity; defer to its own bead.
- **Code-signing.** No EV cert yet. SmartScreen will warn on first download; that's acceptable for v0.1.0. When a cert is available, add a signing step between "Build MSI" and "Package zip" in release.yml.
- **Auto-update channel.** CLAUDE.md banned-additions list explicitly rules out MSIX/Velopack/Squirrel/Inno. Releases are the only update channel; users get notified by visiting the GitHub Releases page.
- **Pre-release tags (`v0.1.0-rc1`).** Strict SemVer only for now. Add later if the cadence demands it.
- **AOT compile cache.** `actions/cache` on the AOT intermediate output. Worth ~30-90s per run if it works cleanly. Add only if the 3-5 min job time becomes painful.
- **Format/style check.** No `dotnet format --verify-no-changes` step. The codebase is small enough that style drift isn't a concern; warnings + IL trim warnings are the meaningful gates.

## Key invariants

1. **The tag is the version.** csproj has no `<Version>`. Workflow extracts and passes through. Don't add a csproj `<Version>` "for clarity" — it creates a two-source-of-truth bug waiting to happen.
2. **`UpgradeCode` GUID never changes.** It's the stable identity Windows uses to recognize upgrades. If it ever rotates, users end up with two copies of Stickies in Add/Remove Programs.
3. **MSI never touches `%LOCALAPPDATA%\Stickies\`.** That directory is owned by the app at runtime. The installer only manages program files.
4. **`workflow_dispatch` never publishes a GitHub Release.** The `if: github.event_name == 'push'` guard on the create-release step is what keeps manual testing from leaking junk releases.
5. **IL2104 is the only allowlisted IL warning.** If a new IL warning becomes unavoidable, add it to `WarningsNotAsErrors` in the workflow with a comment explaining why — don't blanket-disable the gate.
6. **CI runs the same publish flow as release.** The whole point of heavy PR gating is "main is shippable"; if CI ever short-circuits to a faster path, that guarantee is gone.

## References

- WiX v5 docs: <https://docs.firegiant.com/wix/>
- GitHub Actions concurrency: <https://docs.github.com/en/actions/using-jobs/using-concurrency>
- `gh release create`: <https://cli.github.com/manual/gh_release_create>
- Bead: `bd show Stickies-43p`
- Prior handoff: [docs/plans/2026-05-06-jl1-uat-pending.md](../plans/2026-05-06-jl1-uat-pending.md)
