# SiNetProjectManager - מדריך התקנה ופריסה (Secrets)

> **המסמך הזה הוא מקור האמת לסדר התקנת סודות.** פריסת דסקטופ: ערוץ 3 = **`SiNet.App.Wpf`** (לא V2).
> Ledger: [`docs/DOCUMENTATION_RECONCILIATION_2026-08-07.md`](docs/DOCUMENTATION_RECONCILIATION_2026-08-07.md).
> **Needs Review:** האם `SiNet.secrets` על ה-share עדיין תחת `SiNetProjectManagerV2\` או הועבר.

> **המסמך הזה הוא מקור האמת היחיד.** כל פעם שצריך לפרוס משהו או להחליף מפתחות - תפתח את זה ותעשה לפי הסדר.

---

## TL;DR - מה עושים ב-99% מהמקרים

**1. במחשב הפיתוח (PowerShell):**
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1
```

**2. בשרת SI-WIN-2K19 (PowerShell as Administrator):**
```powershell
powershell -ExecutionPolicy Bypass -File "\\SI-WIN-2K19\AppFolder\AppNet\SiOffice.AccService\Install-OnServer.ps1" -SecretsFile "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNet.secrets"
```

> **Needs Review:** נתיב `SiNet.secrets` עדיין מצביע על תיקיית V2 ההיסטורית על ה-share. אם הועבר -- לעדכן כאן ואת `Install-OnServer` usage.

זהו. שני שלבים. הסקריפט בשרת ישאל שתי סיסמאות ויעשה הכל.

---

## 1. ארכיטקטורה - באיזה משתמש כל דבר רץ

| רכיב | רץ ב | משתמש Windows |
|---|---|---|
| **`SiNet.App.Wpf`** (WPF -- production desktop) | מחשבי משתמשים | המשתמש המחובר |
| `SiOffice.AccService` (Service) | `SI-WIN-2K19` | **`SI-ENG\sieng`** |
| `MasterPlan.SyncEngine` (Tasks) | `SI-WIN-2K19` | **`SI-ENG\sieng`** |

> **היסטורי:** `SiNetProjectManagerV2` היה host הדסקטופ הקודם; אינו ערוץ הפאבליש הנוכחי.

**הכלל:** Windows Credential Manager הוא **per-user** (DPAPI). השירות חייב לרוץ כאותו משתמש שהסודות נשמרו אצלו, אחרת הוא לא יראה אותם.

---

## 2. הסודות

מאוחסנים ב-**Windows Credential Manager** עם prefix `SiNet/`. רשימה ב-`SiNetSQL\Services\CredentialProvider.cs` (`SecretKeys.All`):

| מפתח | שימוש |
|---|---|
| `SiNet/GeminiApiKey` | Gemini AI |
| `SiNet/Autodesk/ClientId` + `ClientSecret` | Autodesk APS |
| `SiNet/Google/ClientSecrets` | Google OAuth |
| `SiNet/ActiveDirectory/Username` + `Password` | חיבור AD |
| `SiNet/ConnectionStrings/SiNetDatabase` | DB ראשי |
| `SiNet/ConnectionStrings/ReplicaDatabase` | DB Replica |
| `SiNet/ConnectionStrings/MasterPlanDatabase` | DB MasterPlan |
| `SiNet/AccService/ApiKey` | אימות client ↔ service |
| `SiNet/AccService/CertificatePassword` | סיסמת ה-PFX (self-signed) של AccService |
| `SiNet/MasterPlanApi/ApiKey` | header X-API-Key ל-MasterPlan API |

**12 מפתחות סך הכל.** אם בייצוא רואים פחות → חסר משהו ב-vault שלך.

Thumbprint pins של AccService **אינם** ב-vault — הם ב-System Settings
(`AccService.PinnedCertificateThumbprints`). ראה `docs/ACC_SERVICE_TLS_VIA_VAULT.md`.

---

## 3. זרימה מלאה

### שלב 1: עדכון מפתחות במחשב הפיתוח

