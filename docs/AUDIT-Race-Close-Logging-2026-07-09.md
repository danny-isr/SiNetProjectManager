# דוח אימות: Race Condition, סגירה עם רקע, ולוגים

**תאריך:** 2026-07-09  
**היקף:** אימות בלבד (ללא שינויי קוד)  
**מבצע:** Agent audit

---

## סיכום Pass/Fail

| נושא | תוצאה | הערות |
|------|--------|--------|
| Race — שני משתמשים, אותו מייל | **PASS** (קוד + בדיקות אוטומטיות) | בדיקת 2 משתמשים חיים — לא בוצעה (דורשת 2 sessions) |
| סגירה עם תהליכים ברקע | **PASS** (קוד + בדיקות מבנה) | UI חי — לא נבדק ידנית במהלך האימות |
| לוגים: רשת vs מקומי | **PASS** (קוד + דוגמאות log) | ברירת מחדל תואמת; central Error+ בפועל |
| אין נפילות שקטות | **PARTIAL** | ACC/ingestion טוב; `AppErrorReporter` לא מחובר ל-host |

---

## 1. בדיקות אוטומטיות

```
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj \
  --filter "FullyQualifiedName~EmailAccPipelineTests|FullyQualifiedName~ErrorHandlingSafetyNetTests"
```

**תוצאה:** 40/40 Passed (0 Failed, 0 Skipped)

### בדיקות רלוונטיות ל-Race

- `Second_user_cannot_upload_same_email_while_locked_maps_to_locked_status`
- `Locked_by_other_user_does_not_call_upload_coordinator`
- `Expired_lock_can_be_recovered_safely_when_lease_is_stale`
- `Email_acc_upload_acquires_lock_before_upload_is_documented_in_legacy_lease` (TTL=15)

### בדיקות רלוונטיות ל-Close

- `Email_window_close_uses_background_work_prompt`

### בדיקות רלוונטיות ל-Error Handling

- `AppGlobalExceptionHandling_wires_all_three_handlers`
- `AsyncRelayCommand_execute_catches_exception_without_rethrowing`

---

## 2. Race Condition — ממצאים

### מוטמע

1. **DB lease אטומי** — `EmailIngestionService.TryAcquireLeaseAsync` (`ProcessingByLogin`, TTL 15 דק')
2. **UI gates** — `EmailAccIngestGates.ShouldBlockPassiveUpload`, `LockedByOtherUser`
3. **Dedup בתהליך** — `EmailAccIngestQueue._inFlight`
4. **Polling** — `WaitForCompletionAsync` כש-worker שני מקבל `InProgress`

### פערים ידועים (לא נבדקו מחדש — מתועדים בקוד)

- External upload path ללא lease
- TTL ללא renewal (>15 דק')
- ביטול לא משחרר lease מיד

### בדיקה ידנית (2 משתמשים)

**סטטוס:** לא בוצע — דורש 2 מחשבים/משתמשים Windows במקביל.

**Checklist למשתמש:**
- [ ] משתמש A מתחיל upload על מייל X
- [ ] משתמש B בוחר אותו מייל — רואה "בטיפול על ידי {login}", ללא upload חדש
- [ ] אחרי סיום A — B רואה Uploaded / יכול להמשיך

---

## 3. סגירה עם תהליכים ברקע — ממצאים

### מוטמע

| רכיב | קובץ |
|------|------|
| מעקב uploads | `AccBackgroundWorkMonitor.cs` |
| דיאלוג 3 כפתורים | `BackgroundUploadsDialog.xaml` |
| סגירת אפליקציה | `MainWindow.xaml.cs` → `OnClosing` |
| סגירת Email Workbench | `EmailWindowView.xaml.cs` → `TryBlockCloseForBackgroundWork` |

| כפתור | התנהגות |
|--------|----------|
| סגור כשיסתיים | ממתין; סוגר כש-`TotalActiveCount==0` |
| סגור עכשיו | סוגר מיד (ללא ביטול מפורש של CTS) |
| ביטול | `e.Cancel=true` — נשאר פתוח |

### לא מכוסה

- Minimize / Hide — אין handler; דיאלוג רק ב-Closing
- `ProjectWorkViewModel.ActiveUploadCount` — לא נספר ב-monitor

### בדיקה UI חיה

**סטטוס:** לא בוצע — דורש הרצת אפליקציה + upload פעיל.

**Checklist למשתמש:**
- [ ] MainWindow (X) בזמן upload → דיאלוג 3 כפתורים
- [ ] "ביטול" משאיר פתוח
- [ ] "סגור כשיסתיים" סוגר אוטומטית
- [ ] EmailWindow בלבד — אותו דיאלוג

---

## 4. לוגים — ממצאים

### ארכיטקטורה (מאומת בקוד)

| Sink | נתיב | ברירת מחדל |
|------|------|------------|
| מקומי | `%LocalAppData%\SiNetProjectManager\Logs\` | `LoggingEnabled=false` → Error+ |
| רשת | `\\si-win-2k19\AutoCAD Data\log\Client\<Machine>\<User>\` | `Logging.Client.CentralLevel=Error` (seed SQL) |

**מקורות:** `CentralLogging.cs`, `UserAppSettingsDefaults.LoggingEnabled=false`, `SeedLoggingSettings.sql`

### דוגמאות log (מכונה DANNY / dannyisrael)

**Central** — `Client-20260708.log` (רק `[EROR]`):

```
[2026-07-08 11:30:37.182] ... [EROR] [EmailIngest] Unexpected error. MessageUniqueId=...
[2026-07-08 12:14:39.993] ... [EROR] [EmailIngest] All 3 upload attempts failed for 'manifest.json'
[2026-07-08 13:16:47.300] ... [EROR] [EmailIngest] Failed to upload attachment: ...
```

**Local** — `SiNet_2026-03-20.log` (session עם LoggingEnabled=true — INFO/DEBUG נראים):

```
[2026-03-20 00:04:31.512] [T001] [INFO] Logger initialized. LogDirectory=...
[2026-03-20 00:04:47.986] [T001] [DEBUG] [Init] Starting auto-initialization...
```

> הערה: לוג מקומי ממרץ מראה verbose — כנראה `LoggingEnabled=true` באותה session. ברירת המחדל בקוד היא `false`.

### נפילות שקטות — פערים

| פער | חומרה |
|-----|--------|
| `AppErrorReporter.ExceptionReported` לא מחובר ב-`App.xaml.cs` | בינונית |
| ~33 `catch { }` ריקים (רוב cleanup) | נמוכה |
| Legacy MVVM dispatcher catches ללא log | נמוכה–בינונית |

**חיובי:** שגיאות ACC/EmailIngest מגיעות ל-central (ראו דוגמאות למעלה).

---

## 5. המלצות לשלב הבא (מחוץ להיקף)

1. חיבור `AppErrorReporter` → `AppLogger.Error` ב-host
2. בדיקה ידנית: 2 משתמשים + דיאלוג סגירה UI
3. (אופציונלי) lease ל-external upload, ביטול מפורש ב-"סגור עכשיו"
