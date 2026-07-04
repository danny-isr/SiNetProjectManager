# Google Boundary

> **Status:** Draft - boundary alignment round (2026-07-03)  
> **Branch:** `SiWorkNet10`

This document records the **current code truth** for Google/Gmail/Drive/Sheets across the clean stack
and the legacy host. It exists to prevent doc/code drift while the refactor is split between:

- the native **module + harness** path (`SiNet.App.Wpf` + `SiNet.Infrastructure.Google`), and
- the legacy **production host** (`SiNetProjectManagerV2` + `SiOffice.GoogleConnector`).

When this document and the code disagree, fix the document first, then make an explicit follow-up
decision for any behavior change.

## 1. Executive Boundary

- **Native today:** Gmail **read**, Gmail auth-state/health, interactive/silent sign-in, and a native
  Gmail **send capability** exist in `SiNet.Infrastructure.Google`.
- **Production today:** `SiNetProjectManagerV2` still uses the legacy Google path for Gmail/Drive/Sheets;
  the native module is not yet the production-host runtime path.
- **Still legacy today:** all Google **Sheets**, all Google **Drive**, legacy outbound send flow,
  legacy Gmail modify/throttle/full-body paths, and every production host consumer under
  `SiNetProjectManagerV2`.
- **Not used at all:** Google Docs, Google Calendar, Google Tasks.
- **Blocked by design:** token-store consolidation and legacy-host replacement. The native and legacy
  Google paths are **not behavior-equivalent**.
- **Open decision:** `GmailSend` is present in code and in the native module scope set; product/policy
  approval for that send capability must remain explicit and documented.

## 2. Active Ownership Map

| Capability | Active implementation | Stack | Current status |
| --- | --- | --- | --- |
| Gmail inbox read (project label scoped) | `InboxViewModel` -> `IEmailGateway` -> `GmailEmailGateway` -> `GmailClientProvider` | Native | Active |
| First real email window (read-only content/details) | `EmailWindowViewModel` -> `IEmailGateway` + `IConnectorAuthService` | Native | Active, read-only body + attachment metadata |
| Gmail auth/health bridge | `IConnectorAuthService` -> `GmailConnectorAuthService` | Native | Active |
| Gmail send capability | `IEmailSender` -> `GmailEmailSender` | Native module | Implemented in code; host adoption is still separate |
| Gmail outbound send used by legacy flows | `GmailOutboundMailService` / `GoogleService` | Legacy host | Active |
| Gmail modify / labels / mark-read | `GoogleService` | Legacy host | Active, not ported |
| Gmail full-body / attachments / throttle | `GoogleService` / `GmailThrottleService` | Legacy host | Active, not ported |
| Google Sheets reports (`R01/R02/R03`) | `SiOffice.GoogleConnector/Reports/*` | Legacy host | Active, not ported |
| Google Sheets migration readers | `SiNetProjectManagerV2/Services/Migration/*` | Legacy host | Active, not ported |
| Google Drive project-file read path | `GoogleDriveServiceProvider` | Legacy host | Active, not ported |
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
| `src/SiNet.Infrastructure.Google/GmailConnectorAuthService.cs` | Auth-state / health bridge (`IConnectorAuthService`) |
| `src/SiNet.Infrastructure.Google/GoogleServiceCollectionExtensions.cs` | Registers read + auth-state + send over the shared provider |
| `src/SiNet.Infrastructure.Google/GmailOptions.cs` | Configurable options: client secrets path, token store path, app name, root label, interactive sign-in |

### 3.2 Current scope truth

The live code in `GmailClientProvider` currently uses these hard-coded scopes:

- `GmailService.Scope.GmailReadonly`
- `GmailService.Scope.GmailSend`

That means the native module is **not** "read-only" anymore at the OAuth level. The **read gateway**
remains read-only in behavior, but the module also contains a native send capability and therefore a
send-capable scope set.

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
| Organizational Sheets/Drive report generation | Candidate for **service account** only after explicit design | Changes ownership, sharing, and admin setup |
| Drive-backed project file storage / automation | Candidate for **service account** only after explicit design | Crosses storage-destination and org-permission boundaries |

Rule of thumb:

- **Mailbox behavior** stays user OAuth unless a later approved design says otherwise.
- **Org-owned Sheets/Drive automation** must not be moved piecemeal; it needs an explicit ownership and
  permission strategy first.

### 4.1 Capability policy map

