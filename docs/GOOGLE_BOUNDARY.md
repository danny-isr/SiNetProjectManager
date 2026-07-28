# Google Boundary

> **Status:** Active — ProjectWork Drive + MasterPlan Reports Sheets (S3, 2026-07-28)  
> **Branch:** `SiWorkNet10`

This document records the **current code truth** for Google/Gmail/Drive/Sheets across the clean stack
and the legacy host. It exists to prevent doc/code drift while the refactor is split between:

- the native **module + harness** path (`SiNet.App.Wpf` + `SiNet.Infrastructure.Google`), and
- the legacy **production host** (`SiNetProjectManagerV2` + `SiOffice.GoogleConnector`).

When this document and the code disagree, fix the document first, then make an explicit follow-up
decision for any behavior change.

## 1. Executive Boundary

- **Native today:** Gmail **read**, Gmail auth-state/health, interactive/silent sign-in, native Gmail
  **send/modify**, **ProjectWork Google Drive** (read + write via `IFileStore`), and **MasterPlan
  Reports Google Sheets** (R01/R02/R03) exist in `SiNet.Infrastructure.Google` + Application ports.
- **Shared auth (locked):** one user OAuth session (`GmailClientProvider`) owns `UserCredential` for
  Gmail, Drive, and Spreadsheets; automatic token refresh; no per-window / per-operation login. UI
  uses `IConnectorAuthService` only (reports may call Sheets helpers after that session is ready).
- **Drive base folder (locked):** `GoogleDrive:SharedDriveId` + `GoogleDrive:ProjectsRootFolderId`
  (central projects root). All ProjectWork Drive paths are resolved under that root.
- **Production today:** `SiNetProjectManagerV2` still uses the legacy Google path for Sheets/Reports
  and for the legacy ProjectWork window; the New System graph registers the native module additively.
- **Still legacy today:** V2 R01–R03 dialogs (GoogleConnector Reports), Inspection Drive consumers,
  legacy outbound send, and legacy Gmail throttle/full-body under `SiNetProjectManagerV2`.
- **Not used at all:** Google Docs, Google Calendar, Google Tasks.
- **Reports Sheets (approved S3):** native R01–R03 use **User OAuth** + `Spreadsheets` on the shared
  credential (not service account). V2 Reports path remains until soak/cutover.
- **Open decision:** `GmailSend` product/policy approval for broad window adoption remains explicit.

## 2. Active Ownership Map

| Capability | Active implementation | Stack | Current status |
| --- | --- | --- | --- |
| Gmail inbox read (project label scoped) | `InboxViewModel` -> `IEmailGateway` -> `GmailEmailGateway` -> `GmailClientProvider` | Native | Active |
| First real email window (read-only content/details) | `EmailWindowViewModel` -> `IEmailGateway` + `IConnectorAuthService` | Native | Active, read-only body + attachment metadata |
| Gmail auth/health bridge | `IConnectorAuthService` -> `GmailConnectorAuthService` | Native | Active |
| Gmail send capability | `IEmailSender` -> `GmailEmailSender` | Native module | Implemented in code; host adoption is still separate |
| Gmail outbound send used by legacy flows | `GmailOutboundMailService` / `GoogleService` | Legacy host | Active |
| Gmail modify / labels / mark-read | `GmailEmailModifyService` / `IEmailGmailModifyService` | Native module | Implemented for list filing + triage; **requires OAuth re-consent** (`GmailModify` scope) |
| Gmail full-body / attachments / throttle | `GoogleService` / `GmailThrottleService` | Legacy host | Active, not ported |
| Google Sheets reports (`R01/R02/R03`) | Native Application ports + `SiNet.Infrastructure.Google` Sheets/Drive helpers; V2 still has GoogleConnector dialogs | Native + Legacy dual | Native S3; Legacy until cutover |
| Google Sheets migration readers | `SiNetProjectManagerV2/Services/Migration/*` | Legacy host | Active, not ported |
| Google Drive ProjectWork read/write | `GoogleDriveFileStore` -> `IGoogleDriveFileService` -> `GmailClientProvider` (shared credential) | Native | Active (User OAuth + Shared Drive root) |
| Google Drive project-file path (legacy window) | `GoogleDriveServiceProvider` / `GoogleDriveStore` | Legacy host | Active until ProjectWork surface cutover |
| Google Drive screenshot upload / PDF export | `GoogleNoteScreenshotUploadService`, `InspectionReportEmailBuilder` | Legacy host | Active, not ported |

