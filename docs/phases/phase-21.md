# Phase 21 — Installer and Distribution

Status: implemented. Install/upgrade/uninstall checks are manual (see Manual validation).

## Goal

Create a repeatable Windows installation process: one artifact that installs the App and the Service, registers and configures the Service, and supports upgrades and uninstall. Replaces the manual `sc create` registration documented in Phase 17a.

## Packaging technology evaluation

| Option | Pros | Cons |
| --- | --- | --- |
| Inno Setup (selected) | Single script, free, service install via `sc` in `[Run]`/`[UninstallRun]`, in-place upgrades via `AppId`, simple command-line build (`ISCC.exe`) | Not MSI |
| WiX / MSI | Enterprise-standard MSI | Custom actions for service lifecycle; disproportionate for one local app |
| MSIX | Modern packaging | Package sandbox conflicts with a SYSTEM service, shared `%PROGRAMDATA%` database and named pipes |

Decision: **Inno Setup**, per-machine install (`PrivilegesRequired=admin`). Matches the local-first, simplest-tool constraints.

## Scope

In scope:

- Centralized version metadata: root `Directory.Build.props` (`<Version>`) applied to all projects and injected into the installer.
- Publish pipeline `build/publish.ps1`: `dotnet publish` of App + Service, `win-x64`, self-contained (no .NET runtime prerequisite), staged into `publish/`.
- `installer/ControlTime.iss`:
  - Fixed `AppId` (upgrade identity), install to `%PROGRAMFILES%\ControlTime`, per-machine.
  - Service registration on install: `sc create ControlTimeService ... start= delayed-auto obj= LocalSystem` + `sc failure ... restart/60000 ×3` + `sc start`.
  - Upgrade: same `AppId` → in-place; Service stopped (and running App closed) before file copy and restarted after; `%PROGRAMDATA%\ControlTime` (database + logs) untouched.
  - Uninstall: `sc stop` + `sc delete`, remove program files; `%PROGRAMDATA%\ControlTime` preserved by default, deleted only after an explicit yes/no confirmation during the uninstall wizard.
  - Startup: no global autostart installed; the Phase 15 in-app per-user toggle remains the only startup mechanism. Installer adds Start Menu shortcuts and a "Launch ControlTime" post-install option only.
  - Kill switch untouched (`CONTROLTIME_ENFORCEMENT` remains a Service-environment escape hatch).
- `docs/phases/phase-21.md` + `AGENTS.md` state update.

Out of scope (Phase 22+): code signing, automatic update mechanism, winget/MSI packaging, ARM64, framework-dependent variant.

## Plan

1. `Directory.Build.props` at repository root: `Version` 1.0.0 (flows to AssemblyVersion/FileProductVersion; installer reads it).
2. `build/publish.ps1`:
   - `dotnet publish src/ControlTime.App -c Release -r win-x64 --self-contained true -o publish/App`
   - `dotnet publish src/ControlTime.Service -c Release -r win-x64 --self-contained true -o publish/Service`
   - Fail on non-zero exit codes; print output paths.
3. `installer/ControlTime.iss`:
   - `[Setup]`: `AppId={{GUID-fixed}}`, `DefaultDirName={autopf}\ControlTime`, `PrivilegesRequired=admin`, `ArchitecturesAllowed=x64compatible`, `OutputBaseFilename` with version.
   - `[Files]`: `publish\App\*` → `{app}`; `publish\Service\*` → `{app}\Service`.
  - `[Code]`: `PrepareToInstall` stops the Service (`sc stop`, waits) and closes a running App (`taskkill /F /IM ControlTime.App.exe`) before file copy.
  - `[Run]`: `sc create` / `sc failure` / `sc start` (exit codes ignored — repair path tolerates error 1073 "already exists"), optional launch (`runascurrentuser`).
  - `[UninstallRun]`: `sc stop` then `sc delete` (`runhidden`; missing-service exit codes ignored).
  - `[Code]` uninstall flow: `InitializeUninstallProgressForm` asks yes/no (default No) whether to also delete `%PROGRAMDATA%\ControlTime`; `CurUninstallStepChanged(usPostUninstall)` performs the deletion only on explicit yes (Inno `[Tasks]` are not offered during uninstall, so no `[Tasks]`/`[UninstallDelete]` entry is used).
4. Verification: `dotnet test`, `dotnet build`, run `publish.ps1` end-to-end (publish + `ISCC.exe` compile).

## Manual validation

