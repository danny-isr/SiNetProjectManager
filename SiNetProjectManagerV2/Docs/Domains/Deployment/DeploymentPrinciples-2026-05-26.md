# Deployment Principles

- **Decision date / Updated date:** 26.05.2026 / **07.08.2026** (desktop SoT retarget)
- **Status:** **Superseded for desktop production SoT** -- keep for cross-cutting install/auth principles; desktop channel SoT is [`docs/RELEASE_PROCESS.md`](../../../../docs/RELEASE_PROCESS.md) + root [`DEPLOYMENT.md`](../../../../DEPLOYMENT.md) (`SiNet.App.Wpf`).
- **Scope:** Office installation, Windows Service, scheduled tasks, WPF
  client (historical V2 MSIX + current App.Wpf), prerequisites, shared folders, ACC
  service, Google connection, WebView2 runtime / profile, configuration,
  diagnostics / `System Status`, and **centralized Autodesk / Google
  authorization**.
- **Reconciliation:** [`docs/DOCUMENTATION_RECONCILIATION_2026-08-07.md`](../../../../docs/DOCUMENTATION_RECONCILIATION_2026-08-07.md)

## Purpose
Define how the application is installed, updated, and authorized in a
customer office, anchored on the actual deployment scripts that already
exist in this repository.

## Source of truth
- Repository-level [`DEPLOYMENT.md`](../../../../DEPLOYMENT.md) is the
  TL;DR for the whole solution and the **master script entry point**.
- [`docs/RELEASE_PROCESS.md`](../../../../docs/RELEASE_PROCESS.md) for
  publish gates and the production desktop channel (`SiNet.App.Wpf`).
- [`SiOffice.AccService\DEPLOYMENT.md`](../../../../SiOffice.AccService/DEPLOYMENT.md)
  for the `SiOffice.AccService` Windows Service (WiX MSI, MajorUpgrade,
  `Install-OnServer.ps1`).
- [`SiNetProjectManagerV2\DEPLOYMENT.md`](../../../DEPLOYMENT.md) for
  the **historical** V2 WPF client (MSIX + `.appinstaller`) -- **Superseded** as desktop SoT.
- [`MasterPlan.SyncEngine\DEPLOYMENT.md`](../../../../MasterPlan.SyncEngine/DEPLOYMENT.md)
  for the scheduled-task console.
- This document for **cross-cutting** deployment, authorization,
  WebView2, and System Status principles.

## Existing deployment artefacts (do not duplicate)

This section documents the deployment scripts that already exist in the
repository. **Do not** create parallel scripts; extend the existing
ones if and when needed (in a future approved round).