## 2.1 Hosts and wiring

| Host | Google runtime path | `AddSiNetGoogle()` | `AddSiNetSecrets()` | Result |
| --- | --- | --- | --- | --- |
| `SiNetProjectManagerV2` production host | Legacy `GoogleService` / `GoogleAuthService` / `GmailOutboundMailService` for active legacy flows; native Gmail module is registered only for future New System consumers | Yes, via `AddSiNetNewSystemGraph()` | Yes, via `AddSiNetNewSystemGraph()` | Vault and native Gmail auth/session services are available to the New System graph, but production Google behavior remains legacy until a window/runtime slice explicitly adopts them |
| `SiNet.App.Wpf` standalone harness | Native `GmailClientProvider` / `GmailEmailGateway` / `GmailEmailSender` | Yes | Yes | Native Gmail is wired with vault-first client-secrets resolution when the secrets provider can resolve them; config fallback remains available only when the provider cannot supply a path |

Implication: the clean Gmail module is real and testable, but it is **not** the production-host
implementation today. The standalone harness is now secrets-aware, yet production Gmail/Drive/Sheets
behavior still belongs to the legacy host.

## 3. Native Gmail Module Boundary

### 3.1 Artifacts

| File / type | Responsibility |
| --- | --- |
| `src/SiNet.Infrastructure.Google/GmailClientProvider.cs` | OAuth session, silent restore, interactive sign-in, cached Gmail client |
| `src/SiNet.Infrastructure.Google/GmailEmailGateway.cs` | Native Gmail read path (`IEmailGateway`), including project-label lookup across Gmail location buckets and best-effort body/attachment metadata extraction |
| `src/SiNet.Infrastructure.Google/GmailEmailSender.cs` | Native Gmail send path (`IEmailSender`) |
| `src/SiNet.Infrastructure.Google/GmailEmailModifyService.cs` | Native Gmail label modify (project labels + triage status) |
| `src/SiNet.Infrastructure.Google/GmailConnectorAuthService.cs` | Auth-state / health bridge (`IConnectorAuthService`) |
| `src/SiNet.Infrastructure.Google/GoogleServiceCollectionExtensions.cs` | Registers read + auth-state + send + modify over the shared provider |
| `src/SiNet.Infrastructure.Google/GmailOptions.cs` | Configurable options: client secrets path, token store path, app name, root label, interactive sign-in |

### 3.2 Current scope truth

The live code in `GmailClientProvider` currently uses these hard-coded scopes:

- `GmailService.Scope.GmailReadonly`
- `GmailService.Scope.GmailSend`
- `GmailService.Scope.GmailModify`
- `DriveService.Scope.Drive` (full Drive — ProjectWork + Reports folders)
- `SheetsService.Scope.Spreadsheets` (MasterPlan R01/R02/R03)

That means the native module is **not** "read-only" anymore at the OAuth level. The **read gateway**
remains read-only in behavior, but the module also contains native send, **label-modify**,
**Drive**, and **Sheets** capabilities. Existing users must perform a **one-time interactive
re-consent** when a previously stored token lacks Spreadsheets (or Drive/send/modify). Silent
restore still works for previously granted scopes; insufficient-scope Sheets/Drive calls surface
as `GoogleConsentRequiredException`.

`GmailOptions` does **not** currently expose scopes; only path/app/root/interactive settings are
configurable. Scope selection remains a code-level decision inside `GmailClientProvider`.

### 3.3 Secrets and token storage

| Aspect | Native module / harness | Legacy host |
| --- | --- | --- |
| Client secrets source | **Vault-first only when `IGoogleClientSecretsPathProvider` is registered**; otherwise config fallback | Legacy `AppConfiguration` / reports config |
| Config fallback | `src/SiNet.App.Wpf/appsettings.json` `Gmail:ClientSecretsPath` (used only when vault-backed resolution cannot supply a path) | Legacy config paths |
| Token store | `Gmail:TokenStorePath` (default host config: `%LOCALAPPDATA%\\SiNet\\google-token`) | `%APPDATA%\\SiNet\\GoogleTokens` |
| App name | `SiNet` | Legacy-specific names (`OfficeConnector`, reports path, etc.) |

Verified wiring:

