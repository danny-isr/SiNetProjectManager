# מדריך פריסה — SiOffice.AccService

מסמך זה מסביר איך לבנות ולפרוס גרסה חדשה של ה-Windows Service `SiOfficeAccService` לשרת `SI-WIN-2K19`.

> ⚠️ **חובה להעלות את מספר הגרסה (Version) לפני כל פריסה.**
> בלי זה, ה-MSI יתקין את קבצי ה-JSON וה-PDB אבל **לא יחליף את ה-DLL הראשי** (זו התנהגות סטנדרטית של Windows Installer — file versioning rules).

---

## 1. איפה הגרסה מוגדרת? (Source of Truth)

יש **קובץ אחד בלבד** שקובע את הגרסה — `SiOffice.AccService.csproj`:

📄 **`D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService\SiOffice.AccService.csproj`**

```xml
<PropertyGroup>
  ...
  <!--
    Version is the single source of truth for AssemblyVersion / FileVersion
    AND for the MSI ProductVersion (read by publish-service.ps1 and passed
    to SiOffice.AccService.Installer.wixproj). Bump on every release.
  -->
  <Version>1.0.0</Version>   <!-- ← זה הקובץ והשורה שצריך לשנות -->
</PropertyGroup>
```

מספר הגרסה הזה משמש לשלושה דברים בו זמנית:
1. **AssemblyVersion / FileVersion** של ה-DLL וה-EXE.
2. **ProductVersion** של ה-MSI (נקרא אוטומטית ע"י `publish-service.ps1`).
3. ה-`MajorUpgrade` של WiX כדי לזהות שגרסה חדשה יותר זמינה ולבצע upgrade אוטומטי.

---

## 2. איך מעלים את הגרסה?

פתח את הקובץ:
```
D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService\SiOffice.AccService.csproj
```

מצא את השורה:
```xml
<Version>1.0.0</Version>
```

ושנה אותה — מספיק להעלות את החלק האחרון:

| לפני | אחרי | מתי |
|---|---|---|
| `1.0.0` | `1.0.1` | תיקון/שינוי קטן |
| `1.0.1` | `1.0.2` | תיקון/שינוי קטן נוסף |
| `1.0.9` | `1.1.0` | פיצ'ר חדש |
| `1.9.x` | `2.0.0` | שינוי גדול / Breaking change |

📌 **חוק:** **כל פריסה לשרת = עליית גרסה.** גם אם זה רק שינוי של שורה אחת. אין יוצא מן הכלל.

לאחר השינוי — שמור את הקובץ.

---

## 3. איפה רואים את הגרסה הנוכחית?

### 🔹 בקוד המקור (לפני build)
```
D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService\SiOffice.AccService.csproj
```
חפש את התג `<Version>...</Version>`.

### 🔹 ב-DLL שכבר מותקן בשרת
```powershell
(Get-Item C:\AccService\SiOffice.AccService.dll).VersionInfo |
    Select-Object FileVersion, ProductVersion
```

### 🔹 ב-MSI שנבנה (במחשב הפיתוח)
```powershell
$msi = "D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService.Installer\bin\Release\SiOfficeAccService.msi"
$wi = New-Object -ComObject WindowsInstaller.Installer
$db = $wi.GetType().InvokeMember("OpenDatabase","InvokeMethod",$null,$wi,@($msi,0))
$view = $db.GetType().InvokeMember("OpenView","InvokeMethod",$null,$db,
    @("SELECT `Value` FROM Property WHERE Property='ProductVersion'"))
$view.GetType().InvokeMember("Execute","InvokeMethod",$null,$view,$null)
$rec = $view.GetType().InvokeMember("Fetch","InvokeMethod",$null,$view,$null)
$rec.GetType().InvokeMember("StringData","GetProperty",$null,$rec,1)
```

### 🔹 ב-Programs and Features (בשרת, לאחר התקנה)
```powershell
Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\* |
    Where-Object { $_.DisplayName -eq "SiOffice ACC Service" } |
    Select-Object DisplayName, DisplayVersion
```

---

## 4. שתי הפקודות של תהליך הפריסה

לאחר שהעלית את הגרסה ב-csproj, יש שתי פקודות בלבד:

### 📦 פקודה #1 — בניית ה-Deployment (במחשב הפיתוח שלך)

הרץ ב-**PowerShell** (לא חייב Admin):

```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService
.\publish-service.ps1
```

הסקריפט יעשה הכל בסדר הזה:
1. בונה את הפרויקט ב-Release / win-x64 (כולל COM refs).
2. מבצע `dotnet publish` ל-`D:\repos2026\SiNetProjectManager_GitHub\artifacts\AccService_Publish\`.
3. קורא את `<Version>` מ-csproj ובונה את ה-MSI עם ה-ProductVersion הזה.
4. מעתיק את ה-MSI ל-share הרשת:
   ```
   \\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi
   ```

**אם אין VPN / לא נגישה הרשת**, הוסף `-SkipDeploy`:
```powershell
.\publish-service.ps1 -SkipDeploy
```
(במקרה כזה ה-MSI נשאר ב-`SiOffice.AccService.Installer\bin\Release\` ותעתיק ידנית).

---

### 🚀 פקודה #2 — התקנה בשרת

התחבר ל-`SI-WIN-2K19` (RDP), פתח **PowerShell as Administrator**, והרץ:

```powershell
msiexec /i "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi" /qn /l*v C:\Temp\AccService-upgrade.log
```

מה ה-flags עושים:
- `/i` — install.
- `/qn` — מצב שקט לחלוטין (ללא UI).
- `/l*v <נתיב>` — לוג מלא (חשוב ל-debug אם משהו משתבש).

ה-MSI יבצע אוטומטית:
1. עוצר את השירות `SiOfficeAccService`.
2. **מסיר את הגרסה הישנה** (כי `MajorUpgrade` רואה שגרסה חדשה יותר נכנסה).
3. מעתיק את כל הקבצים החדשים ל-`C:\AccService\` (כולל ה-DLL!).
4. מפעיל מחדש את השירות.

---

## 5. בדיקות לאחר ההתקנה

הרץ בשרת לוודא שהכל תקין:

```powershell
# 1. השירות רץ
Get-Service SiOfficeAccService

# 2. ה-DLL החדש באמת הוחלף — בדוק LastWriteTime ו-FileVersion
Get-Item C:\AccService\SiOffice.AccService.dll |
    Select-Object Name, Length, LastWriteTime, @{N='Version';E={$_.VersionInfo.FileVersion}}

# 3. Smoke test ל-API
Invoke-RestMethod https://localhost:8443/v1/acc/health -SkipCertificateCheck

# 4. לוגי השירות (היום)
Get-ChildItem "$env:ProgramData\SiOffice\AccService\logs" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 |
    Get-Content -Tail 30
```

ה-`LastWriteTime` של ה-DLL וה-`Version` חייבים להיות הגרסה החדשה שהעלית. אם הם עדיין מציגים את הגרסה הישנה — סימן ששכחת להעלות `<Version>` ב-csproj.

---

## 6. סיכום מהיר — Cheat Sheet

```
┌─────────────────────────────────────────────────────────────────┐
│  שלב 1: העלאת גרסה (חובה!)                                       │
│  ערוך:  SiOffice.AccService\SiOffice.AccService.csproj           │
│  שורה:  <Version>1.0.X</Version>   ← הגדל ב-1                    │
├─────────────────────────────────────────────────────────────────┤
│  שלב 2: בניית ה-Deployment (במחשב פיתוח)                         │
│  cd D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService  │
│  .\publish-service.ps1                                           │
├─────────────────────────────────────────────────────────────────┤
│  שלב 3: התקנה (בשרת SI-WIN-2K19, PowerShell as Admin)            │
│  msiexec /i "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi" /qn /l*v C:\Temp\AccService-upgrade.log │
└─────────────────────────────────────────────────────────────────┘
```

---

## 7. נספח — שאלות נפוצות

### ❓ שכחתי להעלות גרסה. מה לעשות?
שתי אפשרויות:
1. **המומלצת** — חזור לשלב 1, העלה את הגרסה והרץ שוב את שני השלבים.
2. **כפיית overwrite חד-פעמית** (לא מומלץ באופן קבוע):
```powershell
msiexec /i "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi" REINSTALL=ALL REINSTALLMODE=amus /qn /l*v C:\Temp\AccService-force.log
```
ה-`REINSTALLMODE=amus` עוקף את file-versioning rules ומאלץ החלפה של כל הקבצים גם באותה גרסה.

### ❓ ההתקנה הראשונה מעולם לא בוצעה
ה-MSI הוא **updater** בלבד — הוא דורש שהשירות יהיה כבר רשום ב-Windows.
לפני ההתקנה הראשונה אי-פעם, הרץ בשרת (PowerShell as Admin):
```powershell
sc.exe create SiOfficeAccService binPath= "C:\AccService\SiOffice.AccService.exe" start= auto DisplayName= "SiOffice ACC Service"
sc.exe failure SiOfficeAccService reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc.exe start SiOfficeAccService
```
(זה צריך לקרות פעם אחת בלבד אי-פעם.)

### ❓ איך אני יודע אם ההתקנה הצליחה?
בדוק את ה-`$LASTEXITCODE` מיד לאחר `msiexec`:
- `0` — הצליח ✅
- `3010` — הצליח, צריך restart ⚠️
- `1603` — נכשל ❌ (בדוק את הלוג ב-`C:\Temp\AccService-upgrade.log`)
