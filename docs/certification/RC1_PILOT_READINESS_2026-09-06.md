# RC1 — Controlled Pilot Release Candidate Gate

> **Date:** 2026-09-06  
> **Updated:** 2026-09-06 (ACC SoT finish + push)  
> **Machine:** `danny` (DEV workstation)  
> **Environment:** `development` branch, SQL `danny\SQLEXPRESS` / `SiData`, Gmail `shirly@si-eng.co.il`  
> **Operator SIUser:** Id=`12` (שירלי / `AzureAD\dannyisrael`)

## Candidate freeze

| Item | Value |
| --- | --- |
| **RC1 candidate SHA (after harness + doc)** | `be5241cd9a108d1ea2981d98cb391c0565d4806f` |
| Prior product baseline | `eeda3c17c356421c95311a5b77754ccab02e204f` |
| Harness fix commit | `612c4e1ed110283e950e586ec6b54ec993d2b703` |
| SiNetSQL | `a34418f294fa6cd131daa538f098af5a7849d3d7` |
| SiOffice.AutodeskConnector | `e847dade01c70fc10043bfa299d23c7077f80c33` |
| SiOffice.GoogleConnector | `c066c3205e1fe6f0a7b997084ff53d35ed33e91e` |
| Product version | `1.0.34` |

Harness-only changes under `src/SiNet.App.Wpf.Tests/Live/` + this certification doc. No production authorization / Pilot policy weaken. No Inspection/runtime composition edits.

### Worktree hygiene

