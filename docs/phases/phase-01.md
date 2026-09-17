# Phase 01 — Project Bootstrap

## Goal

Create the basic .NET solution and dependency structure without implementing any application functionality.

## Scope

* WPF application project (`src/ControlTime.App`)
* xUnit test project (`tests/ControlTime.Tests`)
* Solution membership and project references
* Nullable reference types and implicit usings in all projects
* Removal of template placeholder files

## Implementation

* `ControlTime.App`: .NET 10 WPF application (`net10.0-windows`, `UseWPF`), referencing `ControlTime.Core` and `ControlTime.Infrastructure`. Contains only the minimal template shell (`App.xaml`, `MainWindow.xaml`).
* `ControlTime.Tests`: xUnit test project referencing `ControlTime.Core`. No tests yet; tests begin in Phase 02.
* `ControlTime.Core` and `ControlTime.Infrastructure` kept unchanged except removal of `Class1.cs`. `ControlTime.Infrastructure` already referenced `ControlTime.Core`.
* Deleted `src/ControlTime.Core/Class1.cs`, `src/ControlTime.Infrastructure/Class1.cs` and the template `UnitTest1.cs`.
* No NuGet packages added beyond those required by the WPF and xUnit templates.

Resulting dependency direction:

```text
ControlTime.App ──► ControlTime.Core
ControlTime.App ──► ControlTime.Infrastructure ──► ControlTime.Core
ControlTime.Tests ──► ControlTime.Core
```

## Tests

No automated tests exist yet by design. `dotnet test` succeeds with zero tests.

## Manual validation

* `dotnet build`: succeeds with 0 warnings and 0 errors.
* `dotnet test`: exits with code 0.
* Solution structure and project references verified against the required dependency direction.

## Decisions

* The solution file uses the newer XML format `ControlTime.slnx` (already present) instead of a classic `.sln`. `AGENTS.md` was updated accordingly.
* `ControlTime.Tests` targets `net10.0` (not `-windows`) because Core is platform-neutral, keeping unit tests free of Windows dependencies.

## Known limitations

* `dotnet test` reports zero tests; the test pipeline is only exercised for real in Phase 02.
* The WPF application is an empty window shell with no behavior.