1. הרץ את WPF **`SiNet.App.Wpf`** → פתח `SecretSetupWindow` (מנהלה → מפתחות וסודות).
2. מלא/עדכן שדות → **שמור** (כל הנקודות ירוקות).
3. **ייצוא חבילה** → סיסמה חזקה → שמור כ-`SiNet.secrets`.

הקובץ מוצפן AES-256 + PBKDF2.

### שלב 2: פרסום הכל לרשת (במחשב הפיתוח)

```powershell
cd D:\repos2026\SiNetProjectManager_GitHub
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1
```

זה ירוץ 4 ערוצים:
1. **AccService** → MSI → `\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi`
2. **SyncEngine** → robocopy → `\\SI-WIN-2K19\AppFolder\AppNet\MasterPlan.SyncEngine\`
3. **WPF (`SiNet.App.Wpf` MSIX)** → `\\...\SiNet.App.Wpf\` + `.appinstaller`
4. **SecretImport CLI + Install-OnServer.ps1** → `\\SI-WIN-2K19\AppFolder\AppNet\SiOffice.AccService\` + `\\...\SiNet.SecretImport\`

> **Needs Review:** העתקת `SiNet.secrets` ל-`\\...\SiNetProjectManagerV2\SiNet.secrets` עדיין מופיעה למטה כנתיב ops היסטורי -- לאשר אם עדיין בתוקף.

**סוויצ'ים שימושיים (תמיד באותו פורמט):**
```powershell
# רק Service:
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1 -SkipConsole -SkipDesktop -SkipTool

# רק SyncEngine:
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1 -SkipService -SkipDesktop -SkipTool

# רק WPF:
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1 -SkipService -SkipConsole -SkipTool

# רק SecretImport:
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1 -SkipService -SkipConsole -SkipDesktop

# בלי קידום גרסה ובלי העתקה לרשת (build מקומי):
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1 -NoBump -SkipDeploy
```

### שלב 3: העלאת `SiNet.secrets` לשרת

העתק את הקובץ שייצאת בשלב 1 ל:
```
\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNet.secrets
```

### שלב 4: התקנה בשרת - **השיטה היחידה**

1. **RDP לשרת** `SI-WIN-2K19` עם משתמש Administrator.

2. פתח **PowerShell as Administrator** והרץ (אותו פורמט בדיוק):
```powershell
powershell -ExecutionPolicy Bypass -File "\\SI-WIN-2K19\AppFolder\AppNet\SiOffice.AccService\Install-OnServer.ps1" -SecretsFile "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNet.secrets"
```

3. הסקריפט ישאל **שתי סיסמאות** (מוסתרות):
   - **Password** = הסיסמה של חשבון Windows `SI-ENG\sieng`
   - **Package password** = הסיסמה שהזנת בייצוא ב-WPF

4. הסקריפט עושה אוטומטית:
   - מייבא את הסודות ל-vault של `sieng` (גם אם אתה Administrator)
   - מסיר Service ישן אם החשבון לא נכון
   - מתקין `SiOfficeAccService` עם `SERVICEACCOUNT=SI-ENG\sieng`
   - מאמת ומציג: `StartName`, `State`, רשימת מפתחות

**סוויצ'ים שימושיים:**
```powershell
# רק לרענן סודות (השירות כבר מותקן נכון):
powershell -ExecutionPolicy Bypass -File "\\SI-WIN-2K19\AppFolder\AppNet\SiOffice.AccService\Install-OnServer.ps1" -SecretsFile "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNet.secrets" -SkipService