| Capability | Auth model | Scope status | Current policy |
| --- | --- | --- | --- |
| Gmail inbox read | User OAuth | Approved (`GmailReadonly`) | Native and allowed |
| Gmail silent restore / explicit connect | User OAuth | Approved | Native and allowed |
| Gmail send | User OAuth | Code present (`GmailSend`) | **Policy gap**: not broadly approved for window migration by default |
| Gmail modify / labels / mark-read | User OAuth | Not added | Deferred |
| Gmail full body / attachment metadata | User OAuth | Approved under `GmailReadonly` | Native and allowed for the first real email window |
| Drive read/list/open | TBD (likely user OAuth or service account by domain) | Not defined | Deferred until a ProjectFiles consumer is selected |
| Drive upload/write | TBD | Not defined | Deferred; do not implement ad hoc |
| Sheets read/write/export | Candidate service account only after explicit design | Not defined | Deferred; keep under Reports ownership |
| Reports generation / screenshot upload | Mixed legacy consumers today | Not defined | Deferred until a Reports boundary is selected |

## 5. Guardrails

Until a separately approved design says otherwise:

1. **Do not consolidate** legacy `GoogleService` onto `AddSiNetGoogle`.
2. **Do not unify token stores** between native and legacy paths.
3. **Do not add/remove scopes** in `GmailClientProvider` without an explicit approved slice.
4. **Do not port Drive/Sheets ad hoc** from random consumers; introduce clean ports only when a real
   migration slice is approved.
5. **Do not reintroduce `SiNet.LegacyBridge`** into the native Gmail read/send flow.

## 6. Deferred Gaps

Still deferred after the native Gmail slice:

- Explicit product/policy decision on whether native `GmailSend` is approved capability or code-present only
- Gmail modify / labels / mark-read parity
- Attachment open/download behavior from the first real email window
- Gmail throttling / rate-limit parity
- Google Drive read/write ports and implementation
- Google Sheets ports and implementation
- Legacy-host switch from `GoogleService` to the native module
- Token-store strategy / one-time re-consent strategy across hosts
- Any service-account move for Sheets/Drive

Minimum parity decision before migrating the first real email window:

- Required foundation: auth/session ownership, vault-first secrets path resolution, token-store
  policy, read summaries, full body/attachment metadata, and explicit connect/restore behavior.
- Optional read expansion: HTML rendering or attachment-open/download behavior, but only if the
  chosen window genuinely requires them.
- Explicitly out for now: send-by-default adoption, modify/labels, Drive, Sheets, and reports.
- `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs` is now allowed to consume
  `IConnectorAuthService` and `IEmailGateway` for the first real read-only window slice, but it must
  not consume `GmailClientProvider` or `IEmailSender` directly and must not grow send/modify behavior
  ad hoc.

## 6.1 Drive / Sheets / Reports defer decision

This foundation round makes the defer policy explicit:

| Area | Current decision | Ownership gate before any runtime move |
| --- | --- | --- |
| Google Drive read/list/open | Deferred | Requires an approved `ProjectFiles` / storage-destination slice |
| Google Drive upload/write | Deferred | Requires explicit storage ownership + auth model decision |
| Google Sheets read/write/export | Deferred | Requires an approved `Reports` boundary and ownership model |
| Report generation / screenshot upload | Deferred | Requires the report/export consumer to be selected first |

Auth policy before any later move:

- Gmail mailbox behavior stays **user OAuth**.
- Drive/Sheets/report automation is a separate ownership problem and is a **candidate** for service
  account or other org-owned auth only after explicit design.
- Do **not** choose that auth model opportunistically inside Gmail migration work.

Guardrails:

- No runtime movement of Drive, Sheets, or report/export code until a real consumer slice is named.
- No ad-hoc ports or infra adapters for Drive/Sheets just because the native Gmail module exists.
- The first real email window must not become the accidental migration home for Drive or Sheets.

## 7. Recommended Next Step

Google should **not** be the next implementation-heavy slice after ACC. The safe follow-up is to
keep Google work staged and policy-led:

1. **G1 — Policy alignment first**
   - explicit Gmail send approval/non-approval,
   - explicit scope policy,
   - explicit token-store coexistence policy,
   - explicit definition of what “Google health” means in product terms.
2. **G2 — Auth/config clarification**
   - vault-first secrets source,
   - allowed config fallback,
   - no forced token-store consolidation.
3. **G3 — Gmail parity only if explicitly approved**
   - full body / attachments / modify / labels / throttling,
   - sender-context gaps only if required by real production-host adoption.
4. **G4 — Drive only behind a ProjectFiles/storage slice**
   - no ad-hoc Drive migration.
5. **G5 — Sheets / Reports after a clear application boundary exists**
   - no opportunistic Sheets/report rewrites before ownership and auth policy are explicit.

Until G1 is settled, keep Google changes limited to **approved documentation/policy alignment** and
do not attempt production-host consolidation.
