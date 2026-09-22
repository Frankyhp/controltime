# Redesign Phase 01 — Visual Design System (PLAN — not implemented)

Status: planned, not started. This file is the implementation plan; on completion it will be condensed into `docs/phases/redesign-phase-01.md`.

Presentation-only. No change to application behaviour, domain logic, persistence, services, rule evaluation, authentication behaviour, usage tracking or Windows integration. Phase 20 — Reliability and Recovery remains the next functional phase.

## Goal

Make ControlTime look like a modern Windows desktop utility: clean, minimal, Segoe UI, light neutral background, white card surfaces, subtle borders, 6–8 px corner radii, consistent spacing, restrained indigo/blue accent, green/amber/red for allowed/warning/blocked.

## Constraints

- No third-party UI framework or new NuGet packages. No `WPF-UI` / `ControlStyles` / LibraryPreview.
- No rewrite of functional code for styling. Bindings, `x:Name`s used by code-behind, `Click=` handlers, `UpdateSourceTrigger`, `IsEnabled={Binding CanEdit}` and `Visibility` + `BoolToVis` converters stay exactly as they are.
- No ViewModel, Core, Infrastructure or Service changes. No new functionality from future phases.
- Plain WPF `ResourceDictionary` + `ControlTemplate` only.
- Preserve accessibility: AA contrast, visible keyboard focus, no font size below 12.

## Decisions

- Phase identifier: `redesign-phase-01` (does not renumber Phase 20/21/22).
- Icons: glyphs from `FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"` (Windows 11 preferred, Windows 10 fallback). Only glyphs present in both fonts are used.

## Current state (verified in code)

- `App.xaml`: `Application.Resources` empty → styling duplicated per View with hardcoded colors (`#FFB9C4D4`, `#FF5A6B80`, `#FFF6E3B8`, `#FFB71C1C`…).
- `MainWindow.xaml`: `DockPanel` + top `Border` `#FFE8EEF6` with 4 `RadioButton` (`IsChecked={Binding IsXSelected, Mode=OneWay}` + `ShowXCommand`), `DataTemplate` per VM.
- `DashboardView.xaml`: local styles `Card`/`CardLabel`/`CardValue`; access state is a large 34 px bordered area; 6 cards of equal hierarchy in a `WrapPanel` (including "Tiempo restante"); errors shown as a red `TextBlock` inside the state area (`HasError`/`ErrorMessage`).
- `RulesView.xaml`: 4 hardcoded alert `Border`s + master/detail `Grid` (ListBox 320 px / `GroupBox` editor); days are 7 plain `CheckBox`es; delete uses inline `Background="#FFF0B4B4"`.
- `HistoryView.xaml`: default `ListView`+`GridView`, inline borders, plain "Actualizar" button.
- `SettingsView.xaml`: 3 classic `GroupBox`es + 3 hardcoded banners.
- `AdminExitWindow.xaml`: default buttons/`PasswordBox`.
- Must preserve: `SetupPasswordBox`, `SetupConfirmPasswordBox`, `UnlockPasswordBox`, `GrantMinutesBox`, `GrantPasswordBox`, `CurrentPasswordBox`, `NewPasswordBox`, `ConfirmPasswordBox`, `PasswordBoxControl`, `ErrorText`, and every `Click=` handler.

## Scope

### R1 — New resource dictionaries in `src/ControlTime.App/Styles/`

Included automatically as `Page` items by the `UseWPF` SDK glob (no `.csproj` change).

