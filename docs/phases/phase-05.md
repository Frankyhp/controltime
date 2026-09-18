# Phase 05 — Administrator Authentication

## Goal

Introduce the administrative access model: secure password hashing, verification, and admin-mode state (normal vs admin).

## Scope

Everything lives in `ControlTime.Core` (pure C#, no new packages, no UI, no new persistence tables).

Implemented:

* administrator password setup (first-run only in this phase)
* administrator password verification
* secure password hashing (PBKDF2-HMAC-SHA256)
* admin session/state with automatic expiration
* entering admin mode
* leaving admin mode

Out of scope: password change flow, rate limiting/lockout, re-authentication prompts in UI, DI container wiring, bonus time, admin UI screens.

## Implementation

Core (`Abstractions/`):

* `IPasswordHasher` — `string Hash(string password)`, `bool Verify(string password, string passwordHash)`

Core (`Services/`):

* `Pbkdf2PasswordHasher` — implements `IPasswordHasher` with `Rfc2898DeriveBytes.Pbkdf2` (SHA-256, 100 000 iterations, 16-byte random salt, 32-byte key). Self-describing format: `PBKDF2.SHA256.{iterations}.{saltBase64}.{keyBase64}`. Verification uses `CryptographicOperations.FixedTimeEquals` and safely returns `false` for malformed stored hashes. No new NuGet packages.
* `AdminAuthService` / `IAdminAuthService` — normal/admin mode model:
  * setup/verification through `IAppSettingsStore` under key `admin.password.hash` (only the PBKDF2 string is persisted, never plaintext)
  * `SetPasswordAsync` only allowed when no password exists yet (`AlreadyConfigured` otherwise); minimum length 8, whitespace-only rejected
  * `TryEnterAdminModeAsync` returns `Success` / `InvalidPassword` / `NotConfigured`
  * admin mode is time-limited (default 5 minutes, injectable) and expires automatically; `ExitAdminMode()` leaves immediately
  * time comes from injected `TimeProvider` (defaults to `TimeProvider.System`) — no `DateTime.Now` in domain logic
  * successful setup implies entering admin mode (the acting user just proved intent via the setup itself)

No database schema change: the hash reuses the existing key/value `AppSettings` table from Phase 04.

## Tests

19 new tests (73 total):

`PasswordHasherTests`:

* hash non-empty and does not contain the password
* same password hashes differently each call (random salt)
* verify correct/wrong password
* empty/whitespace password rejected
* malformed stored hash verifies as `false` instead of throwing

`AdminAuthServiceTests` (in-memory settings store + fake `TimeProvider`):

* `HasPasswordAsync` false before setup, true after
* setup stores only the hash (plaintext never persisted)
* second setup returns `AlreadyConfigured` and original password still valid
* weak passwords (`TooShort`, whitespace) rejected and nothing stored
* enter admin mode with correct/incorrect password; wrong password keeps normal mode
* authentication before setup returns `NotConfigured`
* admin mode active until just before expiry, expires exactly at the deadline
* `ExitAdminMode` returns to normal mode
* fresh service instance verifies against the persisted hash

`PersistenceIntegrationTests` (real SQLite):

* password set through `AdminAuthService` + `SqliteAppSettingsStore`, verified from a fresh context; wrong password rejected; stored value contains no plaintext

## Manual validation

Not applicable — no UI in this phase.

## Decisions

* PBKDF2-HMAC-SHA256 via built-in `Rfc2898DeriveBytes`: zero dependencies, sufficient for a local desktop utility. Chosen over the ASP.NET `PasswordHasher` type, which is not available in plain `net10.0` without extra packages.
* Hash reuses the Phase 04 key/value settings table; no new table or migration.
* Hashing and admin state live in Core (deterministic and testable); persistence stays behind `IAppSettingsStore`.
* Admin session expiration implemented now (injectable `TimeProvider`) so Phase 16 only has to tune policy, not add the mechanism.

## Known limitations

* Setup is first-run only; changing the password is Phase 16.
* No rate limiting or lockout on failed attempts (Phase 16).
* `AdminAuthService` is not yet registered in a DI container or used by any UI (Phases 06+).
* Admin mode is in-memory per process; restart always returns to normal mode.
