# SiNetProjectManagerV2 — Deployment Guide (MSIX + Auto-Update)

הגישה כאן: **MSIX ללא `.wapproj`**. כל ה-pipeline הוא `dotnet publish` → `MakeAppx.exe` → `SignTool.exe` → `robocopy`. אין פרויקט עוטף, אין VS GUI, רק שני קבצים שב-Git: `Package.appxmanifest` והסקריפט `publish-desktop.ps1`. כל הכלים (`MakeAppx`, `SignTool`) כבר מותקנים אצלך כחלק מ-Windows SDK.

## TL;DR

**במחשב פיתוח** (אחרי ההגדרה החד-פעמית למטה):
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\SiNetProjectManagerV2
powershell -ExecutionPolicy Bypass -File .\publish-desktop.ps1
```

**מחשב משתמש קצה — פעם ראשונה בלבד**:
1. (חד-פעמי לכל מחשב) להתקין את התעודה ה-`.cer` ב-`Local Machine → Trusted People` ו-`Trusted Root`.
2. דאבל-קליק על:
   ```
   \\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNetProjectManagerV2.appinstaller
   ```
3. לחיצה על Install.

**מאותו רגע**: בכל פתיחה של האפליקציה, Windows בודק את ה-UNC ומעדכן אוטומטית.

---

## ⚙️ הגדרה חד-פעמית (פעם אחת בלבד למאגר)

### שלב 1: יצירת תעודת חתימה
ב-PowerShell עם הרשאות אדמין:
```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject "CN=SI Office" `
    -KeyUsage DigitalSignature `
    -FriendlyName "SiNet MSIX Signing" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(10)

$pwd = Read-Host -AsSecureString -Prompt "PFX password"
Export-PfxCertificate -Cert $cert -FilePath "$env:USERPROFILE\Documents\SiNet-Signing.pfx" -Password $pwd
Export-Certificate   -Cert $cert -FilePath "$env:USERPROFILE\Documents\SiNet-Signing.cer"

