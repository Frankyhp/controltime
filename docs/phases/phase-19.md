# Phase 19 — Usage History UI

## Goal

Expose stored usage information in the App: today, previous days, weekly overview, bonus granted, and basic session details for a selected day. Statistics kept intentionally simple.

```text
UsageHistoryService (Core)
  ├─ IUsageSessionStore.GetSessionsOverlappingAsync(range)   (one query)
  ├─ ITimeGrantStore.GetGrantsInRangeAsync(range)            (one query)
  └─ per-day usage via UsageCalculator (asOf = now) → UsageHistory
            ▼
Historial tab (App): 14 days + week total + sessions of the selected day
```

## Scope

In scope:

- Core:
  - `Domain/UsageHistory`: `UsageHistoryEntry(DateOnly Date, TimeSpan Usage, TimeSpan BonusGranted)` and `UsageHistory(Days most-recent-first, CurrentWeekUsage, Sessions, AsOf, Today)`.
  - `Services/UsageHistoryService`: single range load of sessions and grants, per-day usage via `UsageCalculator.GetUsageForDay` with `asOf: now` (open sessions count up to now; midnight-crossing sessions split per day), per-day bonus from grants attributed to the local day, `CurrentWeekUsage` summed from Monday.
  - `ITimeGrantStore.GetGrantsInRangeAsync(from, to)` (implemented in `SqliteTimeGrantStore`).
- App:
  - Fourth tab **Historial** (`Views/HistoryView`, `ViewModels/UsageHistoryViewModel`): week-total card, 14-day list (Día / Uso / Bonus with "Hoy"/"Ayer" labels), manual "Actualizar", sessions of the selected day (period `HH:mm – HH:mm` or "en curso", duration).
  - App registers `IUsageSessionStore` **read-only** (the Service remains the sole usage-session writer) and `UsageHistoryService`/`UsageHistoryViewModel`.

Out of scope:

- Charts/advanced analytics, per-rule history, historical recalculation, Service-side history queries (App reads SQLite directly, same pattern as rules/grants).

## Decisions

- Days are local-calendar based (`DateOnly` + `utcOffset`), consistent with `UsageService`/`DashboardService`; the ViewModel passes `DateTimeOffset.Now.Offset`.
- Week starts on Monday (es-ES culture convention) and the week total only sums days inside the loaded range.
- Session details show raw overlapping sessions for the selected day (a midnight-crossing session appears on both days with its full duration); day usage itself is correctly split.
- The App reads sessions directly from SQLite rather than via IPC: read-only access adds no consistency risk worth a new message (Service stays the only writer).

## Tests

224 tests green (`dotnet test`), solution build clean (`dotnet build`).

- `UsageHistoryServiceTests` (new): midnight split across two days; open session counted up to now; per-day bonus aggregation; grants outside range ignored; days ordered most-recent-first including empty days; `CurrentWeekUsage` sums from Monday (Sunday-night overflow excluded); sessions outside range excluded.

## Manual validation (pending)

- Run the App with data accumulated by the Service: Historial tab shows coherent today/yesterday/week numbers matching the Dashboard (usage today) and Rules bonus panel (bonus today).
- Leave a session open → "en curso" row for today, usage grows after "Actualizar".
- Midnight-crossing session appears split across days in the totals and as one session in the details of both days.
- Bonus granted today appears as `+bonus` on Hoy and only inside the current week total.
- Database empty → 14 rows with 0:00, no errors.

## Known limitations

- History is local-time-zone dependent: usage is attributed to days by the current UTC offset, so pre-DST-change data shifts (same trade-off as daily limits).
- No pagination: the tab always loads a fixed 14-day window (≤90 supported by the service).
- Session detail durations are not clipped to the selected day (see Decisions).
