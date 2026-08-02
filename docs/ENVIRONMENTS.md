# Environments — Production vs Development

> **Title:** Environments — Production vs Development  
> **Date:** 02.08.2026  
> **Updated:** 02.08.2026 (branch flow, ACC place `SI`, Gmail DEV direction, log level keep Warning)  
> **Status:** Active  
> **Scope:** Machine roles, configuration placement, allowed/forbidden operations per environment, and the target state for Google/ACC isolation. Documentation only — no code changes in this round.

Related: [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md), [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md), [`DEV_TOOLS.md`](./DEV_TOOLS.md), [`LOGGING.md`](./LOGGING.md), [`DEPLOYMENT.md`](../DEPLOYMENT.md), [`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md).

---

## 1. Purpose

Two physical machines run this repository. They are **not** interchangeable:

| Role | Machine | Primary job |
| --- | --- | --- |
| **PROD** | This workstation (release + ops) | Ship signed builds via `publish-all.ps1`, watch live logs and health during pilot, apply only small fixes / small feature updates |
| **DEV** | The second workstation | Day-to-day development, Debug builds, DevTools Reset & Seed, larger features and experiments |

Both sides must know which machine they are on, which database and external systems they touch, and what is allowed.

---

## 2. Environment matrix (current state)

| Concern | PROD (this machine) | DEV (second machine) |
| --- | --- | --- |
| Role | Release station + ops monitoring | Development |
| Git branch (target) | **`release`** — see [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) §3 | **`development`** (absorbs `release` after every ship) |
| Desktop host | Installed / tested as **`SiNet.App.Wpf`** (MSIX channel) | Usually Debug / F5 under Visual Studio or Cursor |
| SQL Server | **Production DB** (vault key `SiNet/ConnectionStrings/SiNetDatabase`) | **Development DB** (separate vault value on that machine) |
| ACC projects | Place = real city / site (**no** `SI` prefix) | Place = **`SI`** only (§5.1) |
| `dbo.SystemSettings` | Production rows (ACC BaseUrl, logging, folder IDs, …) | Development rows — already isolated because the DB is separate |
| Windows Credential Manager vault | Production secrets | Development secrets (must not copy production connection strings blindly) |
| AccService | Points at the production / office AccService (`AccService.BaseUrl` in SystemSettings) | Should point at a **dev AccService** or a carefully gated endpoint — see §5 |
| Central log share | `\\si-win-2k19\AutoCAD Data\log\` (default) | Same UNC today unless overridden in the DEV DB `Logging.CentralLogPath` |
| Publish channel | **Only PROD runs** `publish-all.ps1` to `\\SI-WIN-2K19\AppFolder\AppNet\` | Must **not** publish to the production share |
| DevTools Reset / Seed | **Forbidden** against production DB | Allowed against the development DB only |
| `#if DEBUG` role selector | Not present in Release/MSIX builds | Present in Debug builds |
| EF migrations | Operator-owned; never auto-applied by agents | Same rule; apply only against DEV DB unless explicitly approved for PROD |

```text
PROD machine                          DEV machine
─────────────                         ───────────
SiNet.App.Wpf (MSIX)                  Debug / F5
publish-all.ps1 ──► UNC AppNet        (no publish to prod share)
     │                                      │
     ▼                                      ▼
  PROD SQL                               DEV SQL
  (SystemSettings PROD)                  (SystemSettings DEV)
     │                                      │
     └──────── Google / ACC ────────────────┘
              (still shared — see §5)
```

---

## 3. Where each setting lives

| Setting | Storage | Separated by machine today? | Notes |
| --- | --- | --- | --- |
| SiNet SQL connection string | Windows Credential Manager — `SiNet/ConnectionStrings/SiNetDatabase` | **Yes** (per-machine vault) | Primary isolation mechanism |
| Replica / MasterPlan connection strings | Vault — `SiNet/ConnectionStrings/*` | **Yes** | Same vault model |
| AccService API key / cert password | Vault — `SiNet/AccService/*` | **Yes** (can differ per machine) | See [`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md) |
| Autodesk ClientId / ClientSecret | Vault — `SiNet/Autodesk/*` | **Can differ**, often same app today | Same Autodesk app ⇒ same ACC hub risk |
| Google OAuth `credentials.json` | Vault — `SiNet/Google/ClientSecrets` | **Can differ** | Same Google Cloud project ⇒ same Drive/Gmail scopes |
| `AccService.BaseUrl` | `dbo.SystemSettings` | **Yes** (follows SQL) | Key: `AccService.BaseUrl` |
| `AccService.PinnedCertificateThumbprints` | `dbo.SystemSettings` | **Yes** | |
| `InspectionTemplatesFolderId` / `InspectionReportsFolderId` | `dbo.SystemSettings` | **Yes** | |
| `Logging.CentralLogPath` and `Logging.*.*Level` | `dbo.SystemSettings` | **Yes** | Defaults in `CentralLoggingDefaults` |
| `GoogleReports.SharedDriveId` / `RootReportsFolderId` / template IDs | **`src/SiNet.App.Wpf/appsettings.json` (in git)** | **No** | Same values on both machines unless overridden locally |
| `GoogleDrive.SharedDriveId` / `ProjectsRootFolderId` | `appsettings.json` (often empty; may be filled by other layers) | **No** when committed | |
| `Gmail.RootLabel` (`פרויקטים_משרד`) | `appsettings.json` | **No** | DEV can label / file into the real mailbox tree |
| `Gmail.TokenStorePath` | `appsettings.json` + optional env `SINET_GOOGLE_TOKEN_STORE` | Per-user local path | Tokens are per Windows profile |
| Per-user `LoggingEnabled` / `LogDirectory` | `%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json` | Per user / machine | Does **not** silence the central sink |
| MasterPlan API key | Vault — `SiNet/MasterPlanApi/ApiKey` | Per machine | Ops rotation: [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md) |

**Rule of thumb:** anything in the **vault** or in **SystemSettings of a separate DB** is already environment-specific. Anything **committed in `appsettings.json`** is shared until a local-override mechanism exists for `SiNet.App.Wpf` (target state in §6).

---

## 4. Allowed / forbidden operations

### 4.1 PROD machine (this workstation)

| Action | Status |
| --- | --- |
| Run `publish-all.ps1` (or a single channel script) to the production UNC share | **Allowed** — this is the release station |
| Small fixes / small feature updates, then release | **Allowed** — see [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) §2 |
| Tail central / local logs; open «מצב מערכת» and «בריאות תהליכים» | **Allowed** — primary ops job |
| DevTools Reset / Seed / demo tasks against the connected DB | **Forbidden** |
| Point vault at the DEV DB “just to try something” | **Forbidden** without an explicit, temporary, documented switch |
| Apply EF migrations without operator confirmation | **Forbidden** (repo rule for all agents) |
| Commit secrets, connection strings, or real API keys | **Forbidden** |
| Git branch | Prefer **`release`**; after every ship merge into `development` |

### 4.2 DEV machine (second workstation)

| Action | Status |
| --- | --- |
| Debug builds, unit/integration tests, feature branches | **Allowed** |
| DevTools Reset / Seed against the **development** DB only | **Allowed** — see [`DEV_TOOLS.md`](./DEV_TOOLS.md) |
| Publish to `\\SI-WIN-2K19\AppFolder\AppNet\` | **Forbidden** |
| Write ACC / Drive / Gmail into **production** place-names / mailbox | **Forbidden** — DEV must use DEV conventions in §5 |
| Use production SQL connection string in the DEV vault | **Forbidden** |
| `#if DEBUG` authorization role selector | **Allowed** (Debug only); opt-out `SINET_SKIP_DEBUG_ROLE_SELECTOR=1` |
| Git branch | Prefer **`development`** (see [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) §3) |

### 4.3 Both machines

- Agents follow [`AGENTS.md`](../AGENTS.md) and `.cursor/rules/*`.
- Local build gate before claiming a code task done: build `SiNet.App.Wpf` + test `SiNet.App.Wpf.Tests`.
- Sibling repos must match [`build/sibling-pins.json`](../build/sibling-pins.json) — see [`BUILD_SIBLING_PINS.md`](./BUILD_SIBLING_PINS.md).

---

## 5. Shared external systems — isolation policy (decided 02.08.2026)

SQL is fully separated. There is **no separate ACC account / hub** and Google is still largely the same Cloud project. Isolation is therefore by **convention and dedicated DEV resources**, not by a second Autodesk tenancy.

### 5.1 ACC — place-name convention (decided)

Same ACC hub / Autodesk app. Projects are distinguished by the **place (מיקום)** field used when registering / creating the project in ACC:

| Environment | Place name convention | Example |
| --- | --- | --- |
| **DEV** | Place name is **`SI`** (registered for the app for development work) | Place = `SI` → development project |
| **PROD** | Place name is the **city / real site only** — **without** an `SI` prefix | Place = city name → production project |

**Rules:**

- On the DEV machine / development DB, create and exercise only projects whose place is `SI`.  
- Never use a real production city place-name for experiments, Reset/Seed side-effects, or bulk ACC writes from DEV.  
- Agents and operators: if an ACC write target’s place is not `SI`, treat it as production and stop unless the operator explicitly confirms a production ops action on the PROD machine.

This is a **process + naming** control, not yet an automatic code gate. A future code slice may refuse DEV AccService calls against non-`SI` places (**Needs Review** / follow-up).

### 5.2 Google / Gmail (direction decided; details open)

| Surface | Decision | Status |
| --- | --- | --- |
| Gmail | Prefer a **dedicated DEV mailbox** (separate Google account / mailbox) so filing and labels do not touch the office production mailbox | **Direction decided** — account/label tree not provisioned yet |
| Drive / reports | Still shared risk via committed `GoogleReports.*` IDs in `appsettings.json` | Use DEV mailbox + avoid writing reports from DEV until local overrides exist (§6.2); optional later: DEV folder under the Shared Drive |

Until the DEV mailbox exists: on DEV, treat Gmail filing and Drive writes as **production-impacting**. Prefer read-only verification. Do not run bulk ingest against the office mailbox.

### 5.3 Residual risk table

| Surface | Isolation today | Residual risk |
| --- | --- | --- |
| ACC hub | Same hub; DEV projects under place `SI` | Mistake using a production place-name from DEV |
| Gmail | Planned separate DEV mailbox | Until provisioned, same mailbox / `Gmail.RootLabel` |
| Google Drive templates / reports | Same Shared Drive IDs in git | DEV can still write if code paths run |

---

## 6. Target state (not implemented in this docs round)

### 6.1 Environment identity — `SINET_ENVIRONMENT`

| Item | Target |
| --- | --- |
| Env var | `SINET_ENVIRONMENT` = `Production` \| `Development` (exact names **Needs Review**) |
| Log enricher | Serilog property `Environment` on every line (alongside existing `Host` / `Machine` / `User`) |
| Purpose | Filter central-share noise; make crash reports self-describing |

Not implemented today. Logs only carry `App`, `Host`, `Machine`, `User`, `ProcessId`, `ThreadId`.

### 6.2 Local overrides for `SiNet.App.Wpf`

Legacy V2 already has `appsettings.local.template.json` (gitignored local file). Target for the production host:

1. Support `appsettings.local.json` (or equivalent) loaded after `appsettings.json`, **not committed**.
2. On DEV, override at least:
   - `GoogleReports.SharedDriveId` / `RootReportsFolderId` / template spreadsheet IDs  
   - `Gmail.RootLabel` (e.g. a DEV-only label tree)  
   - `GoogleDrive.*` folder IDs if used  
3. Keep production values only on PROD (committed defaults or PROD-only local file).

### 6.3 ACC / Google — aligned with §5 decisions

| Piece | Target follow-up |
| --- | --- |
| ACC place `SI` | Documented process rule (§5.1). Optional later: enforce in bootstrap / project-create when host is Development |
| DEV Gmail mailbox | Provision account; point DEV vault Google OAuth + token store + (when available) `Gmail.RootLabel` via local override |
| Drive | Optional DEV root folder; not required before first pilot if DEV avoids report-generation writes |

Full separate ACC tenancy remains **out of scope** (not available). Soft-ban-only without the `SI` place rule is **dropped**.

### 6.4 Separate central log root for DEV

**Not required for the early pilot** (owner decision 02.08.2026 — keep the default central log profile). May revisit if DEV noise pollutes PROD tails. Optional later: override `Logging.CentralLogPath` on the DEV DB only.

---

## 7. How an agent or human recognizes the machine

Practical checks (no `SINET_ENVIRONMENT` yet):

1. **Vault SQL target** — open Secret Setup / Credential Manager and see which server/database the connection string hits.  
2. **Installed package** — PROD pilot users run MSIX `SiNet.App.Wpf` from `\\SI-WIN-2K19\...\SiNet.App.Wpf\`. DEV usually runs from `bin\Debug`.  
3. **Ask the operator** — if unclear, do not assume; do not run DevTools or publish.

---

## 8. Out of Scope

- Implementing `SINET_ENVIRONMENT` or log enrichers  
- Creating a second Autodesk / ACC tenancy  
- Provisioning the DEV Gmail mailbox (ops action; document when done)  
- Adding `appsettings.local.json` support to `SiNet.App.Wpf`  
- Changing CI or publish scripts / creating git branches  
- Any DB schema / migration work  
- Changing production `Logging.Client.CentralLevel` for the early pilot  

## 9. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Single shared SQL for PROD and DEV | Dropped | Full DB separation already in place and required |
| Separate ACC hub / tenancy for DEV | Dropped / unavailable | Use place-name `SI` instead (§5.1) |
| Soft-ban only with no ACC naming rule | Dropped | Replaced by §5.1 |
| Treating `appsettings.json` Google IDs as environment-safe | Postponed | Needs DEV mailbox + local overrides |
| Hard enforcement that DEV cannot publish | Postponed | Process rule only until tooling/CI gate exists |
| Lowering central log level to Information for pilot | Postponed | Owner: keep default Warning for early stages — see [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) |
| Seq / HTTP telemetry sink | Postponed | UNC Serilog share remains the ops channel |

## 10. Needs Review

1. **Exact `SINET_ENVIRONMENT` value set** when implemented (`Production`/`Development` vs `prod`/`dev`).  
2. **Which Google account** becomes the DEV mailbox, and whether Drive gets a DEV-only folder.  
3. **Whether to add a code gate** that blocks ACC project create/write from DEV when place ≠ `SI`.

## 11. Cross-references

- Release workflow: [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md)  
- Live monitoring: [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md)  
- DevTools constraints: [`DEV_TOOLS.md`](./DEV_TOOLS.md)  
- Rollout phases: [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md)  
- Domain deployment principles: [`SiNetProjectManagerV2/Docs/Domains/Deployment/DeploymentPrinciples-2026-05-26.md`](../SiNetProjectManagerV2/Docs/Domains/Deployment/DeploymentPrinciples-2026-05-26.md)
