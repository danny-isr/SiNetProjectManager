# Google Boundary

> **Status:** Active - ProjectWork Drive slice (2026-07-16)  
> **Branch:** `SiWorkNet10`

This document records the **current code truth** for Google/Gmail/Drive/Sheets across the clean stack
and the legacy host. It exists to prevent doc/code drift while the refactor is split between:

- the native **module + harness** path (`SiNet.App.Wpf` + `SiNet.Infrastructure.Google`), and
- the legacy **production host** (`SiNetProjectManagerV2` + `SiOffice.GoogleConnector`).

When this document and the code disagree, fix the document first, then make an explicit follow-up
decision for any behavior change.

## 1. Executive Boundary

- **Native today:** Gmail **read**, Gmail auth-state/health, interactive/silent sign-in, native Gmail
  **send/modify**, and **ProjectWork Google Drive** (read + write via `IFileStore`) exist in
  `SiNet.Infrastructure.Google`.
- **Shared auth (locked):** one user OAuth session (`GmailClientProvider`) owns `UserCredential` for
  both Gmail and Drive; automatic token refresh; no per-window / per-operation login. UI uses
  `IConnectorAuthService` only.
- **Drive base folder (locked):** `GoogleDrive:SharedDriveId` + `GoogleDrive:ProjectsRootFolderId`
  (central projects root). All ProjectWork Drive paths are resolved under that root.
- **Production today:** `SiNetProjectManagerV2` still uses the legacy Google path for Sheets/Reports
  and for the legacy ProjectWork window; the New System graph registers the native module additively.
- **Still legacy today:** all Google **Sheets**, Reports/Inspection Drive consumers, legacy outbound
  send flow, and legacy Gmail throttle/full-body paths under `SiNetProjectManagerV2`.
- **Not used at all:** Google Docs, Google Calendar, Google Tasks.
- **Blocked by design:** forced token-store consolidation with unrelated legacy Reports clients beyond
  the existing V2 New System mapping. Native and legacy Google paths are **not** fully
  behavior-equivalent for Sheets/Reports.
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
| Google Sheets reports (`R01/R02/R03`) | `SiOffice.GoogleConnector/Reports/*` | Legacy host | Active, not ported |
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
- `DriveService.Scope.Drive` (full Drive — ProjectWork read/write)

That means the native module is **not** "read-only" anymore at the OAuth level. The **read gateway**
remains read-only in behavior, but the module also contains native send, **label-modify**, and
**Drive** capabilities. Existing users must perform a **one-time interactive re-consent** when a
previously stored token lacks Drive (or send/modify). Silent restore still works for previously
granted scopes; insufficient-scope Drive calls surface as `GoogleConsentRequiredException`.

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
- **Drive / Sheets / Reports** remain legacy/deferred.
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
| Sheets read/write/export | Candidate service account only after explicit design | Not defined | Deferred; keep under Reports ownership |
| Reports generation / screenshot upload | Mixed legacy consumers today | Not defined | Deferred until a Reports boundary is selected |

## 5. Guardrails

Until a separately approved design says otherwise:

1. **Do not consolidate** legacy `GoogleService` onto `AddSiNetGoogle` for Sheets/Reports.
2. **Do not unify token stores** beyond the existing V2 New System mapping of native Gmail/Drive to
   `AppConfiguration.GoogleTokenStorePath`.
3. **Do not add/remove scopes** in `GmailClientProvider` without an explicit approved slice.
4. **Do not port Sheets/Reports Drive** ad hoc; ProjectWork Drive is the approved consumer slice.
5. **Do not reintroduce `SiNet.LegacyBridge`** into the native Gmail/Drive ProjectWork flow.
6. **Do not open a second OAuth flow** for Drive — always reuse the shared user credential.

## 6. Deferred Gaps

Still deferred after the ProjectWork Drive slice:

- Explicit product/policy decision on whether native `GmailSend` is approved for broad window adoption
- Attachment open/download behavior from the first real email window
- Gmail throttling / rate-limit parity
- Google Sheets ports and implementation
- Legacy-host switch of Reports/Inspection Drive consumers from `GoogleAuthService`
- ~~Full production cutover of the legacy ProjectWork window (Phase 6)~~ — menu/task routing + native hubs wired; legacy `ProjectWorkView` file cleanup optional
- Any service-account move for Sheets/Reports

`src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs` may consume `IConnectorAuthService` and
`IEmailGateway` for the read-only email window, but must not consume `GmailClientProvider` or
`IEmailSender` directly and must not grow send/modify behavior ad hoc.

## 6.1 Drive / Sheets / Reports status

| Area | Current decision | Notes |
| --- | --- | --- |
| Google Drive ProjectWork read/write | **Approved / native** | User OAuth; `SharedDriveId` + `ProjectsRootFolderId` |
| Google Sheets read/write/export | Deferred | Requires an approved `Reports` boundary and ownership model |
| Report generation / screenshot upload | Deferred | Requires the report/export consumer to be selected first |

Auth policy:

- Gmail mailbox + ProjectWork Drive stay **user OAuth** on one shared credential.
- Sheets/Reports automation remains a separate ownership problem (service-account candidate only
  after explicit design).

## 7. Recommended Next Step

1. Optional cleanup: delete unused legacy `ProjectWorkView` / `ProjectWorkViewModel` after soak.
2. Keep Sheets/Reports on the legacy Google path until a Reports boundary is approved.
3. GmailSend broad adoption remains a separate policy decision.
