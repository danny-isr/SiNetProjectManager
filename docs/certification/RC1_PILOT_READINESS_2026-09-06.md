# RC1 — Controlled Pilot Release Candidate Gate

> **Date:** 2026-09-06  
> **Updated:** 2026-09-06 (blocker clearance)  
> **Machine:** `danny` (DEV workstation)  
> **Environment:** `development` branch, SQL `danny\SQLEXPRESS` / `SiData`, Gmail `shirly@si-eng.co.il`, AccService Local (in-process PilotSmoke) / `https://localhost:8443` when host-started  
> **Operator SIUser:** Id=`12` (שירלי / `AzureAD\dannyisrael`)

## Candidate freeze

| Item | Value |
| --- | --- |
| **Candidate product SiNet SHA** | `eeda3c17c356421c95311a5b77754ccab02e204f` |
| **Harness / RC1-doc SHA (after clearance)** | `612c4e1ed110283e950e586ec6b54ec993d2b703` (+ follow-up docs pin commits on same branch) |
| `origin/development` at freeze | **identical** to candidate (`eeda3c17`) |
| SiNetSQL | `a34418f294fa6cd131daa538f098af5a7849d3d7` |
| SiOffice.AutodeskConnector | `e847dade01c70fc10043bfa299d23c7077f80c33` |
| SiOffice.GoogleConnector | `c066c3205e1fe6f0a7b997084ff53d35ed33e91e` |
| Sibling verification | **PASS** (`build/fetch-siblings.ps1` + local HEAD match) |
| Product version (csproj / window title) | `1.0.34` |

Product candidate remains `eeda3c17`. Clearance required **test-harness-only** fixes under `src/SiNet.App.Wpf.Tests/Live/` (no production authorization / Pilot policy weaken).

### Worktree hygiene

