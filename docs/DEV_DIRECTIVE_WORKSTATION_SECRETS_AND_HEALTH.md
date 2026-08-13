# DEV-027 — הנחיית פיתוח: סודות תחנה + בריאות שמבחינה בין התקלות

> **Title:** Workstation secrets import for non-admin users; System Status must classify AccService/Gmail/vault faults  
> **Date:** 13.08.2026  
> **Updated:** 13.08.2026 (operator lock: two import modes; Deep health = 30 min or Refresh)  
> **Status:** Implemented on `development` — operator verify pending; do not ship to `release` until verify  
> **Scope:** Product/engineering directive for the **`development`** branch after PROD log review (Lilach `SI2025-1` AccService 401; Sarita Gmail cancel / R02; System Status TLS guidance false-positive). Implement on `development` after this lock; do not ship to `release` until the slices land.  
> **Branch:** Write/merge on `development`; ship via normal `release` process later.

Related: [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md), [`APP_SHELL.md`](./APP_SHELL.md), [`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md), [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md), [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md), [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md), [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md).

---

## 1. Why (evidence 13.08.2026)

Central log `\\si-win-2k19\AutoCAD Data\log`:

| Who | Signal | What the log actually is | What the user sees |
| --- | --- | --- | --- |
| **Lilach** `SI2025-1` 17.06.2026 | `[AccService] EnsureInboxAsync FAILED — category=ApiKeyRejected, http=401` body `Missing or invalid X-AccService-Key header` | TLS **succeeded**; vault on that Windows user has no / wrong AccService API key | «מצב מערכת» guidance says **TLS / SSL** |
| **Sarita** 13.08 11:16 | `[Gmail] Messages.List failed: The operation was canceled` | System Status 10s timeout cancelled the Gmail probe; gateway logs it as Error | Empty mailbox / noise in Llog |
| **Sarita** 12.08 17:47 | `[R02] Cannot open database Db_Mp_SiEng` for `SI-ENG\Sarita` | Not a member of `SI-ENG\שרטטים` | R02 fail dialog |
| Health probe `/v1/acc/health` | Auth-**exempt** | AccService row can be **Idle** while inbox ensure is 401 | False green |
| Health SSL failure | Swallowed into status `Detail`, **no** `Log.Error` | No `Client-yyyyMMdd.log` line | SSL text in the panel only |

Operator constraint: a normal user **cannot open «מפתחות וסודות»** (`System.Settings.Write` / Administrator). There is no supported way for Lilach to put `SiNet/AccService/ApiKey` into her per-user Credential Manager.

---

## 2. Existing mechanisms (reuse — do not invent parallel stacks)

| Mechanism | Where | Reuse |
| --- | --- | --- |
| Encrypted `.secrets` package | `SecretProvisioningFileService` — AES-256-CBC + PBKDF2, magic `SNET`, compatible with `SiNet.SecretImport` | **The** file format. Admin already exports this from Secret Setup |
| Import/export ports | `ISecretSetupService.ExportAsync` / `PreviewImportAsync` / `ImportAsync` | Call the same ports. Preview = **key names only**, never values. Extend `ImportAsync` with a **mode** (today only `overwrite: bool` — not enough for replace/delete) |
| Import merge (today) | `CredentialVaultSecretSetupService.ImportAsync` | Unknown keys skipped; empty skipped; keys not in the file are **never** deleted. Target adds an explicit **replace** mode (see §3.2) |
| Admin Secret Setup UI | `SecretSetupWindow` + `SecretSetupViewModel` gated by `System.Settings.Write` | **Keep** for generate / edit / export / test. Do **not** open this window for employees |
| AccService health | `IAccServiceHealthProbe` → `GET /v1/acc/health` (no API key) | Keep as the **fast** reachability/TLS row |
| AccService authenticated diag | `IAccServiceDiagnosticsProbe` → `GET /v1/acc/diag` **with** `X-AccService-Key`; already maps HTTP 401 | Use as the **deep** auth-key row — already implemented, **not wired into System Status** |
| Local key presence | `IAccServiceKeyDiagnostics.Describe()` (`HasApiKey`, length, hash prefix — no raw secret) | Cheap local Fast row / part of vault row |
| System Status | `IRuntimeSubsystemStatusService` + `ISubsystemStatusContributor`; startup +3s then every 5 min; per-contributor 10s timeout | Extend with probe **tier**; do not add a second health bus |
| Guidance | `SystemStatusGuidanceCatalog` | Split TLS vs API-key vs import; today **any** Degraded `acc-service` gets TLS text — that is the SSL lie |
| Google token store | `FileDataStore` under `%LOCALAPPDATA%\SiNet\google-token` | Mutex / serialize writes — contributors currently `Task.WhenAll` and collide |

Vault is **per Windows user** (DPAPI). Export on the admin PC does not populate Lilach’s vault. That is by design; the missing piece is an **employee-reachable import**.

---

## 3. Target state (locked)

### 3.1 Slice A — classify and log (no new screens)

1. **`SystemStatusGuidanceCatalog` for `acc-service`:** TLS guidance **only** when the summary looks like TLS (`SSL`, `TLS`, `certificate`, `thumbprint`, `AuthenticationException`, `trust relationship`). HTTP 401 / `ApiKeyRejected` / missing vault key → **API-key / import** guidance (Hebrew: המפתח חסר או נדחה; ייבא חבילת `.secrets` או פנה למנהל). Do **not** use `state is Degraded` as a reason to show TLS.
2. **`GmailEmailGateway`:** `OperationCanceledException` is **not** `Log.Error`. Rethrow (or return cancelled) so the 10s contributor timeout can mark the Gmail row as timeout instead of “Gmail נגיש — אין הודעות”.
3. **Health / diag failures:** when AccService probe is Offline / 401 / TLS, write **Warning** to Serilog (machine, user, category, status/exception type — no secrets) so Llog gets a line.
4. **Google token file lock:** serialize `FileDataStore` writes (named mutex or one shared store instance) so parallel System Status Google contributors cannot throw `IOException` “file is being used by another process”.

### 3.2 Slice B — employee import of `.secrets` (the operator’s “request import keys”)

**Do not** grant employees `System.Settings.Write`. **Do not** open Secret Setup.

| Item | Spec |
| --- | --- |
| Feature code | New `Shell.ImportWorkstationSecrets` — minimum role **Employee** (any signed-in user) |
| Menu | מנהלה (or next to מצב מערכת): **«ייבוא מפתחות תחנה»** — visible without Administrator |
| Flow | OpenFileDialog `*.secrets` → existing password dialog → `PreviewImportAsync` (names only) → **choose mode** → confirm → import |
| After import | Refresh System Status (Fast + Deep). Hebrew summary: כמה עודכנו / נוספו / נמחקו / דולגו |
| Export | Stays **Administrator-only** inside Secret Setup. Operator produces one `SiNet.secrets` + password and gives it to the workstation (USB / approved share). Password is the access control |
| Guidance link | When vault missing AccService key or diag 401, System Status `GuidanceHe` tells the user to run **«ייבוא מפתחות תחנה»** — not to open Secret Setup |
| Admin Secret Setup import | Same two modes (one behavior). Keep generate / edit / export on that window only |

#### B.1 Two import modes (operator lock 13.08.2026)

After preview, the user **must** pick one. There is no silent default that deletes.

| Mode | Hebrew (UI) | What happens to `SecretCatalog` keys |
| --- | --- | --- |
| **UpsertFromFile** | «עדכן את כל מה שמופיע בקובץ» | Every catalog key **in the file** is written (create or replace). Catalog keys **not** in the file stay as they are. No deletes |
| **ReplaceCatalogWithFile** | «החלף — השאר רק מה שקיים בקובץ» | Same upsert for keys in the file, **then delete** catalog keys that exist in this Windows user’s vault but are **absent from the file**. The workstation catalog then matches the package |

Shared rules for both modes:

- Preview lists **key names only** (in file / already on PC / would be deleted in Replace). Never values.
- Unknown keys (not in `SecretCatalog`) are never written and never used as a delete target.
- Empty values in the file are skipped (not written; do not count as “present” for Replace-delete).
- Replace requires a **second** strong confirm that names the keys that will be deleted.
- Never touch vault entries outside `SecretCatalog.AllKeys`.
- Never apply a second file format.

Port shape (extend, do not fork): replace `bool overwrite` with `SecretImportMode` (`UpsertFromFile` | `ReplaceCatalogWithFile`). Admin import uses the same enum. `overwrite: false` (skip existing) is **not** one of the two product options — drop it from the employee flow; admin may keep skip-existing only if tests still need it, otherwise migrate tests to the two modes.

Optional UX (same slice if cheap): a button on the System Status row that runs the same import command. Not a second importer.

### 3.3 Slice C — System Status coverage (fast vs deep)

Principle: **the panel must distinguish the failure classes we already hit in production**, without stalling the UI.

| Tier | When | Timeout | What |
| --- | --- | --- | --- |
| **Fast** | Current loop: ~3s after shell, then every **5 minutes**; also on window Refresh | **10s** (existing) | Cheap / already-running probes. Must stay parallel and coalesced |
| **Deep** | First refresh after shell (~3s), then every **30 minutes**, and **whenever** the user clicks Refresh in «מצב מערכת» | **20s** bounded | Authenticated / SQL-permission probes that would be wasteful every 5 min. **Locked** — not startup-only |

Contributors declare a tier (`Fast` default). Aggregator runs Fast every cycle; Deep only on Deep cadence or manual Refresh. A Deep timeout yields Degraded for **that** row only.

#### C.1 Failure-class catalogue (must appear in «מצב מערכת»)

| Class | Row key (reuse if possible) | Tier | Signal | Guidance |
| --- | --- | --- | --- | --- |
| AccService TLS / pin | `acc-service` | Fast | Health probe exception / chain / pin | Existing TLS pin text |
| AccService up but **key missing locally** | `acc-service-key` **or** enrich `acc-service` | Fast | `IAccServiceKeyDiagnostics.HasApiKey == false` | Import `.secrets` |
| AccService up, key present, **401** | same | **Deep** | `IAccServiceDiagnosticsProbe` HTTP 401 | Key mismatch — re-import; do not say SSL |
| AccService Autodesk token on **server** | keep for DEV-002 | Deep | `/diag` `refreshTokenFileExists` if already on the payload | [`OPS_ACCSERVICE_TOKEN_REFRESH.md`](./OPS_ACCSERVICE_TOKEN_REFRESH.md) |
| Gmail timeout / cancel | `google` | Fast | Timeout row, not Error log | Retry / network; not “אין הודעות” |
| Gmail unauthenticated | `gmail` / `google` | Fast | existing | existing Gmail guidance |
| Replica SQL denied | `masterplan-replica` (new) | **Deep** (or Fast if `OpenAsync` is cheap) | `SqlConnection` to vault `ReplicaDatabase` only — **not** live `Db_Mp_SiEng` | User not in `SI-ENG\שרטטים` / login mapping; point to IT, not Secret Setup |
| Google client secrets missing | `google-config` | Fast | existing path/vault | Import `.secrets` if the package includes Google JSON |

**Do not** probe live `Db_Mp_SiEng` on a timer. Product reports are Replica-first (DEV-025). Replica connect is enough to catch Lilach/Sarita-style “login failed for this Windows user”.

**Do not** scrape UNC Llog from the client as a health signal.

### 3.4 Footer

If any of the new/classified rows are Degraded, the existing footer indicator stays red/amber. No extra popup in this ID (popups remain [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) / DEV-002, admin-only).

---

## 4. Implementation notes (for the DEV agent)

1. Docs-first: this file is the SoT. After approval, implement on **`development` only**.
2. Prefer extending `ISecretSetupService` + a small dedicated window/dialog. No second encryption format. No plaintext JSON keys file.
3. Register `Shell.ImportWorkstationSecrets` in `AppFeatureCodes` **and** `AppFeatureAuthorization` (Employee). Deny-by-default.
4. System Status: extend `ISubsystemStatusContributor` (or aggregator) with tier; do not `Task.WhenAll` Deep probes on every 5-minute tick.
5. Tests (behavior names):  
   - guidance 401 ≠ TLS  
   - Gmail cancel is not Error  
   - UpsertFromFile updates keys in the file and leaves others  
   - ReplaceCatalogWithFile updates keys in the file and deletes catalog keys absent from the file  
   - Replace does not delete unknown / non-catalog vault keys  
   - employee menu visible without `System.Settings.Write`  
   - Fast cycle does not call `/v1/acc/diag`  
   - Deep runs on the 30-minute cadence **or** manual Refresh and maps 401  
6. No EF / schema. No migrations.
7. Logging: Warning on classified AccService failures; never log API key values or `.secrets` passwords.

---

## 5. Ops notes (not code — operator on PROD)

- **Lilach / any new workstation:** admin exports `.secrets` from Secret Setup → user runs «ייבוא מפתחות תחנה» (after this ships). Until then, an admin must import on that Windows session (RDP as Lilach, or grant temporary Secret Setup).
- **Sarita R02:** add `SI-ENG\Sarita` to AD group `SI-ENG\שרטטים` if she should run MasterPlan reports. DEV-022 only re-applies that **group** after restore; it does not add missing people.
- AccService pin in SQL already matches cert `B4B25512CE85202CF0591A2023B6A307121AB082` (verified 13.08.2026). Do not rotate the pin unless the cert changes.

---

## 6. Risk and complexity

| Area | Assessment |
| --- | --- |
| **Complexity** | Moderate. Slice A is catalog + logging + mutex. Slice B is a thin UI over existing import. Slice C needs a Fast/Deep split in the aggregator — the only non-trivial part |
| **Effort** | A: small. B: small–medium (dialog + feature code + tests). C: medium (tier + 1–2 contributors + guidance) |
| **Blast radius** | Secret Setup admin path must keep working. Upsert replaces keys that are in the file. Replace **deletes** catalog keys missing from the file — only after named confirm. Employee import cannot generate keys or edit other users |
| **Security** | `.secrets` + password = full service-secret set for that Windows user. Same as today’s admin import. Do not auto-load from UNC without a click + password |
| **Perf** | Fast loop stays 10s / 5 min. Deep 30 min + manual. No UI-thread I/O |
| **Data/schema** | None |
| **Compatibility** | Existing admin Export/Import unchanged. V2 `SecretProvisioningService` file format unchanged |

---

## 7. Out of Scope

- Granting employees the full Secret Setup window or `System.Settings.Write`
- Auto-import at startup from a network path
- Changing AccService `/health` to require the API key (would break monitors)
- Adding Sarita to AD from the app
- SyncEngine MasterPlan API 429 / extra `--daily` the same day (ops; not this ID)
- DEV-002 admin startup popup (already planned)
- In-app Llog viewer
- Live `Db_Mp_SiEng` health probe
- New encryption format or per-key “request from server” protocol

---

## 8. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Opening Secret Setup for all users | **Dropped** | Operator cannot give secrets-admin to drafters; import is the approved gap-filler |
| UNC silent provisioning (`Install-OnServer` on every PC) | **Postponed** | Server script is for `sieng`; workstation needs a click + password |
| Lowering central log level to Information | **Postponed** | Pilot decision 02.08.2026; Warning on classified failures is enough |
| Google token lock as a System Status row | **Dropped** | Fix the race (Slice A); do not display file-lock as a subsystem |
| Scraping central logs into the health window | **Dropped** | Structured probes only |
| Employee import “skip keys that already exist” (`overwrite: false`) | **Dropped** as a product choice | Operator lock: only UpsertFromFile or ReplaceCatalogWithFile |

---

## 9. Needs Review

1. ~~Import overwrite vs leave others~~ **Locked** — two modes, §3.2 B.1.  
2. ~~Deep 30 min vs startup-only~~ **Locked** — 30 minutes or Refresh, §3.3.  
3. Confirm new row `acc-service-key` vs a single `acc-service` summary that concatenates health + key (prefer **one row** if the Hebrew summary stays readable).  
4. Share location for `SiNet.secrets` given to users (not committed; password out of band).

---

## 10. Recommended implementation order

1. Slice A (guidance + Gmail cancel + Warning logs) — unblocks honest diagnosis in Llog.  
2. Slice B (employee import) — unblocks Lilach-class workstations.  
3. Slice C (Fast/Deep + 401/replica rows) — unblocks «מצב מערכת» from lying green/SSL.
