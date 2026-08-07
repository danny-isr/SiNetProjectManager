# מדריך פריסה — SiNetProjectManager Solution

מדריך מסודר לפריסה של הרכיבים מסביבת העבודה אל שרת הקבצים `\\SI-WIN-2K19`.

> **סביבות ותהליך שחרור (2026-08-02 / עדכון 07.08.2026):** תפקידי מכונות PROD/DEV, שערי שחרור ו-rollback —
> [`docs/ENVIRONMENTS.md`](docs/ENVIRONMENTS.md), [`docs/RELEASE_PROCESS.md`](docs/RELEASE_PROCESS.md).
> ניטור אחרי פאבליש: [`docs/PRODUCTION_MONITORING.md`](docs/PRODUCTION_MONITORING.md).
> ערוץ הדסקטופ הנוכחי הוא **`SiNet.App.Wpf`** (לא V2) — ראה גם [`docs/DESKTOP_CUTOVER.md`](docs/DESKTOP_CUTOVER.md).
> מקור האמת לערוצים: `publish-all.ps1`. Ledger: [`docs/DOCUMENTATION_RECONCILIATION_2026-08-07.md`](docs/DOCUMENTATION_RECONCILIATION_2026-08-07.md).
>
> **ערכת שרת (בלי D:\repos):** אחרי `publish-all.ps1` נוצרת תיקייה עצמאית
> `\\SI-WIN-2K19\AppFolder\AppNet\Server\` עם MSI + SecretImport + `Install-OnServer.ps1`.
> על השרת (כ-Administrator): `Upgrade-AccService.cmd` בתיקיית Server,
> או `powershell -File ...\Install-OnServer.ps1 Upgrade` (בלי `-SkipImport`).

---

## סקירה כללית

הפתרון מורכב מארבעה ערוצי פריסה דרך **`publish-all.ps1`**, ולכל אחד יש סקריפט ויעד משלו:

| # | רכיב | סוג | סקריפט | יעד ב-`\\SI-WIN-2K19\AppFolder\AppNet\` |
|---|---|---|---|---|
| 1 | `SiOffice.AccService` | Windows Service | `SiOffice.AccService\publish-service.ps1` | `SiProjecNet2026-Full\` (MSI) |
| 2 | `MasterPlan.SyncEngine` | Console (Task Scheduler) | `MasterPlan.SyncEngine\publish-console.ps1` | `MasterPlan.SyncEngine\` (`DeployDir` -- לא `MasterPlanSync\`) |
| 3 | **`SiNet.App.Wpf`** | WPF Desktop (production) | `src\SiNet.App.Wpf\publish-desktop.ps1` | `SiNet.App.Wpf\` (MSIX + `.appinstaller`) |
| 4 | `SiNet.SecretImport` | portable EXE | `src\SiNet.SecretImport\publish-tool.ps1` | `SiNet.SecretImport\` |

**היסטורי (לא ערוץ פאבליש):** `SiNetProjectManagerV2` -- נשאר בריפו כ-reference; ראה `SiNetProjectManagerV2\DEPLOYMENT.md` (Superseded).

מעל הכל יש סקריפט-על: **`publish-all.ps1`** שמריץ אותם ברצף.

---

## שימוש יומיומי - להריץ הכל בפעם אחת

זה מה שאתה רוצה לעשות ברוב הזמן. מסביבת העבודה, PowerShell:

```powershell
cd D:\repos2026\SiNetProjectManager_GitHub
git pull
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1
```

זה ירוץ את הערוצים ברצף:
1. **AccService** → MSBuild + WiX → MSI → העלאה ל-`SiProjecNet2026-Full`.
2. **SyncEngine** → `dotnet publish` self-contained single-file → robocopy ל-`MasterPlan.SyncEngine`.
3. **Desktop (`SiNet.App.Wpf`)** → MSBuild + MakeAppx + SignTool → MSIX + `.appinstaller` → robocopy ל-`SiNet.App.Wpf`.
4. **SecretImport** → portable EXE.

כל רכיב מבמפ את ה-`<Version>` שלו ב-csproj עצמאית. אם רכיב נכשל - הסקריפט עוצר ולא ממשיך.

---

## פריסה חלקית - רק רכיב אחד

### רק WPF Desktop (production host):
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\src\SiNet.App.Wpf
powershell -ExecutionPolicy Bypass -File .\publish-desktop.ps1
```

