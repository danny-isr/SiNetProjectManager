# 🔐 ניהול מפתחות וסודות - SiNetProjectManager

מסמך זה מסביר **איפה** המפתחות נשמרים, **איך** מעבירים אותם בין מחשבים,
ו**איך** מספקים אותם לשרת `SI-WIN-2K19` בלי להתקין שם את WPF.

> **TL;DR:** כל הסודות חיים ב-**Windows Credential Manager** (DPAPI per-user).
> במחשב הפיתוח שלך מנהלים אותם דרך `SecretSetupWindow` ב-WPF; בשרת מייבאים
> אותם פעם אחת באמצעות `SiNet.SecretImport.exe` תחת חשבון השירות.

---

## 1. ארכיטקטורת הסודות

### איפה הסודות חיים?
- **Windows Credential Manager** (Generic Credentials, prefix `SiNet/`).
- מוצפן ב-DPAPI **לפי משתמש Windows** - משתמש A לא רואה סודות של משתמש B.
- אין סוד יחיד שמסונכרן בין מחשבים אוטומטית; כל מחשב/משתמש מוגדר בנפרד.

### רשימת המפתחות (`SecretKeys` ב-`SiNetSQL\Services\CredentialProvider.cs`)
| מפתח | שימוש | רכיב צרכן |
|---|---|---|
| `SiNet/GeminiApiKey` | Gemini AI | WPF |
| `SiNet/Autodesk/ClientId` + `ClientSecret` | Autodesk APS OAuth | WPF + AccService |
| `SiNet/Google/ClientSecrets` | תוכן `credentials.json` של Google OAuth | WPF |
| `SiNet/ActiveDirectory/Username` + `Password` | חיבור AD | WPF |
| `SiNet/ConnectionStrings/SiNetDatabase` | DB ראשי | WPF + AccService |
| `SiNet/ConnectionStrings/ReplicaDatabase` | DB Replica | WPF + SyncEngine |
| `SiNet/ConnectionStrings/MasterPlanDatabase` | DB MasterPlan | WPF + SyncEngine |
| `SiNet/AccService/ApiKey` | אימות לקוח↔שירות (header `X-AccService-Key`) | WPF (שולח) + AccService (מאמת) |
| `SiNet/MasterPlanApi/ApiKey` | header `X-API-Key` ל-MasterPlan Web API | SyncEngine |

### סדר עדיפויות לקריאת מפתח (דוגמה: `MasterPlanApiClient`)
1. **Windows Credential Manager** (vault) ✅ מועדף
2. **Environment Variable** (`MASTERPLAN_API_KEY`) - ל-CI / בדיקות
3. **`appsettings.json`** - fallback dev/legacy בלבד

---

## 2. איפה זה רץ ומי המשתמש

| רכיב | היכן | משתמש Windows |
|---|---|---|
| `SiNetProjectManagerV2` (WPF) | מחשבי משתמשים | המשתמש המחובר |
| `SiOffice.AccService` (Windows Service) | `SI-WIN-2K19` | מותקן עם `SERVICEACCOUNT=DOMAIN\sieng` |
| `MasterPlan.SyncEngine` (Console) | `SI-WIN-2K19` Task Scheduler | **`sieng`** (`MasterPlandaily`, `MasterPlanMonthly`) |

> ⚠️ **חשוב:** Credential Manager הוא per-user. אם ה-Service רץ כ-`LocalSystem`
> אבל הסודות נשמרו תחת `DOMAIN\YourUser` - השירות לא יראה אותם ויחזיר 401.
> בשרת `SI-WIN-2K19` כל הסודות חייבים להיכתב תחת **`sieng`**.

---

## 3. הזרימה המעשית

### 🟢 שלב 1: הגדרת מפתחות במחשב הפיתוח
1. פתח את WPF (`SiNetProjectManagerV2`).
2. ייפתח דיאלוג `SecretSetupWindow` (או הגעה אליו דרך התפריט).
3. מלא את כל השדות → **שמור** (וידוא: כל הנקודות ירוקות 🟢).
4. לחץ **📦 ייצוא חבילה** → בחר סיסמה חזקה → קובץ `SiNet.secrets`.

קובץ ה-`.secrets` מוצפן AES-256-CBC + PBKDF2 (100K iterations). בטוח להעביר.

