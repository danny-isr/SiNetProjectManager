# SiNetProjectManagerV2 — Deployment Guide (MSIX + Auto-Update)

## TL;DR — אחרי ההגדרה החד-פעמית

**במחשב פיתוח** (גרסה מתבמפת, MSIX + .appinstaller מועתקים לשרת):
```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\SiNetProjectManagerV2
powershell -ExecutionPolicy Bypass -File .\publish-desktop.ps1
```

**מחשב משתמש קצה - פעם ראשונה בלבד**:
1. (חד-פעמי לכל מחשב) להתקין את תעודת החתימה (`.cer`) ב-`Local Machine → Trusted People` ו-`Trusted Root Certification Authorities`.
2. דאבל-קליק על:
   ```
   \\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNetProjectManagerV2.Package.appinstaller
   ```
3. ללחוץ Install.

**מאותו רגע**: בכל פתיחה של האפליקציה היא בודקת את ה-UNC ומעדכנת את עצמה אוטומטית. אין יותר שום פעולה ידנית במחשבי המשתמשים.

---

## ⚠️ הגדרה חד-פעמית (לפני שהסקריפט יעבוד)

הסקריפט `publish-desktop.ps1` מצפה לפרויקט `Windows Application Packaging Project` בשם `SiNetProjectManagerV2.Package`. צריך ליצור אותו **פעם אחת** ב-Visual Studio (אין SDK רשמי ליצירה דרך CLI).