### רק Console SyncEngine:
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\MasterPlan.SyncEngine
powershell -ExecutionPolicy Bypass -File .\publish-console.ps1
```

### רק Windows Service:
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService
powershell -ExecutionPolicy Bypass -File .\publish-service.ps1
```

### דרך publish-all עם דילוג:
```powershell
.\publish-all.ps1 -SkipService -SkipConsole -SkipTool    # רק desktop (App.Wpf)
.\publish-all.ps1 -SkipDesktop                           # service + console (+ tool unless skipped)
```

---

## דגלים שימושיים

| דגל | משמעות | מתי להשתמש |
|---|---|---|
| `-SkipDeploy` | בנייה בלי העלאה לרשת | בדיקה לוקלית בלי לשנות את `\\SI-WIN-2K19` |
| `-NoBump` | להריץ עם אותה גרסה | לעשות retry אחרי שגיאה זמנית בלי לעלות מספר גרסה |
| `-SkipService` | לדלג על AccService | רק ב-`publish-all.ps1` |
| `-SkipConsole` | לדלג על SyncEngine | רק ב-`publish-all.ps1` |
| `-SkipDesktop` | לדלג על WPF (App.Wpf) | רק ב-`publish-all.ps1` |
| `-SkipTool` | לדלג על SecretImport | רק ב-`publish-all.ps1` |

דוגמה: בנייה מלאה לבדיקה לוקלית בלי לדפדף שרתים:
```powershell
.\publish-all.ps1 -SkipDeploy
```

---

## תנאי מקדים על מחשב סביבת העבודה

חד-פעמי, פעם אחת בלבד:

1. **Visual Studio 2026** מותקן (יש לך - 18.5.2).
2. **.NET 10 SDK** מותקן (`dotnet --version` → `10.0.x`).
3. **Windows 10/11 SDK** מותקן (לצורך `MakeAppx.exe` + `SignTool.exe`).
   - בדיקה: `Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' | Select-Object Name`.
   - אמור להחזיר תיקייה כמו `10.0.26100.0`. אם ריק → להתקין דרך VS Installer → Modify → Individual Components → "Windows 11 SDK".
4. **WiX Toolset** מותקן (לצורך ה-service MSI).
5. **תעודת Code-Signing** עם `Subject = CN=SI Office` ב-`Cert:\CurrentUser\My`.
   - יש לך, אומת ב-ריצה אחרונה: `F3C37720E61E69D6D20C786A6AE818E4FEF0C539`.
6. **גישה ל-`\\SI-WIN-2K19\AppFolder\AppNet\`** עם הרשאות כתיבה.

---

## התעודה (חד-פעמי לכל הארגון)

הסקריפט `src\SiNet.App.Wpf\publish-desktop.ps1` בוחר אוטומטית את התעודה הראשונה ב-`Cert:\CurrentUser\My` עם code-signing EKU. אם רוצים תעודה ספציפית:
```powershell
.\publish-desktop.ps1 -CertThumbprint "F3C37720E61E69D6D20C786A6AE818E4FEF0C539"
```

ה-`Subject` של התעודה (`CN=SI Office`) חייב להיות **זהה בדיוק** ל-`Publisher` במניפסט של `SiNet.App.Wpf`.

תחנות הקצה במשרד מקבלות את ה-`.cer` אוטומטית דרך Group Policy שכבר מוגדר ב-Active Directory.

---

## התקנה ראשונית על תחנת קצה

פעם אחת בלבד לכל מחשב משתמש:

### Windows Service (AccService)
```
\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi
```
דאבל-קליק → Install. עדכוני שירות ב-`publish-all.ps1` הבאים יחליפו את השירות הקיים אוטומטית (WiX MajorUpgrade).

### WPF Desktop (SiNet.App.Wpf -- production)
```
\\SI-WIN-2K19\AppFolder\AppNet\SiNet.App.Wpf\SiNet.App.Wpf.appinstaller
```
דאבל-קליק → Install. **מעכשיו עדכון אוטומטי בכל פתיחה של האפליקציה** - אין צורך לחזור על התהליך.

> **היסטורי:** נתיב V2 `...\SiNetProjectManagerV2\SiNetProjectManagerV2.appinstaller` אינו ערוץ הפאבליש הנוכחי.

### Console SyncEngine
לא מותקן על תחנות. רץ על השרת מ-Task Scheduler, שמצביע ישירות על:
```
\\SI-WIN-2K19\AppFolder\AppNet\MasterPlan.SyncEngine\MasterPlan.SyncEngine.exe
```
כל ריצת `publish-console.ps1` מחליפה את הקובץ - הריצה הבאה של ה-Task משתמשת בגרסה החדשה.

---

## מחזור חיים יומיומי

```powershell
# 1. עדכון מקומי לקוד אחרון
cd D:\repos2026\SiNetProjectManager_GitHub
git pull

