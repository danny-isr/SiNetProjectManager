# ProjectWork («בעבודה 2») — מעקב בדיקה ידנית

> Session resume: 2026-07-19  
> Environment: V2 **New system** (NewShell)  
> Legend: `OK` | `FAIL` | `SKIP` | `AUTO` (guardrail/unit) | `PENDING_LIVE` (needs human UI)

## Automated guardrails (this session)

| Check | Result | Evidence |
| --- | --- | --- |
| NewShell menu «בעבודה 2» after «מיילים» | AUTO OK | `NewShellProjectWorkMenuTests` |
| Menu hidden when feature denied | AUTO OK | same |
| Host returns false if shell not attached | AUTO OK | same |
| Host reuses cached view across NavigateTo | AUTO OK | same |
| Hub `UnregisterProvider` only clears self | AUTO OK | same |
| Tree open ACC/Drive/local + DnD/conflict/delete wired | AUTO OK | `ProjectWorkManualQaBoundaryTests` |
| Inspection adapters on native hubs | AUTO OK | same |
| Launcher prefers `IProjectWorkSurfaceHost` | AUTO OK | same |
| V2 composite host (MainWindow + NewShell) | AUTO OK | same |
| Existing ProjectWork unit suite | AUTO OK | filter `FullyQualifiedName~ProjectWork` + new guardrails: **88 passed** (2026-07-19) |

## Wave 1 — Smoke shell

| # | Item | Status | Notes |
| --- | --- | --- | --- |
| 1 | תפריט → בעבודה 2 בתוכן הראשי | AUTO OK / PENDING_LIVE | Menu + host wired; confirm visually after restart |
| 2 | בחירת פרויקט → עץ נטען בלי קיפאון | PENDING_LIVE | `OpenBrowseModeAsync` + `LoadTreeAsync` present |
| 3 | מיילים ↔ בעבודה 2 שומר cache | AUTO OK / PENDING_LIVE | Host create-once verified in test |
| 4 | סטטוס סריקה בתחתית | AUTO OK / PENDING_LIVE | `Tree.ScanStatus` bound in XAML |

## Wave 2 — קריאה ופתיחה

| # | Item | Status | Notes |
| --- | --- | --- | --- |
| 5 | תיקיות DB + unassigned לפי naming | PENDING_LIVE | Parser + tree integrate scanned files |
| 6 | פתיחת קובץ מקומי | PENDING_LIVE | `OpenAsync` local path |
| 7 | תגי FileServer / ACC / Drive | PENDING_LIVE | Node badges in XAML/VM |
| 8 | ACC WebView2 tab | PENDING_LIVE | `IAccViewerHost` + AccViewerHost pane |
| 9 | Drive list/open + consent | PENDING_LIVE | `GoogleDriveFileStore`; re-consent if needed |

## Wave 3 — כתיבה / DnD

| # | Item | Status | Notes |
| --- | --- | --- | --- |
| 10 | Drag-in ל-file / alternative / version | PENDING_LIVE | `HandleFileDropAsync` |
| 11 | מניעת קונפליקט סיומת | PENDING_LIVE | `ProjectFileExtensionConflict` |
| 12 | מחיקת גרסה מקומית + רענון | PENDING_LIVE | `ConfirmAndDeleteAsync` |
| 13 | Open-With + sidecar | PENDING_LIVE | `SetOpenPreference*` |

## Wave 4 — אינטגרציה

| # | Item | Status | Notes |
| --- | --- | --- | --- |
| 14 | Inspection picker / linked file via hubs | AUTO OK / PENDING_LIVE | V2 adapters use hubs |
| 15 | פתיחה ממשימה + השלמה | AUTO OK / PENDING_LIVE | Launcher + task strip on VM |

## Wave 5 — לפי מדיניות

| # | Item | Status | Notes |
| --- | --- | --- | --- |
| 16 | ACC upload/delete | SKIP until policy open | `IAccWritePolicy` default closed |
| 17 | Drive upload/rename/delete | PENDING_LIVE | Native store supports writes |
| 18 | השוואה ללגסי «בעבודה» | SKIP unless legacy shell | NewShell is primary |

## Live session log

| When | Item # | Result | Observation |
| --- | --- | --- | --- |
| 2026-07-19 | guardrails | AUTO OK | Tests + boundary checks added for resume |

## How to continue live

1. Restart V2 in New system mode.
2. Run Wave 1 items 1–4 visually; update Status column to `OK`/`FAIL`.
3. Report failures here (or in chat) for debug fixes before Wave 2+.