- `SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs` registers
  `AddSiNetSecrets()`.
- `src/SiNet.App.Wpf/App.xaml.cs` documents Vault as the source of truth for Gmail client secrets,
  binds only token-store override from `SINET_GOOGLE_TOKEN_STORE`, and restores connector sessions
  through `IConnectorAuthService` instead of resolving `GmailClientProvider` directly.
- The standalone `SiNet.App.Wpf` host now calls `AddSiNetSecrets()` as well, so
  `IGoogleClientSecretsPathProvider` is available there.
- `src/SiNet.App.Wpf/Inbox/InboxViewModel.cs` now consumes `IConnectorAuthService` for connect/state
  behavior instead of depending on `GmailClientProvider` directly.
- `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs` now consumes the same shared
  `IConnectorAuthService` plus `IEmailGateway`, loading real project-scoped Gmail summaries plus
  full body/attachment metadata by the project's canonical label leaf instead of hard-coding Gmail
  label traversal in WPF.
- `SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs` registers
  `AddSiNetGoogle(ConfigureNewSystemGmail)` additively inside the New System graph, with token store
  and app name mapped from the legacy host configuration, without switching legacy Google behavior.
- Therefore, in the standalone harness, native Gmail sign-in is vault-first, with
  `Gmail:ClientSecretsPath` acting only as fallback when a usable provider-backed path is not
  available.

### 3.4 Auth/session ownership

Closed foundation shape for New System Gmail:

- **Concrete session owner:** `GmailClientProvider`
- **App/WPF-facing auth seam:** `IConnectorAuthService` via `GmailConnectorAuthService`
- **Secrets materialization seam:** `IGoogleClientSecretsPathProvider`
- **Silent restore path:** startup resolves `IConnectorAuthService` and calls `TryRestoreSessionAsync()`
- **Interactive connect path:** explicit user action calls `IConnectorAuthService.LoginAsync()`

### 3.5 G-Startup closure (2026-07-04)

The **G-Startup** slice closes one specific gap only: V2 New System startup now performs the same
silent connector-auth restore as the standalone harness.

| Host | Silent restore at startup | Mechanism |
| --- | --- | --- |
| Standalone `src/SiNet.App.Wpf` | Yes | `GetServices<IConnectorAuthService>()` + `TryRestoreSessionAsync()` off UI thread |
| V2 New System (`RunNewSystemStartup`) | **Yes (G-Startup closed)** | `StartNewSystemConnectorAuthRestore()` — same port, same off-UI-thread pattern |
| V2 Legacy production startup | Unchanged | Legacy `GoogleService` / `GoogleAuthService` path only; no native restore added there |

Rules for this slice:

- Restore is **silent only** — no automatic interactive login, no retry loop, no new fallback.
- Token store policy is **unchanged** — V2 continues to map native Gmail to
  `AppConfiguration.GoogleTokenStorePath` via `ConfigureNewSystemGmail`.
- Legacy `GoogleService` production flows are **not replaced** by this change.
- **GmailSend** still requires a separate **G-Policy** decision before any window adoption.
- **MasterPlan Reports Sheets (R01–R03)** are approved native (S3); Inspection/other Sheets remain deferred.
- **Broad legacy window migration** remains blocked until G-Policy, ACC-Host contract, and other
  foundation gaps are closed.

Rules:

- No WPF window should resolve `GmailClientProvider` directly for connect/state behavior.
- No gateway should orchestrate OAuth on its own; gateways consume the shared provider/session.
- The standalone harness keeps `SINET_GOOGLE_TOKEN_STORE` as a token-store override only.
- The legacy production host now also registers the native Gmail module inside the New System graph,
  but that registration is **additive foundation wiring only**. It does not switch active legacy
  Google flows away from `GoogleService`.

## 4. User-level vs System-level Split

| Area | Correct auth model | Why |
| --- | --- | --- |
| Gmail mailbox read / send / reply as the signed-in user | **User OAuth** | Personal mailbox access is user-scoped |
| Gmail auth-state / connect / disconnect | **User OAuth** | Tied to the user's mailbox session |
| ProjectWork Drive file storage (list/download/upload/delete/rename) | **User OAuth** (same session as Gmail) | Locked 2026-07-16: shared credential provider + Shared Drive root |
| Organizational Sheets / Reports Drive automation | Candidate for **service account** only after explicit design | Changes ownership, sharing, and admin setup |

