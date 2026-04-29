# SiOffice.AccService — Deployment Guide

## TL;DR

שתי פקודות. זהו.

**1. במחשב פיתוח** (גרסה נבמפת אוטומטית, MSI נבנה ומועתק לשרת):
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService
powershell -ExecutionPolicy Bypass -File .\publish-service.ps1
```

**2. בשרת** (התקנה / עדכון - אותה פקודה בדיוק בכל פעם):
```powershell
msiexec /i "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi" /qn /l*v C:\Temp\AccService-install.log
```

ה-MSI מטפל לבד ב:
- העתקת קבצים ל-`C:\AccService`
- רישום השירות `SiOfficeAccService` (LocalSystem, auto-start)
- עצירה/הפעלה של השירות בכל עדכון
- פתיחת פורט 8443 בחומת האש
- יצירת תעודת SSL self-signed בהפעלה ראשונה (`accservice.pfx` ליד ה-exe)

---

## ניהול גרסאות

הגרסה נשמרת במקום אחד בלבד: `<Version>` בקובץ `SiOffice.AccService.csproj`.

הסקריפט `publish-service.ps1` **מעלה אותה אוטומטית** בכל הרצה (Build component עולה ב-1, למשל 2.0.0 → 2.0.1). אין צורך לערוך את הקובץ ידנית.

אם בכל זאת רוצים להריץ בלי במפ:
```powershell
.\publish-service.ps1 -NoBump
```

לצפייה בגרסה הנוכחית:
```powershell
Select-String -Path .\SiOffice.AccService.csproj -Pattern "<Version>"
```

---

## בדיקה אחרי התקנה (בשרת)

```powershell
Get-Service SiOfficeAccService
Invoke-WebRequest https://localhost:8443/v1/acc/health -SkipCertificateCheck
```

אם משהו נכשל - הלוג המלא ב-`C:\Temp\AccService-install.log`. חיפוש מהיר לסיבת כישלון:
```powershell
Select-String -Path C:\Temp\AccService-install.log -Pattern "value 3","Rollback","1603" -Context 3,3
```

---

## הסרת המוצר

```powershell
msiexec /x "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi" /qn
```

---

## חד-פעמי: ניקוי התקנה ידנית קודמת

אם השירות נרשם פעם בעבר עם `sc.exe` (לפני ש-MSI התחיל לטפל בזה), יש למחוק אותו פעם אחת לפני ההתקנה החדשה כדי שה-MSI יוכל לרשום את ה-ServiceInstall שלו ללא קונפליקט:

```powershell
Stop-Service SiOfficeAccService -ErrorAction SilentlyContinue
sc.exe delete SiOfficeAccService
Remove-Item C:\AccService -Recurse -Force -ErrorAction SilentlyContinue
```

מכאן והלאה, כל התקנה/עדכון נעשים אך ורק דרך ה-MSI.
