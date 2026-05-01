# MasterPlan.SyncEngine — Deployment Guide

## TL;DR

הקונסולה הזו רצה כ-Scheduled Task על השרת מתוך כונן רשת משותף. אין צורך במתקין; פשוט מסנכרנים את ה-EXE החדש לכונן הרשת ב-`robocopy /MIR`, וה-Task Scheduler ירוץ אוטומטית עם הגרסה החדשה בהפעלה הבאה.

**במחשב פיתוח** (גרסה מתבמפת אוטומטית, EXE מועתק לשרת):
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\MasterPlan.SyncEngine
powershell -ExecutionPolicy Bypass -File .\publish-console.ps1
```

זהו. ה-Task Scheduler ימשיך להריץ אוטומטית בהפעלה הבאה - הוא מצביע על אותו נתיב UNC, רק התוכן התעדכן.

---

## למה אין מתקין?

- אין שירות לרשום
- אין מפתחות registry
- אין הרשאות מיוחדות
- אין shortcuts או start menu
- ה-Scheduled Task כבר קיים ומצביע על נתיב קבוע ב-UNC

**מתקין כאן זה over-engineering**. סנכרון קבצים זה הפתרון הנכון.

---

## ניהול גרסאות

הגרסה נשמרת ב-`<Version>` בקובץ `MasterPlan.SyncEngine.csproj` (זרע ראשוני: `1.0.0` נוצר אוטומטית בהרצה הראשונה אם לא קיים).

הסקריפט מעלה את הגרסה אוטומטית בכל הרצה (Build component עולה ב-1). להריץ בלי במפ:
```powershell
.\publish-console.ps1 -NoBump
```

---

## פרמטרים

| פרמטר | ברירת-מחדל | מתי לשנות |
|---|---|---|
| `-DeployDir` | `\\SI-WIN-2K19\AppFolder\AppNet\MasterPlanSync` | אם ה-Task Scheduler מצביע על UNC אחר |
| `-OutputDir` | `..\artifacts\MasterPlanSync_Publish` | בדרך כלל לא נוגעים |
| `-Runtime` | `win-x64` | אם השרת ARM (לא רלוונטי) |
| `-SkipDeploy` | - | בנייה בלי העלאה לרשת (לבדיקה לוקלית) |
| `-NoBump` | - | רוצים לפרסם את אותה הגרסה שוב |

---

## מה הסקריפט עושה

1. **מעלה גרסה** ב-`<Version>` של ה-csproj (אלא אם `-NoBump`).
2. **`dotnet publish`** עם `--self-contained true` + `PublishSingleFile=true`:
   - תוצר: EXE יחיד, **לא דורש .NET runtime** מותקן בשרת.
   - כולל native libs (`IncludeNativeLibrariesForSelfExtract=true`).
3. **`robocopy /MIR`** מסנכרן את כל התיקייה ל-UNC (כולל מחיקת קבצים שהוסרו).

---

## בדיקה אחרי deploy

על השרת (או דרך RDP):
```powershell
Get-Item "\\SI-WIN-2K19\AppFolder\AppNet\MasterPlanSync\MasterPlan.SyncEngine.exe" |
    Format-Table Name, Length, LastWriteTime
```

לבדיקת ההרצה הבאה של ה-Scheduled Task:
```powershell
Get-ScheduledTask -TaskName "MasterPlanSync" | Get-ScheduledTaskInfo
```

---

## עדכון בטוח כשה-Task פעיל

`robocopy /MIR` ייכשל אם ה-EXE רץ באותו רגע (file locked). אם זה תרחיש שכיח:

1. הריצו את ה-publish **מחוץ לחלון ההרצה** של ה-Task.
2. או: עצרו זמנית את ה-Task לפני ה-publish:
   ```powershell
   Disable-ScheduledTask -TaskName "MasterPlanSync"
   .\publish-console.ps1
   Enable-ScheduledTask -TaskName "MasterPlanSync"
   ```