Rule of thumb:

- **Mailbox + ProjectWork Drive** stay **user OAuth** on the shared `GmailClientProvider` session.
- **Org-owned Sheets/Reports automation** must not be moved piecemeal; it needs an explicit ownership
  and permission strategy first.

### 4.1 Capability policy map

| Capability | Auth model | Scope status | Current policy |
| --- | --- | --- | --- |
| Gmail inbox read | User OAuth | Approved (`GmailReadonly`) | Native and allowed |
| Gmail silent restore / explicit connect | User OAuth | Approved | Native and allowed |
| Gmail send | User OAuth | Code present (`GmailSend`) | **Policy gap**: not broadly approved for window migration by default |
| Gmail modify / labels / mark-read | User OAuth | Code present (`GmailModify`) | Native for list filing/triage; re-consent may be required |
| Gmail full body / attachment metadata | User OAuth | Approved under `GmailReadonly` | Native and allowed for the first real email window |
| Drive ProjectWork read/list/open | User OAuth | Approved (`Drive`) | Native via `GoogleDriveFileStore` |
| Drive ProjectWork upload/write/delete/rename | User OAuth | Approved (`Drive`) | Native via `GoogleDriveFileStore` |
| Sheets read/write/export (MasterPlan R01–R03) | **User OAuth** (shared credential) | Approved (`Spreadsheets`) | Native S3; service account deferred |
| Inspection screenshot upload / other Reports Drive | Mixed legacy consumers | Not defined | Still deferred (not MasterPlan R0x) |

## 5. Guardrails

Until a separately approved design says otherwise:

1. **Do not consolidate** legacy `GoogleService` onto `AddSiNetGoogle` for non-Reports consumers.
2. **Do not unify token stores** beyond the existing V2 New System mapping of native Gmail/Drive to
   `AppConfiguration.GoogleTokenStorePath`.
3. **Do not add/remove scopes** in `GmailClientProvider` without an explicit approved slice
   (S3 approved `Spreadsheets` for MasterPlan Reports).
4. **MasterPlan R01–R03 Sheets** are the approved Reports consumer slice; do not ad-hoc port
   Inspection/other Sheets consumers without a new boundary decision.
5. **Do not reintroduce `SiNet.LegacyBridge`** into the native Gmail/Drive/Reports flow.
6. **Do not open a second OAuth flow** for Drive/Sheets — always reuse the shared user credential.
7. **Do not** introduce a service account for R0x in this slice.

## 6. Deferred Gaps

Still deferred after the ProjectWork Drive slice:

- Explicit product/policy decision on whether native `GmailSend` is approved for broad window adoption
- Attachment open/download behavior from the first real email window
- Gmail throttling / rate-limit parity
- ~~Google Sheets ports for MasterPlan R01–R03~~ — native S3
- Legacy-host switch / deletion of V2 R0x dialogs after soak
- Inspection Drive / screenshot Sheets consumers
- ~~Full production cutover of the legacy ProjectWork window (Phase 6)~~ — menu/task routing + native hubs wired; legacy `ProjectWorkView` file cleanup optional
- Service-account move for org Sheets automation (optional future)

`src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs` may consume `IConnectorAuthService` and
`IEmailGateway` for the read-only email window, but must not consume `GmailClientProvider` or
`IEmailSender` directly and must not grow send/modify behavior ad hoc.

## 6.1 Drive / Sheets / Reports status

| Area | Current decision | Notes |
| --- | --- | --- |
| Google Drive ProjectWork read/write | **Approved / native** | User OAuth; `SharedDriveId` + `ProjectsRootFolderId` |
| Google Sheets MasterPlan R01–R03 | **Approved / native** | User OAuth + `Spreadsheets`; see `MASTER_PLAN_MIGRATION.md` S3 |
| Inspection report generation / screenshot upload | Deferred | Separate consumer |

Auth policy:

- Gmail mailbox + ProjectWork Drive + MasterPlan Reports Sheets stay **user OAuth** on one shared credential.
- Service-account for org Sheets automation remains optional future design (not S3).

## 7. Recommended Next Step

1. Operator smoke for native R01–R03 (deferred consolidated debug session).
2. After soak: retire V2 R0x dialogs / GoogleConnector Reports dual path.
3. GmailSend broad adoption remains a separate policy decision.