| File | Content |
|---|---|
| `Colors.xaml` | `AppBackgroundBrush` `#F3F4F6`, `SurfaceBrush` `#FFFFFF`, `SurfaceSubtleBrush` `#FAFBFC`, `BorderBrush` `#E3E6EA`, `TextPrimaryBrush` `#1F2430`, `TextSecondaryBrush` `#5A6B80`, `AccentBrush` `#3949AB`, `AccentHoverBrush` `#303F9F`, `AccentSubtleBrush` `#EEF0FA`, `SuccessBrush` `#1B5E20` / `SuccessBg` `#E8F5E9`, `WarningBrush` `#E65100` / `WarningBg` `#FFF4E5`, `DangerBrush` `#B71C1C` / `DangerBg` `#FDECEA`, `InfoBg` `#EEF0FA`, `RowSelectedBrush`, `FocusVisualBrush` |
| `Typography.xaml` | Segoe UI base; `PageTitle`, `SectionTitle`, `FieldLabel`, `BodyText`, `HintText`, `CardLabel`, `MetricText` (22 SemiBold), `HeroMetricText` (44 Bold, `FontFeatureSettings="tnum"`), `ErrorText`, `StatusText` |
| `Buttons.xaml` | `PrimaryButton` (accent, radius 6), `SecondaryButton` (white + `BorderBrush`), `DestructiveButton` (red), `LinkButton`; own `ControlTemplate` with IsMouseOver / IsPressed / IsEnabled / IsKeyboardFocused triggers, no shadows |
| `Inputs.xaml` | `AppTextBox`, `AppPasswordBox` (template with `PART_ContentHost`, 6 px radius, accent focus), `AppCheckBox`, `DayChip` (CheckBox whose `ControlTemplate` renders a 14 px-radius chip: `AccentSubtleBrush` fill + accent border when `IsChecked`, respects `IsEnabled`). `ComboBox` style **deferred** — the app has no ComboBox today |
| `Cards.xaml` | `Card` (white Border, `BorderBrush`, `CornerRadius="7"`, `Padding="16"`), `CardGrid`, `CalloutCard` (3 px left accent/warning border), `SectionHeader` |
| `Navigation.xaml` | `NavRailItem` (`RadioButton` `ControlTemplate`: 40 px row, radius 6, 3 px left accent bar + `AccentSubtleBrush` when selected, glyph + text, visible focus) and `NavRailPanel`; glyph `FontFamily` constant |
| `Tables.xaml` | `AppListView`, `AppGridViewColumnHeader` (`SurfaceSubtleBrush` background, `TextSecondaryBrush` 11 SemiBold, no 3D), `ListViewItem` style (34 px rows, subtle alternation, `RowSelectedBrush` + accent bar on selection, hover), `NumericCell` (`TextAlignment=Right` + `tnum`) for `GridViewColumn.ElementStyle` |
| `Banners.xaml` | `StatusBanner` base (compact Border, `Padding="12,8"`, radius 6, glyph + text) with `InfoBanner`, `WarningBanner`, `SuccessBanner`, `DangerBanner` variants; shared by Dashboard / Rules / Settings |

Glyphs: Dashboard `\uE80F`, Reglas `\uE90F`, Historial `\uE81C`, Ajustes `\uE713`, warning `\uE7BA`, success `\uE73E`, error `\uEA39`.

### R2 — Modified files