Write-Host "Thumbprint: $($cert.Thumbprint)"
```

⚠️ **חשוב**: ה-`Subject` של התעודה (`CN=SI Office`) חייב **בדיוק** להיות זהה ל-`Publisher` ב-`Package.appxmanifest`. אם אתה משנה אחד - שנה את השני.

### שלב 2: שמירת התעודה
- ה-`.pfx` (פרטי) - להישאר ב-`%USERPROFILE%\Documents\` או location מאובטח אחר. **לעולם לא ב-Git** (כבר חסום ב-`.gitignore`).
- ה-`.cer` (ציבורי) - אפשר לשמור ב-Git אם רוצים שנוח להפיץ.

### שלב 3: התקנת התעודה על מחשבי המשתמשים
חד-פעמי לכל תחנת קצה. כאדמין:
```powershell
Import-Certificate -FilePath ".\SiNet-Signing.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Import-Certificate -FilePath ".\SiNet-Signing.cer" -CertStoreLocation Cert:\LocalMachine\Root
```
או דרך Group Policy לכל הארגון.

### שלב 4 (אופציונלי): אייקונים אמיתיים
הסקריפט מייצר אוטומטית 3 PNG כחולים-פלייסהולדר ב-`SiNetProjectManagerV2\Images\` בריצה ראשונה אם התיקייה לא קיימת. אם רוצים אייקון אמיתי, להחליף את שלושת הקבצים:
- `Images\StoreLogo.png` (50×50)
- `Images\Square150x150Logo.png` (150×150)
- `Images\Square44x44Logo.png` (44×44)

---

## הרצה

### ברירת מחדל (התעודה נבחרת אוטומטית מ-CurrentUser\My):
```powershell
.\publish-desktop.ps1
```
הסקריפט סורק את `Cert:\CurrentUser\My`, מוצא את התעודה הראשונה עם code-signing EKU, וחותם איתה.

### עם thumbprint מפורש:
```powershell
.\publish-desktop.ps1 -CertThumbprint "ABCDEF0123456789..."
```

### עם קובץ PFX:
```powershell
.\publish-desktop.ps1 -CertPfxPath "$env:USERPROFILE\Documents\SiNet-Signing.pfx" -CertPfxPassword "yourpassword"
```

### בדיקה לוקלית בלי העלאה לרשת:
```powershell
.\publish-desktop.ps1 -SkipDeploy
```

### להריץ בלי לחתום (לבדיקה - ה-MSIX לא יותקן ככה):
```powershell
.\publish-desktop.ps1 -SkipSign -SkipDeploy
```

---

## פרמטרים

| פרמטר | ברירת-מחדל | הסבר |
|---|---|---|
| `-DeployDir` | `\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2` | UNC שכל המשתמשים פותחים ממנו |
| `-OutputDir` | `..\artifacts\SiNetProjectManagerV2_Package` | תיקיית ביניים לוקלית |
| `-Runtime` | `win-x64` | תחנות x86 - לא רלוונטי |
| `-CertThumbprint` | אוטומטי | thumbprint של תעודה ב-`CurrentUser\My` |
| `-CertPfxPath` | - | חתימה מקובץ PFX במקום מ-store |
| `-CertPfxPassword` | - | סיסמת ה-PFX |
| `-SkipDeploy` | - | בנייה בלי העלאה לרשת |
| `-SkipSign` | - | בלי חתימה (בדיקה בלבד) |
| `-NoBump` | - | פרסום חוזר של אותה גרסה |

---

## ניהול גרסאות

מקור אמת יחיד: `<Version>` ב-`SiNetProjectManagerV2.csproj`. הסקריפט:
1. מבמפ את הגרסה אוטומטית (Build component עולה ב-1).
2. משלב את הערך לתוך `Package.appxmanifest` בריצה (החלפת placeholder `{VERSION}`).
3. כותב את אותו ערך ל-`.appinstaller`.

**כל שלושת המקומות תמיד תואמים** - אין סיכון של mismatch.

---

## מה הסקריפט עושה (פייפליין מלא)

1. **Bump** ל-`<Version>` ב-csproj.
2. **`dotnet publish`** self-contained, win-x64, **loose files** (לא single-file - לא תואם MSIX).
3. **Stage** ה-`Package.appxmanifest` (עם `{VERSION}` מוחלף) + תיקיית `Images\` לתוך `payload\`.
4. **`MakeAppx.exe pack`** → קובץ `.msix` בשם `SiNetProjectManagerV2_<version>_x64.msix`.
5. **`SignTool.exe sign`** → חתימה ב-SHA256.
6. **כתיבת `.appinstaller`** עם ה-UNC URI - המנגנון שגורם לעדכון אוטומטי בפתיחה הבאה של האפליקציה.
7. **`robocopy /MIR`** → העלאה לכונן הרשת.

---

## מחזור עדכון יומיומי

```powershell
git pull
# ... שינויי קוד ...
.\publish-desktop.ps1            # bump + build + sign + deploy
git commit -am "release vX.Y.Z"
git push
```

המשתמשים לא עושים כלום - הם יקבלו את העדכון בפתיחה הבאה.

---

## פתרון בעיות

### "MakeAppx.exe / SignTool.exe not found"
חסר Windows SDK. ב-Visual Studio Installer → Modify → workload **".NET desktop development"** או **".NET Multi-platform App UI development"** (כל אחד מהם מתקין SDK).

### "SignerSign() failed: 0x800B0109"
התעודה לא ב-Trusted Root במחשב היעד (במחשב המשתמש). חוזרים על שלב 3 לעיל.

### "App package must be digitally signed"
חתמת בתעודה שונה מה-`Publisher` שב-manifest. ה-`Subject` של התעודה חייב להיות זהה בדיוק.

### "Install failed: 0x80073CF3"
המשתמש מנסה להתקין `.msix` עם `Identity/Name` זהה אבל `Publisher` שונה משהותקן קודם. נדרש להסיר את הגרסה הישנה ידנית:
```powershell
Get-AppxPackage *SiNet.ProjectManagerV2* | Remove-AppxPackage
```