# 2. פריסה (בנייה + העלאה לרשת) -- רק מתחנת PROD
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1

# 3. commit של מספרי הגרסה החדשים
git add **/*.csproj
git commit -m "release: bump versions"
git push
```

המשתמשים לא עושים כלום:
- WPF (App.Wpf): יקבלו עדכון אוטומטי בפתיחה הבאה.
- Service: כבר מותקן + רץ; עדכון בריצת MSI הבאה (אם צריך).
- SyncEngine: ה-Task הבא ישתמש ב-exe החדש.

---

## פתרון בעיות נפוצות

### "Windows SDK not found"
חסר Windows 11 SDK. ב-VS Installer → Modify → Individual Components → לחפש "Windows 11 SDK" ולסמן.

### "MSB4803: ResolveComReference is not supported"
הסקריפטים כבר עוקפים את זה דרך `vswhere` + `MSBuild.exe` של Visual Studio. אם בכל זאת מופיע - לוודא ש-`vswhere.exe` קיים: `Test-Path "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"`.

### "NETSDK1047: Assets file doesn't have a target for 'net10.0-windows/win-x64'"
ב-csproj חסר `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`. אם זה חוזר - למחוק `obj\` ו-`bin\` של הפרויקט ולנסות שוב.

### "robocopy failed with exit code 8+"
אין הרשאות כתיבה ל-`\\SI-WIN-2K19\AppFolder\AppNet\` או שהשרת לא מגיב. לבדוק `Test-Path \\SI-WIN-2K19\AppFolder\AppNet\`.

### "SignerSign() failed: 0x800B0109" אצל המשתמשים
ה-`.cer` לא הגיע ל-`Trusted Root` של תחנת הקצה. לאלץ רענון GPO על אותו מחשב: `gpupdate /force`.

### "Install failed: 0x80073CF3" (MSIX)
המשתמש מנסה להתקין MSIX עם `Identity/Name` זהה אבל `Publisher` שונה משהותקן קודם. להסיר ידנית את החבילה הישנה (App.Wpf או V2 היסטורי) ואז להתקין מחדש מה-`.appinstaller`.

### שגיאות UTF-8 משונות (`Unexpected token`)
הסקריפטים נשמרים כ-UTF-8 with BOM. אם מישהו שמר אותם בלי BOM - לתקן את קידוד הקובץ ב-editor ולא דרך המרת encoding עיוורת על markdown.

---

## מסמכי משנה

לפרטים מעמיקים על כל ערוץ - יש קובץ `DEPLOYMENT.md` / שחרור בכל פרויקט:
- `SiOffice.AccService\DEPLOYMENT.md` - WiX MSI, MajorUpgrade, ServiceController.
- `MasterPlan.SyncEngine\DEPLOYMENT.md` - הגדרת Task Scheduler, single-file exe.
- `src\SiNet.App.Wpf\` + [`docs/RELEASE_PROCESS.md`](docs/RELEASE_PROCESS.md) - ערוץ הדסקטופ הפעיל.
- `SiNetProjectManagerV2\DEPLOYMENT.md` - **Superseded / Historical** (V2 MSIX; לא ערוץ פאבליש).

---

## TL;DR

```powershell
cd D:\repos2026\SiNetProjectManager_GitHub
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1
```