### 🟢 שלב 2: publish של הכל לרשת
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub
.\publish-all.ps1
```
זה ירוץ 4 ערוצים:
1. `SiOffice.AccService` → MSI
2. `MasterPlan.SyncEngine` → robocopy
3. `SiNetProjectManagerV2` → MSIX + .appinstaller
4. `SiNet.SecretImport` → robocopy ⬅ **הכלי הפורטבילי לשרת**

מתגי דילוג: `-SkipService`, `-SkipConsole`, `-SkipDesktop`, `-SkipTool`,
`-NoBump` (בלי קידום גרסה), `-SkipDeploy` (בלי העתקה לרשת).

### 🟢 שלב 3: ייבוא בשרת (פעם אחת לכל סבב מפתחות)

1. **העבר את `SiNet.secrets`** לשרת (USB / share).

2. **RDP לשרת בחשבון `sieng`** (לא Administrator!).

3. הרץ את הכלי הפורטבילי:
   ```powershell
   $tool = "\\SI-WIN-2K19\AppFolder\AppNet\SiNet.SecretImport\SiNet.SecretImport.exe"

   # ודא שאתה החשבון הנכון
   & $tool whoami
   #   -> Current Windows user : SI-WIN-2K19\sieng   (או DOMAIN\sieng)

   # ייבא
   & $tool import C:\Temp\SiNet.secrets

   # אמת
   & $tool status
   ```

4. **לא נדרש להתקין WPF בשרת.** הכלי self-contained (~82MB) בלי תלויות.

### 🟢 שלב 4: וידוא שזה עובד
- ה-Task `MasterPlandaily` ירוץ בריצה הבאה ויצרוך את `MasterPlanApi/ApiKey`
  מה-vault במקום מ-`appsettings.json`.
- `AccService` יקרא `AccService/ApiKey` מה-vault של `sieng`.
- אחרי כמה ימי הצלחה - אפשר להסיר את `"ApiKey"` מ-`MasterPlan.SyncEngine\appsettings.json`.

---

## 4. תרחישים מיוחדים

### החלפת מפתח MasterPlan API
1. במחשב הפיתוח: WPF → מלא את ה-MasterPlan key החדש → ייצוא.
2. RDP לשרת כ-`sieng` → `SiNet.SecretImport.exe import ...` → ה-vault מתעדכן.
3. ה-Task הבא יזרום עם המפתח החדש - **בלי restart לשירותים**.

### החלפת חשבון השירות (לדוגמה sieng → si-service)
1. RDP בחשבון החדש (`si-service`).
2. הרץ `SiNet.SecretImport.exe import ...` תחתיו.
3. שנה את הגדרות ה-Tasks וה-Service לרוץ תחתיו.
4. אופציונלי: מחק את הסודות הישנים מ-vault של `sieng`.

### הוספת מפתח חדש
1. הוסף קבוע ב-`SiNetSQL\Services\CredentialProvider.cs` (`SecretKeys.XYZ` + הוסף ל-`All`).
2. הוסף שדה ב-`SecretSetupWindow.xaml` + `.cs` (סטטוס + prefill + save + validate).
3. במקום הצריכה: `CredentialVaultService.GetSecret(SecretKeys.XYZ) ?? envFallback ?? configFallback`.
4. צא חבילה חדשה → ייבא בשרת.

---

## 5. בעיות נפוצות

| תסמין | סיבה | פתרון |
|---|---|---|
| `WARN AccService API key is not configured` | ה-Service רץ תחת משתמש אחר מזה שייבא | RDP בחשבון השירות, הרץ `import` שוב |
| `MasterPlan API key not found` בלוג של SyncEngine | Task רץ כ-`sieng` אבל המפתחות יובאו תחת `Administrator` | RDP כ-`sieng`, ייבא שוב |
| WPF פותח את `SecretSetupWindow` בכל הפעלה | חסרים מפתחות חובה ב-vault של המשתמש | מלא ושמור הכל בירוק |
| `סיסמה שגויה או קובץ פגום` בייבוא | סיסמה לא תואמת לזו של הייצוא | ייצא מחדש עם סיסמה ידועה |
| הכלי לא נמצא בשרת | `publish-all.ps1` לא רץ עם הערוץ הרביעי | הרץ `.\SiNet.SecretImport\publish-tool.ps1` |

---

## 6. קבצים קשורים

- `SiNetSQL\Services\CredentialProvider.cs` - `SecretKeys`
- `SiNetSQL\Services\CredentialVaultService.cs` - P/Invoke ל-Credential Manager
- `SiNetProjectManagerV2\Services\SecretProvisioningService.cs` - ייצוא/ייבוא `.secrets`
- `SiNetProjectManagerV2\WPF Window\SecretSetupWindow.xaml(.cs)` - דיאלוג GUI
- `SiNet.SecretImport\Program.cs` - CLI פורטבילי לשרת
- `SiNet.SecretImport\publish-tool.ps1` - publish של הכלי
- `publish-all.ps1` - אורקסטרטור 4 ערוצים
- `DEPLOYMENT.md` - מדריך הפצה כללי

---

## 7. אבטחה

- ✅ הסודות מוצפנים DPAPI per-user - **לא ניתנים לקריאה ע"י משתמש אחר**.
- ✅ קובץ `.secrets` מוצפן AES-256 עם PBKDF2 - בטוח להעביר ב-USB/אימייל.
- ✅ אין סודות ב-Git: `appsettings.json` מכיל רק `BaseUrl` ו-fallback dev שצריך להסיר.
- ⚠️ מי שמקבל את `SiNet.secrets` + הסיסמה = שולט בכל הסודות. שמור על שתיהן בנפרד.
- ⚠️ אחרי החלפת איש צוות שעזב: ייצא חבילה חדשה עם מפתחות שהוחלפו (Gemini, Autodesk וכו').