| Class | Notes |
| --- | --- |
| Stash | `0f1bffd8cf4ce6021b5dbe2f4fdf04a888ff6f95` — `RC1-temp-unrelated-dirty` (**do not pop**) |
| Local evidence | `%LOCALAPPDATA%\SiNet\pilot-smoke\`, `tmp-e2e\` (untracked) |

---

## AccServiceAdmin identity contract (production seam)

PilotSmoke ACC layer binds the **same** store as `SiOffice.AccService/Program.cs`:

`new TokenProvider(…, AutodeskTokenStoreOptions.AccServiceAdmin)`

Proof (`tmp-e2e/rc1-acc-admin-proof.txt`, Local ACC seam — AccService HTTP not required):

| Check | Result |
| --- | --- |
| Purpose | `AccServiceAdmin` |
| Path | `%LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json` (dedicated; isolated from UserContext) |
| Token available | **true** |
| Connected admin email | `siad@si-eng.co.il` |
| Expected (`AccBootstrapAdminEmail`) | `siad@si-eng.co.il` |
| Identity status | **Healthy** (email match + Admin API probe) |
| Admin API probe | **200** (`AccServiceAdminApiProbe` list projects on hub `b.43b1768f-…`) |
| Project authorization | tip read of SoT lineage on ACC project `SI-SI` / `62311c21-…` **OK** |
| UserContext shortcut | **Not used** |
| IdentityOperationGuard | **Unchanged** |

---

## A6 root cause (prior failure → fix)

**Prior exception (before harness token fix):**  
`ACC Admin mutation blocked: AccService must use the dedicated AccService Autodesk token store.`

| Question | Answer |
| --- | --- |
| Root cause class | **Wrong identity/token purpose** — harness used UserContext vault `AddSiNetAutodeskVaultTokenProvider()` while `AccProjectProvisioningService` (since `cf92e8c`) requires `AutodeskTokenStorePurpose.AccServiceAdmin` + dedicated path |
| Wrong hub/project? | No — disposable Place `SI` → expected ACC name `SI-SI` |
| Stale mapping? | No — failure aborted **before** mapping write |
| Project-number normalization? | No |
| Smoke project unmappable by design? | No — Place `SI` maps to existing DEV ACC project `SI-SI` via production provisioner |
| Production mapping repair? | **Not performed** |

### Successful A6 values (tip SHA SoT, project **3223**)

| Field | Value |
| --- | --- |
| Source ProjectId | `3223` |
| ProjectNumber / NameAndNumber | `(3223)[P0-SMOKE] 0906-1612` |
| Place | `SI` |
| Expected ACC project name | `SI-SI` |
| Actual AccProjectId | `62311c21-b79a-40e6-a992-18c6032314a0` |
| Actual AccProjectName | `SI-SI` |
| Target folder | `urn:adsk.wipprod:fs.folder:co.CoYQIyUNQ9eru3iYs8FETw` |
| Target path | `/(3223)[P0-SMOKE]_0906-1612` |

---

## A — PilotSmoke S1–S8a

| Item | Value |
| --- | --- |
| Evidence | `p0-pilot-smoke-20260906-134809.md` |
| Result | **PASS** |

S1–S8a all Pass. Post-restore: `Pilot.Enabled=false`, users=`12`, codes inert.

---

## B — Gmail → ACC → SQL SoT (tip / new candidate)

Disposable corridor: `SI-SMOKE-INBOX` + Place `SI` / ACC `SI-SI` (supported production filing pattern).  
Message `P0-SMOKE-RC1-20260906-161134` + `rc1-smoke.pdf` (self-INBOX insert; no external workflow send).  
Evidence: `p0-pilot-smoke-20260906-161211.md`

| Step | Result |
| --- | --- |
| A1–A4 | Pass (disposable inbox) |
| A5 ingest | **Succeeded** `1/1` → inbox `c515b53e-…` |
| G2 Gmail filing | **Pass** → SQL project 3223 |
| A6 mapping | **Pass** → `SI-SI` |
| A6b tag | **Pass** |
| A7 MoveToProject | **Pass** `moved=1/1` AllFilesTransferred (not FiledButMoveMetadataFailed / MissingInAcc / MetadataReadFailed / UnknownAccInboxFile) |
| G3 unfile | **Pass** (0 project labels) |
| A3r restore | **Pass** |

### Three sources of truth (final)

| Source | State |
| --- | --- |
| **Gmail** | Subject token message filed then **unfiled**; 0 project labels remain (G3) |
| **ACC** | Physical tip `urn:adsk.wipprod:fs.file:vf.d7viKgoNTqG1QxZgZw1B4w?version=1` on project `62311c21-…` (`SI-SI`) — Autodesk tip readback **OK** |
| **SQL** | `EmailInboxMessage` id **25**; attachment id **26** `rc1-smoke.pdf` AccItemId `urn:adsk.wipprod:dm.lineage:d7viKgoNTqG1QxZgZw1B4w` AccVersionId matches tip; ProjectFileId=174; InboxAccProjectId=`c515b53e-…` |

Cleanup: Gmail unfile + InboxProjectName/OfficeInbox restore via harness; ACC soft-delete leftovers remain per smoke policy (manual Admin Console). Pilot.* left fail-closed.

---

## C — Packaging

| Check | Status |
| --- | --- |
| DEV dry-package | **Unavailable** — Windows SDK / MakeAppx path missing on DEV (not a product defect) |
| UNC `\\SI-WIN-2K19\AppFolder\AppNet\` | **Write probe OK** from this DEV session |
| DEV `CN=SI Office` cert | **Not present** on DEV (expected — lives on release station) |
| DEV .NET SDK | `10.0.302` present |
| PROD release station | Prerequisites documented in `docs/RELEASE_PROCESS.md` §4; prior ship `a874409` = **1.0.34** signed/published from that station |

**Classification:** `READY FOR PROD RELEASE-STATION DRY PACKAGE`  
(not an RC1 blocker on DEV). Require dry `publish-all.ps1 -SkipDeploy -NoBump` on PROD before publish.

---

## Automated gates on new candidate

| Gate | Result |
| --- | --- |
| Release `SiNet.App.Wpf` build | **PASS** |
| `SiNet.App.Wpf.Tests` Release | **PASS** — Failed 0, Passed **3763**, Skipped 19, Total 3782 |
| Google.Tests | **PASS** — 93/93 |
| LegacyBridge.Tests | **PASS** — 20/20 |
| SyncEngine.Tests | **PASS** — 72/72 |
| secret-scan | **PASS** (2553 files) |
| Focused PilotSmoke SQL on tip | **PASS** (S1–S8a; evidence under `%LOCALAPPDATA%\SiNet\pilot-smoke\`) |
| ACC SoT on tip | **PASS** — `p0-pilot-smoke-20260906-161211.md` |

DB/schema: **unchanged** (no migrations).

---

## Proposed initial pilot allowlist (do **not** enable yet)

**UserIds:** `12`  
**Codes:** `Proposal`, `Opinion`, `Review`  
Defer Planning/Outsourcing.

## Branch delta (do **not** merge)

| Branch | SHA |
| --- | --- |
| `origin/release` | `a874409fd9d41b6590d86c574a43ff94f1f3fd41` |
| `development` tip (candidate) | `be5241cd9a108d1ea2981d98cb391c0565d4806f` |

No development→release merge. No publish. No install.

---

RC1 VERDICT: READY FOR PILOT PROMOTION
