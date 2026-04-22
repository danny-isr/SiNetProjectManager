# 🔧 פתרון בעיות ClickOnce - מדריך איתור תקלות

## 📍 הבעיה: ההתקנה מצליחה אבל האפליקציה לא נפתחת

### ✅ **שינויים שבוצעו:**

1. ✅ **הוספת try-catch גלובלי ב-OnStartup**
   - עכשיו אם יש exception, תופיע הודעת שגיאה מפורטת
   - הודעת השגיאה כוללת קוד שגיאה ונתיב ללוגים

2. ✅ **הוספת logging מפורט לכל שלב הפעלה**
   - כל שלב (1-10) כותב ללוג מתי הוא מתחיל
   - אם משהו נכשל, תוכל לראות בדיוק באיזה שלב

---

## 🔍 **איך לאתר את הבעיה:**

### **שלב 1: פרסם את האפליקציה מחדש**

```powershell
# Visual Studio -> Build -> Publish SiNetProjectManagerV2
```

וודא שבחרת:
- ✅ ClickOnce (לא FileSystem)
- ✅ Publish location: `\\si-win-2k19\AppFolder\AppNet\SiProjecNet2026-Full`
- ✅ Install location: אותו נתיב או ריק

---

### **שלב 2: נסה להתקין במחשב יעד**

1. הפעל: `\\si-win-2k19\AppFolder\AppNet\SiProjecNet2026-Full\setup.exe`
2. אם האפליקציה לא נפתחת, **תופיע עכשיו הודעת שגיאה עם קוד שגיאה ונתיב ללוגים**

---

### **שלב 3: בדוק את הלוגים**

הלוגים נמצאים ב:
```
C:\Users\[UserName]\AppData\Local\SiNetProjectManagerV2\Logs\
```

**או שתופיע הנתיב המדויק בהודעת השגיאה!**

פתח את הקובץ האחרון:
```
SiNet-YYYYMMDD.log
```

חפש:
```
[STARTUP]
```

תראה בדיוק באיזה שלב האפליקציה נכשלה, למשל:
```
[STARTUP] Step 2: Ensuring database connection...
[STARTUP] Database connection failed. Shutting down.
```

---

### **שלב 4: בדוק Event Viewer (אם אין לוגים)**

אם האפליקציה קורסת **לפני שהלוגים נוצרים**:

1. פתח Event Viewer:
   ```
   eventvwr.msc
   ```

2. נווט ל:
   ```
   Windows Logs -> Application
   ```

3. סנן לפי:
   - **Source**: `Application Error`, `.NET Runtime`, `ClickOnce`
   - **Level**: Error, Critical

4. או הרץ ב-PowerShell:
   ```powershell
   Get-EventLog -LogName Application -Source 'Application Error','.NET Runtime' -Newest 20 | 
       Where-Object {$_.TimeGenerated -gt (Get-Date).AddHours(-1)} | 
       Format-List -Property TimeGenerated,Source,Message
   ```

---

## 🐛 **בעיות נפוצות ופתרונות:**

### **1. "חסר connection string ל-SiNetDatabase"**

**סיבה:** Credential Manager לא מוגדר במחשב היעד.

**פתרון:**
- העתק קובץ `SiNet.secrets` לתיקיית ההתקנה
- או הגדר Credential Manager ידנית ב-Windows

---

### **2. "Could not load file or assembly 'WebView2'"**

**סיבה:** WebView2 Runtime לא מותקן.

**פתרון:**
- הוסף WebView2 Runtime ל-Prerequisites ב-ClickOnce publish
- או התקן ידנית מ: https://developer.microsoft.com/en-us/microsoft-edge/webview2/

---

### **3. "Database connection failed"**

**סיבה:** המחשב לא מחובר לרשת / אין גישה ל-SQL Server.

**פתרון:**
- בדוק חיבור רשת
- וודא ש-SQL Server נגיש מהמחשב היעד
- בדוק Firewall rules

---

### **4. ".NET 8 Runtime is not installed"**

**סיבה:** .NET 8 Runtime חסר במחשב היעד.

**פתרון:**
- הוסף .NET 8 Desktop Runtime ל-Prerequisites
- או התקן ידנית מ: https://dotnet.microsoft.com/download/dotnet/8.0

---

## 📋 **Checklist לפני פרסום:**

- [ ] Build מצליח ב-Release mode
- [ ] כל קבצי התצורה מסומנים ב-"Copy to Output Directory"
- [ ] Credential Manager מוגדר (או יש קובץ SiNet.secrets)
- [ ] SQL Server נגיש מהרשת
- [ ] WebView2 Runtime מותקן (או ב-Prerequisites)
- [ ] .NET 8 Runtime מותקן (או ב-Prerequisites)

---

## 🚀 **צעדים הבאים:**

1. **פרסם מחדש** עם השינויים
2. **נסה להתקין** במחשב יעד
3. **צלם/העתק את הודעת השגיאה** (אם יש)
4. **שלח את הלוגים** מ-`%LocalAppData%\SiNetProjectManagerV2\Logs`

---

_עודכן לאחרונה: 2026-04-22_
