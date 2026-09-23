# Redesign Phase 01 — Visual Design System

## Goal

Presentation-only redesign: make the WPF UI look like a modern Windows desktop utility using plain WPF `ResourceDictionary` + `ControlTemplate` only. No behaviour, domain, persistence, service or Windows-integration changes. No new packages.

## Scope

New `src/ControlTime.App/Styles/` resource dictionaries, merged in `App.xaml`, plus a restyled shell (`MainWindow`) and five screens. All bindings, `x:Name`s, `Click=` handlers, `UpdateSourceTrigger`, `IsEnabled` and `Visibility`+`BoolToVis` behaviour preserved exactly.

## Implementation

- `Styles/Colors.xaml`: palette (neutral background, white surfaces, indigo accent, success/warning/danger pairs, row-selection and focus brushes).
- `Styles/Typography.xaml`: Segoe UI scale (`PageTitle`, `SectionTitle`, `FieldLabel`, `CardLabel`, `MetricText`, `HeroMetricText` with tabular figures via `Typography.NumeralAlignment=Tabular`, `ErrorText`, `StatusText`, `HintText`). Nothing below font size 12.
- `Styles/Buttons.xaml`: `PrimaryButton`, `SecondaryButton`, `DestructiveButton`, `LinkButton` over one `ControlTemplate` (radius 6, hover/pressed/disabled/focus triggers, shared `AppFocusVisualStyle`).
- `Styles/Inputs.xaml`: `AppTextBox` / `AppPasswordBox` templates with `PART_ContentHost` and accent focus border; `AppCheckBox`; `DayChip` (14 px-radius chip: accent fill/border + bold accent text when checked, focus ring).
- `Styles/Cards.xaml`: `Card`, `CardGrid` (negative-margin `UniformGrid`), `CalloutCard` (3 px left warning border), `SectionHeader`.
- `Styles/Navigation.xaml`: `IconFont` (`Segoe Fluent Icons, Segoe MDL2 Assets`), `NavRailItem` (40 px row, accent bar + subtle fill when checked, visible focus), `NavRailPanel`.
- `Styles/Tables.xaml`: `AppListView`, `AppListViewItem` (34 px rows, alternation, hover, `RowSelectedBrush` + accent bar), `AppGridViewColumnHeader` (flat, `TextSecondaryBrush` SemiBold at font size 12), `NumericCell` (right-aligned tabular figures).
- `Styles/Banners.xaml`: plain `Border` styles only (`StatusBanner` base + `InfoBanner`/`WarningBanner`/`SuccessBanner`/`DangerBanner`); icon and text are composed explicitly per usage. No custom control was introduced for banners.
- `App.xaml`: merged dictionaries + implicit styles for `Window`, `TextBlock`, `TextBox`, `PasswordBox`, `Button` (secondary look), `CheckBox`, `ListViewItem`, `GridViewColumnHeader`.
- `MainWindow.xaml`: 216 px left nav rail with the four navigation `RadioButton`s (glyph + text; original `GroupName`, `IsChecked` bindings and commands unchanged) + white rounded content surface. `MinHeight`/`MinWidth` added.
- `DashboardView.xaml`: compact `DangerBanner` error on top; hero row (state + "Tiempo restante" as 44 px dominant metric); 4-metric row; Actividad / Horario row. `StateBrush`/`ActivityBrush` consumed unchanged from the VM.
- `RulesView.xaml`: setup/unlock/admin alerts as banner-styled `Border`s; "Tiempo extra para hoy" as a `Card`; master/detail `GroupBox`es → `Card`s with `SectionHeader`; rule list as `AppListView`; 7 `DayChip`s; primary/secondary/destructive buttons (inline delete `Background` removed); error/status messages as banners that collapse when empty (DataTriggers, VM untouched).
- `HistoryView.xaml`: prominent week metric (34 px) with right-side "Actualizar"; `AppListView` tables with flat headers; numeric columns (Uso / Bonus / Duración) right-aligned tabular via `CellTemplate` + `NumericCell`; error as `DangerBanner`.
- `SettingsView.xaml`: setup as `CalloutCard`; unlock/admin as banners; three `GroupBox`es → `Card`s ("Contraseña de administrador" keeps `IsEnabled="{Binding CanEdit}"`, "Arranque", "Bloqueo de seguridad" read-only on `SurfaceSubtleBrush`); error/status banners.
- `AdminExitWindow.xaml`: styled input/buttons, error color from resources. Enter/Escape behaviour unchanged.

## Tests

No new tests (presentation-only; no UI tests exist). `dotnet test`: 257 passed. `dotnet build`: green, 0 warnings.

## Manual validation

Checklist in the original plan (Dashboard states + unavailable-Service banner, rules CRUD/chips/grant, history selection, settings, AdminExitWindow, keyboard walk-through). Not executed in this environment beyond build/test; requires running the App + Service on Windows.

## Decisions

- Phase identifier `redesign-phase-01`; does not renumber Phases 20/21/22.
- Banners are plain `Border` styles with explicit icon/text content — the simplest normal-WPF approach; no custom control.
- `AppGridViewColumnHeader` font size is 12 (accessibility minimum), not 11 as originally drafted.
- `DashboardViewModel.StateBrush`/`ActivityBrush` untouched in this phase.
- If a global implicit style misbehaves, make that style explicit rather than add per-view overrides.
- The implicit `TextBlock` style deliberately has no `Foreground` setter (a style setter on the generated content `TextBlock` would beat the inherited `Foreground` of buttons/chips and make white-on-accent text unreadable). Default text color flows inherited from the `TextElement.Foreground` setter on the implicit `Window` style.

## Known limitations

- `GridViewColumn.ElementStyle` is not settable in XAML in this toolchain (declared on internal `GridViewColumnBase`); numeric cells use `CellTemplate` + `NumericCell` style instead — identical visual result.
- WPF has no `FontFeatureSettings`; tabular figures use `Typography.NumeralAlignment=Tabular`.
- `Segoe Fluent Icons` is absent on Windows 10; the chosen glyphs (`E80F`, `E90F`, `E81C`, `E713`, `E7BA`, `E73E`, `EA39`) exist in `Segoe MDL2 Assets`.
- Replacing default theme styles removes the built-in grayed-disabled text fade; disabled state is conveyed by control opacity/subtle fills.