| Channel | Artefact | Master script | Per-channel script | Network target |
|---|---|---|---|---|
| WPF client (`SiNetProjectManagerV2`) | MSIX + `.appinstaller` (auto-update on launch) | [`publish-all.ps1`](../../../../publish-all.ps1) | [`SiNetProjectManagerV2\publish-desktop.ps1`](../../../publish-desktop.ps1) | `\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\` |
| `SiOffice.AccService` (Windows Service) | WiX MSI (`SiOfficeAccService.msi`, MajorUpgrade) | [`publish-all.ps1`](../../../../publish-all.ps1) | [`SiOffice.AccService\publish-service.ps1`](../../../../SiOffice.AccService/publish-service.ps1) | `\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\` |
| `MasterPlan.SyncEngine` (scheduled task) | self-contained single-file EXE | [`publish-all.ps1`](../../../../publish-all.ps1) | [`MasterPlan.SyncEngine\publish-console.ps1`](../../../../MasterPlan.SyncEngine/publish-console.ps1) | `\\SI-WIN-2K19\AppFolder\AppNet\MasterPlan.SyncEngine\` |
| `SiNet.SecretImport` (portable provisioner) | self-contained single-file EXE | [`publish-all.ps1`](../../../../publish-all.ps1) | [`SiNet.SecretImport\publish-tool.ps1`](../../../../SiNet.SecretImport/publish-tool.ps1) | `\\SI-WIN-2K19\AppFolder\AppNet\SiNet.SecretImport\` |
| Server-side install (AccService + secrets) | unified installer + vault import | — | [`SiOffice.AccService\Install-OnServer.ps1`](../../../../SiOffice.AccService/Install-OnServer.ps1) | runs on `SI-WIN-2K19`; consumes the MSI + `SiNet.secrets` |

Key facts derived from the existing scripts (informational, not a
specification to extend in this round):

- `publish-all.ps1` runs the four channels in order and bumps each
  `<Version>` independently in its `.csproj`. Channels can be skipped
  with `-SkipService` / `-SkipConsole` / `-SkipDesktop` / `-SkipTool`.
- `publish-desktop.ps1` produces a signed MSIX and an `.appinstaller`
  that points to the UNC share; end-user workstations auto-update on
  launch (`OnLaunch HoursBetweenUpdateChecks="0"` +
  `AutomaticBackgroundTask`). Code-signing certificate `CN=SI Office`
  is selected from `Cert:\CurrentUser\My` or supplied via
  `-CertThumbprint` / `-CertPfxPath`.
- `publish-service.ps1` builds via Visual Studio MSBuild (COM ref
  workaround for `MSB4803`), publishes framework-dependent
  (`--self-contained false`), and produces `SiOfficeAccService.msi` via
  WiX; it also publishes `Install-OnServer.ps1` next to the MSI on the
  share.
- `Install-OnServer.ps1` (run elevated on the server) imports the
  `SiNet.secrets` package into the **per-user Windows Credential
  Manager (DPAPI) vault** of the service account (default
  `SI-ENG\sieng`), then installs the Windows Service to **run as that
  same account**, then verifies vault status and service state.
- Default network root: `\\SI-WIN-2K19\AppFolder\AppNet\`. WPF clients
  read `.appinstaller`, the Task Scheduler points to the SyncEngine
  EXE, and the server installs the MSI from this share.
- Code-signing prerequisites and Windows 10/11 SDK tooling
  (`MakeAppx.exe`, `SignTool.exe`) are required on the build machine
  per the published `DEPLOYMENT.md`.

## Core principles

### Components and responsibilities

1. **WPF client (`SiNetProjectManagerV2`)**: installed via MSIX +
   `.appinstaller`; auto-updates on launch. The client is **not**
   privileged and must not perform privileged ACC orchestration when
   `AccService:BaseUrl` is configured (it routes through the service).
2. **`SiOffice.AccService` (server / office service)**: privileged
   Windows Service installed via WiX MSI. Requires Account Admin /
   Project Admin / Folder `CONTROL` rights on ACC and is reached over
   HTTPS. Office Inbox ensure is exposed through the service endpoint
   and is the **central remote provisioning path**.
3. **`MasterPlan.SyncEngine`**: scheduled-task console, runs on the
   server from the network share; uses the same per-user DPAPI vault as
   the service account.
4. **`SiNet.SecretImport`**: portable provisioner used by
   `Install-OnServer.ps1` to seed the per-user Credential Manager vault.

### External dependencies

5. **ACC Service dependency.** The client uses `AccService:BaseUrl` to
   reach the service; service-mode is the standard production path. See
   [`Domains\ACC\AccSystemPrinciples-2026-05-26.md`](../ACC/AccSystemPrinciples-2026-05-26.md)
   and
   [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Architecture/ServiceCatalog-2026-05-26.md).
6. **Database dependency.** SQL is the source of truth for business
   process / cache for ACC; connection strings live in configuration,
   not in source documents. **No migrations at startup** (see
   *Startup boundary* below).
7. **File Server dependency.** The shared root
   `\\SI-WIN-2K19\AppFolder\AppNet\` hosts MSIX / `.appinstaller`, MSI,
   SyncEngine EXE, and the SecretImport EXE. Write access is required
   on the build machine; read access is required on every client and
   on the server.
8. **Google / Gmail / Drive / Sheets dependency.** Provided through
   `SiOffice.GoogleConnector` / `GoogleService`. Gmail is **read-only
   ingestion**; Google Drive **upload remains postponed**; Google
   Sheets is integration / reporting / template surface only. See
   [`Domains\Email\EmailSystemPrinciples-2026-05-26.md`](../Email/EmailSystemPrinciples-2026-05-26.md)
   §11 and
   [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../ProjectFiles/ProjectFilesPrinciples-2026-05-26.md)
   § *Google service boundary for project files*.
9. **WebView2 Runtime dependency.** Required on every WPF client
   workstation. Used by Gmail / ACC / browser-style login UI. See
   *WebView2 profile / `UserDataFolder`* below.
10. **Configuration / `appsettings` dependency.** Per-environment
    `appsettings.*.json` and `AccService:BaseUrl` are deployment
    settings; secrets live in the per-user Credential Manager vault on
    the server (seeded by `Install-OnServer.ps1` / `SiNet.SecretImport`).
    **Secrets are never stored in source documents.**
11. **Default Office Management project ID is `136`** (not `126`), used
    for project-independent workflows.

### Logs / diagnostics / System Status

12. **Logs and structured diagnostics** follow
    [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Diagnostics/DiagnosticsPrinciples-2026-05-26.md).
    Deployment must not introduce a parallel logging channel; the
    existing central logging (e.g. `AppLogger`) is used.
13. **The existing `System Status` menu / window** is the central
    health surface and must be **reused or extended**, never duplicated.
    System Status reflects, at a minimum:
    - `ACC Service` availability (`AccService:BaseUrl` reachable, health
      endpoint OK),
    - **Autodesk authorization** status (token present, valid, not
      expired),
    - **Google authorization** status (per the relevant Google scope:
      Gmail / Drive / Sheets),
    - **DB connection** availability,
    - **File Server** availability
      (`\\SI-WIN-2K19\AppFolder\AppNet\` reachable),
    - **Google / Gmail** availability (API reachable, not just token),
    - **WebView2 Runtime** availability (installed on the workstation),
    - **AI service** availability (if AI is wired in for the
      installation).
    See
    [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Diagnostics/DiagnosticsPrinciples-2026-05-26.md)
    and
    [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Architecture/ServiceCatalog-2026-05-26.md).
14. **A parallel `System Status` mechanism is not approved.**

### Startup boundary

15. **At startup, the WPF client may only**:
    - perform **lightweight availability checks** (ping `AccService`
      health endpoint, DB connection probe, File Server reachability
      probe, WebView2 runtime presence check),
    - **update `System Status`** with the results of those checks,
    - load configuration / `appsettings`,
    - skip local `AccUserBootstrapService.ProvisionUsersAsync` when
      `AccService:BaseUrl` is configured (already an established rule).
16. **At startup, the WPF client must NOT**:
    - run a **global / system-wide scan** of projects or files,
    - run **EF migrations** (manual migration workflow only — see
      [`Domains\Architecture\ArchitecturePrinciples-2026-05-26.md`](../Architecture/ArchitecturePrinciples-2026-05-26.md)
      § *Manual migration rule*),
    - perform **ACC project creation** or **wide provisioning** of
      users / projects / custom-attribute definitions outside the
      dedicated ACC Inbox provisioning path,
    - perform **automatic uploads** to ACC / Google Drive / File Server,
    - change **workflow / task** state,
    - apply **silent fallbacks** that hide a failed dependency (e.g.
      AccService unreachable ⇒ silently switch to local privileged
      orchestration).

## Centralized Autodesk / Google authorization (added 26.05.2026)

Autodesk and Google authorization must be **centralized and reused
across the application**. The intent:

- Not every window creates a new authorization flow.
- Not every `ViewModel` manages its own token.
- Not every WebView opens a separate profile when the same session is
  required.
- There is a **single central mechanism (or, at minimum, a single
  central principle)** for:
  - **Autodesk user authorization** (three-legged user flow used by the
    WPF client where required),
  - **Google user authorization** (Gmail / Drive / Sheets scopes used
    by `SiOffice.GoogleConnector` / `GoogleService`),
  - **Service-side / two-legged authorization through `SiOffice.AccService`**
    where applicable (the service holds its own credentials in the
    per-user DPAPI vault seeded by `Install-OnServer.ps1`; the WPF
    client does **not** duplicate that two-legged flow locally).
  - **WebView2 session / cookies / `UserDataFolder` policy** (see next
    section).

**If authorization is missing or expired**:

- A **single central flow** is invoked.
- `System Status` is updated to reflect the auth state.
- A **clear, actionable message** is shown to the user.
- The application **must not** repeatedly raise an auth prompt from
  every window.

**Forbidden**:

- Authorization prompts triggered per `ViewModel` / per window.
- Per-window token stores.
- The WPF client bypassing `AccService` two-legged paths when
  `AccService:BaseUrl` is configured.
- Silent fallback from `AccService` to a local privileged path.
- Storing OAuth tokens / Autodesk tokens in source documents or in
  unsecured files; client tokens use the platform secure storage, and
  server secrets use the per-user DPAPI vault seeded by
  `Install-OnServer.ps1`.

## WebView2 profile / `UserDataFolder` policy (added 26.05.2026)

- If WebView2 is used for **Gmail**, **ACC**, or a **browser-style
  login**, the location of the **session / browser profile /
  `UserDataFolder`** must be **explicit and documented**.
- When multiple windows must **share a login**, they must use the
  **same `UserDataFolder` / profile policy**. A random / default
  `UserDataFolder` per window is **not approved**.
- If a specific window **requires session isolation** (a deliberate
  separate profile), that isolation must be **documented** with the
  reason; isolation by accident is not allowed.
- This policy is documentation only in this round; no new WebView2
  profile mechanism is created.

## Secrets / credentials / token storage (added 26.05.2026)

Three distinct kinds of sensitive material must remain **separated**,
each with **central ownership**. No new vault, no new token store, and
no new WebView2 profile is created in this round — this section
documents the existing structure and the rules that apply to it.

### 1) Service secrets

Secrets used by the **service / server / shared infrastructure**:
ACC Service API key + TLS cert / password, database connection strings,
Autodesk **client_id / client_secret** (application credentials,
**not** end-user tokens), Active Directory bind credentials,
MasterPlan API key, Gemini / AI API keys, and the Google **OAuth
client_secrets.json** content. These are imported and stored through
the existing official path:

- `SiNet.secrets` — an encrypted, portable provisioning file
  (AES-256-CBC + PBKDF2, custom `SNET` header) produced and consumed by
  `SiNetProjectManagerV2\Services\SecretProvisioningService.cs`.
- [`SiNet.SecretImport`](../../../../SiNet.SecretImport/) — portable
  single-file EXE used by
  [`SiOffice.AccService\Install-OnServer.ps1`](../../../../SiOffice.AccService/Install-OnServer.ps1)
  to seed the **per-user Windows Credential Manager (DPAPI) vault** of
  the service account on the server (default `SI-ENG\sieng`).
- `CredentialVaultService` (`SiNetSQL\Services\CredentialVaultService.cs`)
  — the **single read/write API** to Windows Credential Manager for the
  application. Targets are `Generic` credentials persisted as
  `CRED_PERSIST_LOCAL_MACHINE`, decrypted via DPAPI by the current
  Windows user only.
- `SecretKeys` (`SiNetSQL\Services\SecretKeys.cs`) — the **single
  central list** of secret target names. New secrets must be added here,
  not invented at call sites.
- On end-user workstations, the same vault is provisioned via the
  `SecretSetupWindow` dialog (`SiNetProjectManagerV2\WPF Window\
  SecretSetupWindow.xaml.cs`), which writes through
  `CredentialVaultService` only.

Rules:

- **No service secrets in `git`.** `appsettings.json` may carry
  non-secret configuration and `_SecretsNote` placeholders only;
  observed today: `SiNetProjectManagerV2\appsettings.json` documents
  this rule explicitly (`"_SecretsNote": "API keys, client secrets, and
  connection strings are stored in Windows Credential Manager
  (encrypted per-user)…"`) and `SiOffice.AccService\appsettings.json`
  documents the same rule for `ConnectionStrings:SiNetDatabase`.
