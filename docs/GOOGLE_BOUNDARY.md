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
| `SiNetProjectManagerV2` production host | Legacy `GoogleService` / `GoogleAuthService` / `GmailOutboundMailService` | No | Yes, via `AddSiNetNewSystemGraph()` | Vault is available to New System services, but production Google behavior remains legacy |
| `SiNet.App.Wpf` standalone harness | Native `GmailClientProvider` / `GmailEmailGateway` / `GmailEmailSender` | Yes | Yes | Native Gmail is wired with vault-first client-secrets resolution when the secrets provider can resolve them; config fallback remains available only when the provider cannot supply a path |

Implication: the clean Gmail module is real and testable, but it is **not** the production-host
implementation today. The standalone harness is now secrets-aware, yet production Gmail/Drive/Sheets
behavior still belongs to the legacy host.

## 3. Native Gmail Module Boundary

### 3.1 Artifacts

| File / type | Responsibility |
| --- | --- |
| `src/SiNet.Infrastructure.Google/GmailClientProvider.cs` | OAuth session, silent restore, interactive sign-in, cached Gmail client |
| `src/SiNet.Infrastructure.Google/GmailEmailGateway.cs` | Native Gmail read path (`IEmailGateway`) |
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
- `src/SiNet.App.Wpf/App.xaml.cs` documents Vault as the source of truth for Gmail client secrets and
  binds only token-store override from `SINET_GOOGLE_TOKEN_STORE`.
- The standalone `SiNet.App.Wpf` host now calls `AddSiNetSecrets()` as well, so
  `IGoogleClientSecretsPathProvider` is available there.
- Therefore, in the standalone harness, native Gmail sign-in is vault-first, with
  `Gmail:ClientSecretsPath` acting only as fallback when a usable provider-backed path is not
  available.

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
- Gmail full-body / attachments parity
- Gmail throttling / rate-limit parity
- Google Drive read/write ports and implementation
- Google Sheets ports and implementation
- Legacy-host switch from `GoogleService` to the native module
- Token-store strategy / one-time re-consent strategy across hosts
- Vault wiring for the standalone `SiNet.App.Wpf` harness host
- UI dependency cleanup: `InboxViewModel` still depends on concrete `GmailClientProvider` instead of auth abstraction
- Any service-account move for Sheets/Drive

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