- `App.xaml`: `MergedDictionaries` with the 8 dictionaries + implicit styles for `Window`, `TextBlock`, `TextBox`, `PasswordBox`, `Button` (base = secondary look), `CheckBox`, `ListViewItem`, `GridViewColumnHeader`, so unstyled controls inherit the system with minimal diffs.
- `MainWindow.xaml`: 2-column `Grid` — 216 px left nav rail (`AppBackgroundBrush`, "ControlTime" title, 4 `RadioButton Style="{StaticResource NavRailItem}"` with glyph + text) and content column (`SurfaceBrush` Border + `ContentControl Content="{Binding Current}"`). `GroupName`, the four `IsChecked` bindings, the four commands and the `DataTemplate`s stay unchanged. Window background `AppBackgroundBrush`, sensible `MinHeight`/`MinWidth`.
- `Views/DashboardView.xaml`: drop local styles. Hero row (`UniformGrid Columns="2"`): access-state card (`StateText` with `StateBrush`, `ReasonText`, `NextAllowedText`) + primary metric card "Tiempo restante" (`HeroMetricText` on `RemainingText`). Then a `UniformGrid Columns="4"` of cards — Uso registrado hoy, Límite diario configurado, Bonus de hoy, Asignación efectiva — and a second row with Actividad (`ActivityBrush`) and Horario activo. `ErrorMessage` becomes a compact `DangerBanner` at the top (same `HasError`/`ErrorMessage` bindings, VM untouched).
- `Views/RulesView.xaml`: same `Grid`/`RowDefinitions`. The three alerts become `WarningBanner`/`InfoBanner`/`SuccessBanner`. "Tiempo extra para hoy" gets its own `Card` with `SectionHeader` and styled inputs/buttons. Master/detail: `GroupBox` → `Card` + `SectionHeader`, `ListBox` → `AppListView`, editor `IsEnabled` intact, `FieldLabel` labels. Days: 7 `CheckBox Style="{StaticResource DayChip}"` with the identical `IsChecked` bindings. Buttons: `Nueva`/`Cancelar` secondary, `Guardar` primary, `Eliminar` / `¿Eliminar definitivamente?` `DestructiveButton` (inline `Background` removed). `ErrorMessage`/`StatusMessage` → `DangerBanner`/`SuccessBanner`.
- `Views/HistoryView.xaml`: week-usage card becomes a prominent metric (34 px) with `Actualizar` (secondary) at the right; `ListView` → `AppListView` with `AppGridViewColumnHeader`, `Uso`/`Bonus` columns using `ElementStyle={StaticResource NumericCell}`; sessions in a card; error as `DangerBanner`.
- `Views/SettingsView.xaml`: the 3 `GroupBox`es become 3 `Card`s with `SectionHeader` — "Contraseña de administrador" (`IsEnabled="{Binding CanEdit}"`), "Arranque", "Seguridad y servicio" (read-only on `SurfaceSubtleBrush`). First-time password setup becomes a prominent `CalloutCard`/`WarningBanner`; unlock/admin banners as in Rules. Names and handlers preserved.
- `Views/AdminExitWindow.xaml`: button/input styles + error color from resource only.
- `ControlTime.App.csproj`: no change.

### R3 — Out of scope

Core, Infrastructure, Service, `App.xaml.cs` DI composition, `ServiceClientHost`, `TrayIconService`, `Presentation/` translators and all ViewModels stay untouched. `DashboardViewModel` keeps exposing `StateBrush`/`ActivityBrush`; their hardcoded RGB values are not modified (hierarchy is achieved with layout and card styles).

## Acceptance criteria

- `dotnet build` and `dotnet test` green (Core tests unchanged; no UI tests exist).
- Manual validation checklist passes.
- `docs/phases/redesign-phase-01.md` written; this plan reduced to a pointer.
- `AGENTS.md` gains an entry for this phase and still lists `Phase 20 — Reliability and Recovery` as the current/next phase.
- `docs/PLAN_CONTROLTIME.md` records the phase inserted after Phase 19 (Milestone C) without renumbering 20/21/22.

## Manual validation checklist

1. Dashboard: states Permitido / Advertencia / Bloqueado; Service stopped → compact banner instead of a large empty area; "Tiempo restante" reads as the dominant metric.
2. Rules: first-time setup callout, unlock panel, admin bar, day chips toggle on/off and persist, create/save/delete with confirmation, grant time (minutes + password) still works through code-behind.
3. History: day selection drives the sessions list; numeric columns right-aligned; week total prominent.
4. Settings: setup callout, change password, autostart toggle gated by admin mode, enforcement status read-only.
5. `AdminExitWindow`: Enter accepts, Escape cancels, password box styled.
6. Left navigation preserves selection and each View's `Loaded`/`Unloaded` behaviour.
7. Keyboard walk-through (Tab) on every screen: focus visible on nav items, chips, buttons; contrast checked on tinted banners.

## Known risks

- `Segoe Fluent Icons` is absent on Windows 10; the MDL2 fallback covers the 7 chosen glyphs.
- Re-templating `PasswordBox` requires keeping `PART_ContentHost`; code-behind `.Password` access is unaffected.
- A `CheckBox` chip template loses the native tick: compensated with accent fill/border + bold text when checked, keyboard accessible by default.
- Global implicit styles may affect controls not explicitly reviewed — audit all five screens plus `AdminExitWindow` after the first build.