| Class | Paths / notes |
| --- | --- |
| Unrelated local work | `stash` commit `0f1bffd8cf4ce6021b5dbe2f4fdf04a888ff6f95` — message `RC1-temp-unrelated-dirty` (index may shift; use commit hash). **Do not pop until after RC1 gate.** |
| Also present | `stash` `wip-gmail-label-cutover-aside` — untouched |
| Local test evidence | Untracked `tmp-e2e/` + `%LOCALAPPDATA%\SiNet\pilot-smoke\` — not shipped |

---

## BUILD GATE (prior — still green)

| Config | Result | Warnings | Errors |
| --- | --- | --- | --- |
| Debug `SiNet.sln` | **PASS** | 261 | 0 |
| Release `SiNet.sln` | **PASS** | 261 | 0 |

## TEST GATE (prior — still green)

| Project | Failed | Passed | Skipped | Total |
| --- | --- | --- | --- | --- |
| `SiNet.App.Wpf.Tests` Release | 0 | **3763** | 19 | 3782 |
| `SiNet.Infrastructure.Google.Tests` | 0 | **93** | 0 | 93 |
| `SiNet.LegacyBridge.Tests` | 0 | **20** | 0 | 20 |
| `MasterPlan.SyncEngine.Tests` | 0 | **72** | 0 | 72 |

Focused Pilot policy unit filter `FullyQualifiedName~PilotStart`: **12 Passed / 0 Failed**.

## SECRET SCAN

`powershell -File .\build\secret-scan.ps1` → **PASS**.

---

## STARTUP / IDENTITY (prior)

| Check | Result |
| --- | --- |
| AccService / App.Wpf Release start | **PASS** (earlier this day) |
| Windows user → SIUser 12 Authorized | **PASS** |
| Gmail silent restore `shirly@si-eng.co.il` | **PASS** |

---

## A — PilotSmoke identity + S1–S8a (clearance)

### Root cause

After `IdentityOperationGuard` required `WorkflowMutate` → `AuthenticatedUserSession`, PilotSmoke only fixed DB `LoginName` and never bound the same `IWindowsCurrentUserAuthenticator` session as production. Live fail was `IdentityOperationDeniedException` **before** PilotStartGate.

### Harness fix (no production guard weaken)

- `PilotSmokeSeed.EnsureAuthorizedOperatorSessionAsync` — authenticate + assert Authorized + UserId match  
- `P0PilotLiveSmokeTests` / `P0PilotGmailAccLiveSmokeTests` — call after login  
- `finally`: snapshot/restore all `Pilot.*`; if restored `Enabled=true`, force `Enabled=false` so DEV allowlists are not left armed  

### Live SQL rerun

| Item | Value |
| --- | --- |
| Evidence | `%LOCALAPPDATA%\SiNet\pilot-smoke\p0-pilot-smoke-20260906-134809.md` |
| Result | **PASS** (dotnet test exit 0) |

| Step | Result |
| --- | --- |
| S1 Fail-closed | **Pass** |
| S2 Narrow allowlist | **Pass** |
| S3 Allowed Proposal start | **Pass** (instance 86) |
| S4 Denied user | **Pass** |
| S5 Denied workflow | **Pass** |
| S6 Corridor | **Pass** |
| S7 Blocked continuation before mutation | **Pass** (S7a/S7b) |
| S8a Kill-switch | **Pass** |

### Pilot.* after restore (fresh SQL read)

| Key | Value |
| --- | --- |
| `Pilot.Enabled` | **false** |
| `Pilot.AllowedUserIds` | `12` |
| `Pilot.AllowedWorkflowCodes` | `Opinion,Proposal,PlanningWorkflow,Review,Outsourcing` (inert while Enabled=false) |

---

## B — Gmail → ACC → SQL SoT (clearance, current candidate + harness)

Disposable message `P0-SMOKE-RC1-20260906-140937` (self-INBOX insert with `rc1-smoke.pdf`; no external workflow send).  
Evidence: `%LOCALAPPDATA%\SiNet\pilot-smoke\p0-pilot-smoke-20260906-140943.md`

| SoT | Proof |
| --- | --- |
| Gmail source | messageId `1a07668e0a88603e`, RFC822 `<p0-smoke-rc1-c768dde289aa4534a037109fd340e707@si-eng.co.il>` |
| Filing action | G2 Gmail label write under `פרויקטים_משרד` → SQL project **3222**; G3 unfile restored mailbox |
| Physical ACC | A5 ingest **Succeeded** `1/1` into disposable `SI-SMOKE-INBOX` (`c515b53e-…`); A7 MoveToProject **1/1** into `SI-SI` (`62311c21-…`) |
| SQL metadata | `EmailInboxMessage` id **24**; attachment id **25** `rc1-smoke.pdf` AccItemId `urn:adsk.wipprod:dm.lineage:TiiVdg5BTkmZNc30OT6e5g` version=1 |
| Readback | Gmail label round-trip + SQL AccItemId/folder ids + ACC guard allowlist only disposable targets |

### Additional harness fixes required for this corridor

1. **AccService Admin token** in `PilotSmokeHost` when ACC layer on — same contract as `SiOffice.AccService` (`AutodeskTokenStoreOptions.AccServiceAdmin`). Does **not** weaken `IdentityOperationGuard`.  
2. **Subject locate** in `PilotSmokeGmailMessagePicker` — Inbox scope for explicit tokens (AllMail excludes `-in:sent`, which hid self-inserted disposables); **refuse** fall-through to AUTO when token set (prevents production mailbox targeting).

`InboxProjectName` / OfficeInbox resource restored after run. `Pilot.Enabled` remains **false**.

---

## C — Packaging reclassification

| Check | Classification |
| --- | --- |
| Dry pack on this DEV host | **NOT RUN ON DEV — RELEASE-STATION PREREQUISITE** |
| Reason | DEV lacks Windows SDK (`MakeAppx` / `SignTool` path). No SDK/cert install authorized in this task. |
| PROD release station | Documented prerequisites in `docs/RELEASE_PROCESS.md` §4: VS/MSBuild, .NET SDK per `global.json`, Windows SDK (`MakeAppx`/`SignTool`), WiX, signing cert **`CN=SI Office`**, UNC write to `\\SI-WIN-2K19\AppFolder\AppNet\`, sibling pins. Recent ship `origin/release` `a874409` = `SiNet.App.Wpf` **1.0.34** confirms that station previously packaged/signed/published. |
| Before publish | **Require** dry `publish-all.ps1 -SkipDeploy -NoBump` on **PROD** release workstation (not this DEV machine). |

RC1 is **not** blocked on missing PROD packaging infrastructure; DEV dry-pack absence is expected and reclassified.

---

## OPS / LIMITATIONS (unchanged)

- DB backup drill: **MANUAL REQUIRED BEFORE PILOT**  
- MasterPlan key rotation: before wide rollout  
- Central Llog UNC marker: **PILOT LIMITATION**  
- Email off-Dispatcher `UnobservedTaskException`: **PILOT LIMITATION**  
- Do **not** enable production Pilot settings yet  

---

## BLOCKERS

*None remaining for RC1 pilot-promotion decision.*

Cleared:

1. ~~PilotSmoke identity session~~ → S1–S8a **PASS**  
2. ~~ACC filing SoT on candidate~~ → Gmail→ACC→SQL **PASS** (evidence `…140943.md`)  
3. ~~Packaging blocked on DEV SDK~~ → reclassified **RELEASE-STATION PREREQUISITE**

---

## Proposed initial pilot allowlist (recommendation only — do **not** set production yet)

**UserIds:** `12`  

**Workflow codes (first day):** `Proposal`, `Opinion`, `Review`  

Keep `PlanningWorkflow` / `Outsourcing` out of first-day allowlist.  
Runtime: leave `Pilot.Enabled=false` until ops explicitly enables the narrow list on the **pilot** database.

## Proposed development → release promotion (do **not** execute in this gate)

| Branch | SHA |
| --- | --- |
| `origin/release` | `a874409fd9d41b6590d86c574a43ff94f1f3fd41` |
| Candidate product `development` | `eeda3c17c356421c95311a5b77754ccab02e204f` |
| Ahead/behind (`release...development`) | **0 behind / 92 ahead** (at freeze) |

No development→release merge. No publish. No installation. No external workflow send.

---

RC1 VERDICT: READY FOR PILOT PROMOTION