- **`AccService:ApiKey` and `AccService:Certificate:Password`** in the
  service `appsettings.json` must remain **empty in source**; real
  values come from the vault on the server.
- **No service secrets in scripts** (`*.ps1`). The deployment scripts
  must not contain secret values; they consume `SiNet.secrets` plus a
  one-time interactive password prompt for the service account.
- **`SiNet.secrets`** is an encrypted artefact and must not be committed.
- **Vault access is per Windows user.** A secret set on a developer
  workstation is not available to other Windows users, by design (DPAPI).

### 2) User OAuth tokens

End-user authorizations against external identity providers:

- **Autodesk three-legged** user authorization is performed by
  `SiOffice.AutodeskConnector\TokenProvider.cs`. Its `client_id` /
  `client_secret` come from the vault (`SecretKeys.AutodeskClientId` /
  `SecretKeys.AutodeskClientSecret`); the **refresh token** is stored
  in `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` per Windows
  user (documented in the connector source).
- **Google user authorization** (Gmail / Drive / Sheets scopes) is
  performed by `SiOffice.GoogleConnector\Reports\GoogleAuthService.cs`
  via `GoogleWebAuthorizationBroker` + `FileDataStore`. Tokens are
  stored under the configured token-store path
  (`GoogleReports:TokenStorePath`, default
  `%APPDATA%\SiNet\GoogleTokens`); the **OAuth client_secrets.json**
  content lives in the vault under `SecretKeys.GoogleClientSecrets`
  and is materialised on demand to
  `%LOCALAPPDATA%\SiNet\Secure\credentials.json` by
  `AppConfiguration.GetGoogleClientSecretsPath()`.
