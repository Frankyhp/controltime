# ControlTime — Product Rules

## Purpose

This document contains stable product behaviour and business rules.

Implementation details and architecture decisions belong elsewhere.

---

# Access modes

ControlTime has two logical access modes:

```text
Normal mode
Admin mode
```

## Normal mode

Normal mode allows access to non-sensitive information such as:

* current usage
* remaining usage time
* current access state
* usage history

Normal users must not be able to modify usage restrictions.

---

## Admin mode

Administrative functionality requires authentication using the configured administrator password.

Administrative functionality includes:

* creating rules
* editing rules
* deleting rules
* enabling or disabling rules
* changing allowed days
* changing allowed schedules
* changing daily usage limits
* changing application settings that affect enforcement

The administrator password must never be stored in plain text.

Only a secure password hash may be persisted.

The initial administrator password must be configured during the appropriate setup flow.

---

# Bonus time / Time grants

An administrator may grant additional usage time for the current day.

This action requires administrator password authentication.

The administrator specifies the number of minutes to grant.

Example:

```text
Daily limit:      120 minutes
Granted time:      30 minutes
Effective limit:  150 minutes
```

A time grant must not modify the configured daily limit.

Instead, it must be stored separately as a time grant.

Conceptually:

```text
TimeGrant
- Id
- GrantedAt
- Minutes
```

Multiple grants may exist on the same day.

Example:

```text
Daily limit           120
Grant                  30
Grant                  15
─────────────────────────
Effective allowance   165 minutes
```

Effective daily allowance is calculated as:

```text
Configured daily limit
+ grants for the current day
```

Remaining usage is calculated as:

```text
Configured daily limit
+ grants for the current day
- usage for the current day
```

Time grants apply only to the day on which they are created.

They do not modify future daily limits.

---

# Time grant restrictions

A time grant increases the daily usage allowance only.

It does not override:

* allowed weekdays
* allowed usage schedules
* other blocking conditions

Example:

```text
Allowed schedule:
17:00 - 21:00

Daily limit:
120 minutes

Granted:
30 minutes
```

The user has 150 minutes available but may still only use them between 17:00 and 21:00.

Temporary access outside an allowed schedule, if ever required, must be implemented as a separate feature.

---

# Rule evaluation

The effective daily limit is:

```text
dailyLimit + timeGrantedToday
```

Conceptually:

```text
RuleEngine
   │
   ├── current time
   ├── current day
   ├── configured rules
   ├── usage today
   └── granted time today
          │
          ▼
      RuleResult
```

Core business logic must calculate access decisions.

The UI must not independently calculate whether access is allowed.

---

# Administrative security

ControlTime is designed to prevent normal or casual users from changing restrictions.

It is not assumed to be impossible for a Windows local administrator to bypass the application.

Administrative protection must therefore be described as configuration protection, not absolute system security.