# רק להתקין/להחליף Service בלי לגעת בסודות:
powershell -ExecutionPolicy Bypass -File "\\SI-WIN-2K19\AppFolder\AppNet\SiOffice.AccService\Install-OnServer.ps1" -SkipImport
```

### שלב 5: וידוא ידני

```powershell
Get-CimInstance Win32_Service -Filter "Name='SiOfficeAccService'" | Select Name, StartName, State
# StartName צריך להיות: SI-ENG\sieng
# State צריך להיות:     Running
```

---

## 4. תחזוקה שוטפת

### החלפת מפתח אחד (לדוגמה Gemini)
1. WPF → עדכן Gemini → שמור → ייצוא.
2. העתק `SiNet.secrets` ל-share.
3. בשרת (Administrator):
```powershell
powershell -ExecutionPolicy Bypass -File "\\SI-WIN-2K19\AppFolder\AppNet\SiOffice.AccService\Install-OnServer.ps1" -SecretsFile "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNet.secrets" -SkipService
```

### הוספת מפתח חדש (קוד)
1. `SiNetSQL\Services\CredentialProvider.cs` → הוסף `public const string XyzKey = "SiNet/Xyz";` **וגם הוסף ל-`SecretKeys.All`**.
2. `SecretSetupWindow.xaml` + `.cs` → הוסף שדה + סטטוס + prefill + save + validate.
3. במקום הצריכה: `CredentialVaultService.GetSecret(SecretKeys.XyzKey)`.
4. publish-all → Install-OnServer.

⚠️ אם תוסיף קבוע אבל **תשכח להוסיף ל-`All`** - הוא לא ייוצא ולא ייובא. בדיוק הבאג שהיה עם `AccServiceApiKey`.

---

## 5. בעיות נפוצות

| תסמין | סיבה | פתרון |
|---|---|---|
| Service רץ אבל מחזיר 401 | רץ כ-`LocalSystem` במקום `sieng` | הרץ Install-OnServer שוב (השלם) |
| לוג: `MasterPlan API key not found` | Task רץ כ-`sieng` אבל מפתחות יובאו ל-Admin | Install-OnServer (תמיד מייבא ל-`sieng`) |
| WPF פותח SecretSetup בכל פעם | חסרים מפתחות מקומית | מלא ושמור הכל בירוק |
| ייבוא: "סיסמה שגויה או קובץ פגום" | Package password שגוי | ייצא מחדש |
| בייצוא רואים פחות מ-12 מפתחות | חסר מפתח ב-vault או ב-`SecretKeys.All` | הוסף לקוד / מלא ב-WPF |
| `Install-OnServer.ps1` לא מוצא MSI | publish-all נכשל בערוץ Service | הרץ publish-all מחדש |

---

## 6. קבצים מרכזיים

| מטרה | קובץ |
|---|---|
| רשימת מפתחות | `SiNetSQL\Services\CredentialProvider.cs` |
| גישה ל-Vault | `SiNetSQL\Services\CredentialVaultService.cs` |
| Export/Import .secrets | `SiNetProjectManagerV2\Services\SecretProvisioningService.cs` |
| GUI ניהול סודות | `SiNetProjectManagerV2\WPF Window\SecretSetupWindow.xaml(.cs)` |
| CLI ייבוא לשרת | `SiNet.SecretImport\Program.cs` |
| **התקנה בשרת (זה מה שמריצים!)** | `SiOffice.AccService\Install-OnServer.ps1` |
| Publish של הכל | `publish-all.ps1` |
| MSI definition | `SiOffice.AccService.Installer\Package.wxs` |

---

## 7. אבטחה

- ✅ DPAPI per-user → סודות לא נראים למשתמשים אחרים.
- ✅ `.secrets` מוצפן AES-256 + PBKDF2(100K) → בטוח להעביר.
- ✅ אין סודות ב-Git.
- ✅ **MasterPlan API key** — רק vault (`SiNet/MasterPlanApi/ApiKey`) או env var `MASTERPLAN_API_KEY`. `MasterPlan.SyncEngine/appsettings.json` נשאר tracked עם `ApiKey` ריק; להעתקת מבנה השתמשו ב-`appsettings.template.json`.
- ⚠️ `SiNet.secrets` + הסיסמה = שליטה מלאה. שמור בנפרד.
- ⚠️ אחרי עזיבת איש צוות: ייצא חבילה חדשה עם מפתחות שהוחלפו.