### שלב 1: להוסיף את פרויקט ה-Packaging
1. ב-VS 2026, ב-**Solution Explorer**, קליק ימני על ה-Solution → **Add → New Project**.
2. לחפש: **"Windows Application Packaging Project"** (C#).
   - אם לא מופיע: VS Installer → Modify → להפעיל את workload **"Universal Windows Platform development"** או רכיב **"MSIX Packaging Tools"**.
3. שם: **`SiNetProjectManagerV2.Package`**
4. Location: **`D:\repos2026\SiNetProjectManager_GitHub\`** (אחות ל-`SiNetProjectManagerV2`).
5. Target Windows version: **Windows 10, version 2004 (build 19041)** או חדש יותר. Minimum: 17763.

### שלב 2: לקשר את ה-WPF
1. בפרויקט ה-Packaging החדש, קליק ימני על **Applications → Add Reference**.
2. לסמן `SiNetProjectManagerV2` → OK.
3. קליק ימני על `SiNetProjectManagerV2` תחת Applications → **Set as Entry Point**.

### שלב 3: לערוך את `Package.appxmanifest`
- **Application** → Display name: `SiNet Project Manager`
- **Packaging** tab:
  - **Package name**: `SiNet.ProjectManagerV2`
  - **Publisher**: ל-EXACT match עם תעודת החתימה (Subject CN). למשל `CN=SI Office`.
  - **Version**: `1.0.0.0` (Major.Minor.Build.Revision - Revision חייב להיות 0).
- **Visual Assets**: לטעון אייקון מ-`SiNetProjectManagerV2\Assets\si.ico` (להמיר ל-PNG בגודל 256×256 קודם).

### שלב 4: ליצור תעודת חתימה (חד-פעמי לכל מאגר)
ב-PowerShell עם הרשאות אדמין:
```powershell
# יצירה
$cert = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject "CN=SI Office" `
    -KeyUsage DigitalSignature `
    -FriendlyName "SiNet MSIX Signing" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(10)

# ייצוא PFX (לחתימה - לא נכנס ל-Git!)
$pwd = Read-Host -AsSecureString -Prompt "PFX password"
Export-PfxCertificate -Cert $cert -FilePath "$env:USERPROFILE\Documents\SiNet-Signing.pfx" -Password $pwd

# ייצוא CER (חלק ציבורי - מותקן על מחשבי משתמשים)
Export-Certificate -Cert $cert -FilePath "$env:USERPROFILE\Documents\SiNet-Signing.cer"

Write-Host "Thumbprint: $($cert.Thumbprint)"  # לרשום!
```

### שלב 5: לחבר את התעודה ל-`.wapproj`
1. ב-VS, קליק ימני על `SiNetProjectManagerV2.Package` → **Properties** (או לערוך את `.wapproj` ידנית).
2. ב-`Package.appxmanifest` → **Packaging** → **Choose Certificate**:
   - להצביע על ה-PFX או לבחור מה-store לפי thumbprint.
3. ה-`.wapproj` אמור להכיל אחרי זה:
   ```xml
   <PropertyGroup>
     <PackageCertificateThumbprint>YOUR_THUMBPRINT_HERE</PackageCertificateThumbprint>
     <AppxPackageSigningEnabled>True</AppxPackageSigningEnabled>
     <AppxAutoIncrementPackageRevision>True</AppxAutoIncrementPackageRevision>
     <GenerateAppInstallerFile>True</GenerateAppInstallerFile>
     <AppInstallerUri>\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\</AppInstallerUri>
     <AppInstallerCheckForUpdateFrequency>OnApplicationRun</AppInstallerCheckForUpdateFrequency>
     <HoursBetweenUpdateChecks>0</HoursBetweenUpdateChecks>
   </PropertyGroup>
   ```

### שלב 6: התקנת התעודה על מחשבי המשתמשים (חד-פעמי לכל מחשב)
להעתיק את `SiNet-Signing.cer` למחשב היעד ולהריץ (כאדמין):
```powershell
Import-Certificate -FilePath ".\SiNet-Signing.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Import-Certificate -FilePath ".\SiNet-Signing.cer" -CertStoreLocation Cert:\LocalMachine\Root
```
או דרך Group Policy לכל הארגון.

### שלב 7: Git
לוודא ש-`.gitignore` כולל:
```
*.pfx
artifacts/
**/AppPackages/
**/BundleArtifacts/
```
התעודה ה-`.cer` (חלק ציבורי) **כן** יכולה להיכנס ל-Git אם רוצים. ה-`.pfx` **לעולם לא**.

---

## ניהול גרסאות

יש **שני** מקורות גרסה:
1. `<Version>` ב-`SiNetProjectManagerV2.csproj` - לקוד .NET (AssemblyVersion/FileVersion).
2. `Version` ב-`Package.appxmanifest` (תוך `<Identity ... Version="1.0.0.0" />`) - ל-MSIX.

הסקריפט `publish-desktop.ps1` מבמפ אוטומטית את (1). ה-(2) מתעלה אוטומטית בזכות `<AppxAutoIncrementPackageRevision>True</AppxAutoIncrementPackageRevision>`.

**חשוב**: ה-MSIX Revision (החלק הרביעי) חייב להישאר 0 ב-manifest. אל תערוך את זה ידנית - ה-MSBuild יעדכן את ה-Build component אוטומטית בכל publish.

---

## פרמטרים של הסקריפט

| פרמטר | ברירת-מחדל | מתי לשנות |
|---|---|---|
| `-DeployDir` | `\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2` | ה-UNC שכל המשתמשים פותחים ממנו |
| `-WapProj` | `..\SiNetProjectManagerV2.Package\SiNetProjectManagerV2.Package.wapproj` | אם שמרתם את הפרויקט בשם אחר |
| `-Platform` | `x64` | תחנות x86 - לא רלוונטי |
| `-SkipDeploy` | - | לבדיקה לוקלית |
| `-NoBump` | - | פרסום חוזר של אותה גרסה |

---

## מחזור עדכון

1. `git pull` בכל אחת מתחנות הפיתוח.
2. שינויי קוד → commit.
3. `.\publish-desktop.ps1` → גרסה מתבמפת, MSIX נחתם, מועלה ל-UNC.
4. `git commit -am "release vX.Y.Z"` → push.
5. **המשתמשים** - לא עושים כלום. בפעם הבאה שיפתחו את האפליקציה היא תזהה את הגרסה החדשה ב-UNC ותעדכן את עצמה.