- **Service-side / two-legged** authorization (when used through
  `SiOffice.AccService`) is **not** duplicated locally on the WPF
  client when `AccService:BaseUrl` is configured (see § *Centralized
  Autodesk / Google authorization* above).

Rules:

- User OAuth tokens are **distinct** from service secrets and must not
  be mixed (no Autodesk refresh token in the Credential Manager vault
  alongside service secrets; no Google user token in `SiNet.secrets`).
- **Centralized flow.** Per-window / per-`ViewModel` authorization
  prompts and per-window token stores are **not approved** (see
  § *Centralized Autodesk / Google authorization*).
- The Google **OAuth client_secrets.json** is a service secret (the
  application's identity), not a user token; it stays in the vault. The
  **user's** Google access/refresh tokens are owned by
  `FileDataStore` under the configured token-store path.

### 3) WebView2 session / profile

WebView2 sessions are **not** token stores, but they hold cookies and
SSO state that affect login. The current policy is implemented in
`SiNetProjectManagerV2\WPFUserControl\WebView2Helper.cs` and
`SiNetProjectManagerV2\Services\AppConfiguration.cs`:

- Base path: `AppConfiguration.WebView2UserDataBasePath`, configurable
  via `WebView2:UserDataBasePath`; default
  `%LOCALAPPDATA%\SiNetProjectManagerV2\WebView2UserData`.
- **Per-Google-account subfolder** for Gmail / Google SSO sessions
  (sanitised email under the base path) via
  `WebView2Helper.CreateUserEnvironmentAsync()`. All windows that need
  the same Google login share this profile.
- **Separate `acc_viewer` subfolder** for the ACC document viewer via
  `WebView2Helper.CreateAccEnvironmentAsync()`, deliberately isolated
  from the Gmail session.
- No window creates a random / default `UserDataFolder`; isolation is
  intentional and documented in the helper.

Rules:

- A new `UserDataFolder` per window **without a documented reason** is
  **not approved**.
- A new WebView2 profile is **not created in this round**.
- Any future window that needs to share login with Gmail / ACC must
  reuse the existing subfolder; any window that requires isolation must
  document the reason next to its environment creation.

### Cross-cutting rules for all three kinds

- **No secrets in `git`.** No service secrets, OAuth tokens, refresh
  tokens, authorization codes, passwords, or cookies in any tracked
  file.
- **No secrets in plain `appsettings`.** `appsettings*.json` carries
  non-secret configuration and explicit `_SecretsNote` placeholders
  only.
- **No secrets in logs.** Logs must not include secret values, OAuth
  tokens, refresh tokens, authorization codes, passwords, or cookies;
  log presence / absence and status / failure category instead. See
  [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Diagnostics/DiagnosticsPrinciples-2026-05-26.md).
- **No duplicate token stores.** Service secrets live in the Windows
  Credential Manager vault (via `CredentialVaultService`); Autodesk
  refresh tokens live under `%LOCALAPPDATA%\SiNet\Autodesk\`; Google
  user tokens live under the configured `FileDataStore` path. New
  parallel stores are **not approved**.
- **`System Status` displays auth/service health, never secret values.**
  Show "configured / missing / expired / unreachable", not the secret
  itself.
- **Diagnostic helpers that handle secrets stay opt-in.** Disabled /
  postponed mechanisms are not silently revived.

## What we do not do now
- Do not add startup-time browser authorization or unrelated ACC
  bootstrap on remote clients in service mode.
- Do not change service architecture, `TokenProvider`, `Bim360Service`,
  or authentication flows as part of deployment documentation.
- Do not store secrets in source documents.
- Do not create new deployment scripts in this round; extend the
  existing ones only via a future approved round.
- Do not change the authorization mechanism in this round; this is a
  documentation / principle round only.
- Do not introduce per-window authorization flows or per-window WebView2
  `UserDataFolder`s.
- Do not introduce a parallel `System Status` mechanism.
- Do not perform global scan / migrations / ACC project creation / wide
  provisioning / automatic uploads / workflow changes / silent fallback
  at startup.

## Dropped / cancelled / postponed
- Running privileged ACC orchestration locally on remote clients when
  service mode is configured — dropped.
- New Google Drive upload mechanism / Google Drive fallback at
  deployment level without an explicit decision — not approved.
- Using `SiOffice.GoogleConnector` / `GoogleService` as a general
  business engine in any deployment configuration — dropped.
- Full step-by-step office install runbook in this document —
  postponed (lives in service-specific `DEPLOYMENT.md` files).
- Per-window Autodesk / Google authorization — **not approved**.
- Separate token / WebView2 profile per window without a documented
  reason — **not approved**.
- Parallel `System Status` mechanism — **not approved**.
- Startup-time wide provisioning / global scan / migrations / ACC
  project creation / automatic uploads / workflow changes —
  **not approved**.
- Silent fallback from `AccService` to a local path — **not approved**.
- Creating a new deployment script in this round — **not in this round**.
- Fixing authorization code in this round — **not in this round**.
- Storing service secrets, OAuth tokens, refresh tokens, authorization
  codes, passwords, or cookies in `git`, in `appsettings*.json`, in
  `*.ps1` scripts, or in any other tracked / plain-text file —
  **not approved**.
- Logging secret values, OAuth tokens, refresh tokens, authorization
  codes, passwords, or cookies — **not approved**.
- Mixing service secrets with user OAuth tokens in a single store —
  **not approved**.
- Creating a new token store / vault / WebView2 profile in parallel to
  the existing ones — **not approved**.
- Showing secret values in `System Status` — **not approved** (show
  configured / missing / expired / unreachable only).
- Moving existing secrets across stores in this round — **not in this
  round**.

## Relevant terms / search terms
SiOffice.AccService, SiOffice.AutodeskConnector, SiOffice.GoogleConnector,
AccService:BaseUrl, AccUserBootstrapService, TokenProvider, Bim360Service,
two-legged, three-legged, Office Inbox ensure, default project 136,
`publish-all.ps1`, `publish-desktop.ps1`, `publish-service.ps1`,
`publish-console.ps1`, `publish-tool.ps1`, `Install-OnServer.ps1`,
MSIX, `.appinstaller`, MajorUpgrade, WiX, `SiNet.SecretImport`,
Windows Credential Manager, DPAPI vault, `SI-ENG\\sieng`,
`\\SI-WIN-2K19\\AppFolder\\AppNet\\`, WebView2 Runtime,
`UserDataFolder`, centralized authorization, System Status,
`CredentialVaultService`, `SecretKeys`, `SecretProvisioningService`,
`SecretSetupWindow`, `SiNet.secrets`, `SiNet.SecretImport`, DPAPI,
Windows Credential Manager, `%LOCALAPPDATA%\\SiNet\\Autodesk\\refresh_token.json`,
`%APPDATA%\\SiNet\\GoogleTokens`, `FileDataStore`,
`GoogleWebAuthorizationBroker`, `client_secrets.json`,
`GoogleClientSecrets`, `AutodeskClientId`, `AutodeskClientSecret`,
`AccServiceApiKey`, service secrets, user OAuth tokens,
WebView2 session / profile.