1. Clean machine: run `ControlTime-Setup-<version>.exe` → `%PROGRAMFILES%\ControlTime` with App + `Service\`, Start Menu shortcuts; `sc query ControlTimeService` → RUNNING (delayed-auto), failure actions `restart/60000 ×3`.
2. Exhaust a rule's allowance with the App closed → Service installed by the installer locks the console (same behaviour as manual `sc create` from Phase 17a).
3. Upgrade: run the new installer over the previous one → database, admin password, rules and history intact; Service running again afterwards.
4. Uninstall → Service stopped and deleted, program files removed, `%PROGRAMDATA%\ControlTime` preserved (removed only if the checkbox was ticked).
5. Second instance of the installer → repair/modify path does not corrupt the running Service (files replaced after stop).

## Manual validation

1. Clean machine: run `ControlTime-Setup-1.0.0.exe` → `%PROGRAMFILES%\ControlTime` with App + `Service\`, Start Menu shortcuts; `sc query ControlTimeService` → RUNNING (delayed-auto), failure actions `restart/60000 ×3`.
2. Exhaust a rule's allowance with the App closed → Service installed by the installer locks the console (same behaviour as manual `sc create` from Phase 17a).
3. Upgrade: run the new installer over the previous one → database, admin password, rules and history intact; Service running again afterwards.
4. Uninstall → Service stopped and deleted, program files removed, `%PROGRAMDATA%\ControlTime` preserved (removed only after answering "Sí" to the confirmation dialog).
5. Re-run the installer over an existing install (repair/upgrade path) → Service stopped before copy, files replaced, Service restarted; App closed if it was running.

Note: `sc create ... start= delayed-auto` is accepted by `sc.exe` (verified during this phase; a syntax check reached the SCM permission stage, and Phase 17a used the same form successfully).

## Implementation

- `Directory.Build.props` (repository root): `<Version>1.0.0</Version>` + `Product`/`Copyright`; applies to all five projects (verified: `FileVersion 1.0.0.0` on both published executables).
- `build/publish.ps1`: reads `<Version>` from `Directory.Build.props`, cleans `publish\`, publishes App → `publish\App` and Service → `publish\Service` (`-c Release -r win-x64 --self-contained true`), then compiles the installer with `ISCC.exe` (searched: `PATH`, `%LOCALAPPDATA%\Programs\Inno Setup 6`, `Program Files[ (x86)]\Inno Setup 6`) passing `/DMyAppVersion` and `/DPublishDir`; `-SkipInstaller` switch skips the ISCC step; every failing step throws.
- `installer/ControlTime.iss`: as planned above; Spanish UI (`compiler:Languages\Spanish.isl`); fixed `AppId` GUID; per-machine (`PrivilegesRequired=admin`, `{autopf}`); `CloseApplications=no` with explicit stop/kill in `PrepareToInstall`; `[Run]` sc registration trio + post-install launch; `[UninstallRun]` sc stop/delete; data-deletion confirmation dialog in `[Code]` (yes/no, default No) applied at `usPostUninstall`.
- `.gitignore`: `dist/` added (`publish/` was already covered by the ClickOnce rule).
- Inno Setup 6.7.3 installed on the development machine via `winget install JRSoftware.InnoSetup` (build-time dependency only).

## Tests

257 tests green (`dotnet test`), solution build clean (`dotnet build`). No new automated tests (no new Core logic).

- `build/publish.ps1` verified end-to-end: `publish\App` (~180 MB) and `publish\Service` (~87 MB) produced; `dist\ControlTime-Setup-1.0.0.exe` (~80 MB, lzma2) compiled successfully.

## Decisions

- Inno Setup over MSI/MSIX (see evaluation).
- Self-contained publish: no runtime prerequisite; size accepted for v1 (framework-dependent variant deferred).
- Service binary lives in `{app}\Service` (separate publish folder avoids publish collisions between WPF App and Worker).
- `%PROGRAMDATA%\ControlTime` is user-visible state: never deleted silently; deletion is an explicit opt-in confirmation during uninstall.
- No global autostart key (HKLM Run) installed — per-user preference stays in-app (Phase 15).
- Installer exit codes from `sc.exe` are deliberately not checked (repair path: 1073/1056 are benign).
- App process force-closed via `taskkill` on upgrade instead of Inno `CloseApplications` (the App does not register with the restart manager).

## Known limitations

- x64 only.
- No code signing → SmartScreen/Defender warnings on fresh machines.
- No auto-update: upgrades are re-running the new installer.
- Inno Setup is a build-time developer dependency, not redistributed or vendored.
- Setup executable produced but full install/upgrade/uninstall checklist above still requires execution on a target machine.
