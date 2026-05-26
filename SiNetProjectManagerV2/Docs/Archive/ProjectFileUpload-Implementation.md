# תיקון והשלמת העלאת אלטרנטיבות ל-ACC/Google Drive

## 📋 סיכום הבעיה והפתרון

### הבעיה המקורית
כאשר המשתמש הוסיף **אלטרנטיבה נוספת** (דרך כפתור ימני → "אלטרנטיבה נוספת"), המערכת:
- ✅ העתיקה את הקובץ לשרת הקבצים בהצלחה
- ✅ יצרה רשומת `ProjectFileInstance` במסד הנתונים
- ❌ **אבל**: תמיד הגדירה את `StorageDestination` ל-`FileServer`, ללא קשר להגדרה של ה-`ProjectFile` המקורי
- ❌ **ולא העלתה** את הקובץ ל-ACC או Google Drive אפילו כשהוגדר שם

### הפתרון המלא שיושם

#### 1. **תיקון `StorageDestination`**
   - עדכנו את `CreateFileInstanceAndUploadAsync` להעביר את סוג האחסון מה-`ProjectFile` המקורי
   - עכשיו `ProjectFileInstance` מקבל את `StorageDestination` הנכון (ACC/GoogleDrive/FileServer)

#### 2. **שירות העלאה חדש: `ProjectFileUploadService`**
   קובץ: `..\SiNetSQL\SiNetSQL\Services\Coordinators\ProjectFileUploadService.cs`

   השירות מטפל ב:
   - ✅ בדיקת סוג האחסון (ACC/GoogleDrive/FileServer)
   - ✅ אימות קיום הקובץ בשרת הקבצים
   - ✅ פתרון נתיב תיקיית היעד מהיררכיית `ProjectFolder`
   - ✅ יצירת מיפוי ACC (אם חסר) דרך `IAccProjectProvisioningService`
   - ✅ יצירת מבנה תיקיות ב-ACC (אם חסר)
   - ✅ זיהוי קבצים כפולים (upload as new version במקרה זה)
   - ✅ העלאה ל-ACC דרך `Bim360Service`
   - ✅ עדכון `ProjectFileInstance.AccItemId` לאחר העלאה מוצלחת
   - ⏳ **Google Drive** - תומך בתשתית, מחכה למימוש הספציפי

#### 3. **העלאה אוטומטית ברקע**
   - כאשר מוסיפים אלטרנטיבה לקובץ שמוגדר ל-ACC/GoogleDrive:
     - הקובץ נשמר **תחילה** בשרת הקבצים
     - `ProjectFileInstance` נוצר עם `StorageDestination` הנכון
     - **מיד לאחר מכן**, תהליך רקע מופעל אוטומטית:
       - מעלה את הקובץ ל-ACC
       - מעדכן את `AccItemId` במסד הנתונים
       - **מודיע למשתמש** על הצלחה/כישלון

#### 4. **שינויים ב-DI Container**
   - הוספנו את `ProjectFileUploadService` ל-`App.xaml.cs`
   - יצרנו `ServiceLocator` סטטי כדי לאפשר גישה ל-DI מתוך ספריות משותפות

#### 5. **הודעות למשתמש**
   - **הצלחה**: חלון קופץ מודיע שהקובץ הועלה בהצלחה ל-ACC
   - **כישלון**: חלון קופץ מציג את שגיאת ההעלאה
   - **Log מפורט**: כל התהליך נרשם ב-AppLogger לצורך debugging

---

## 🛠 קבצים שנוצרו/עודכנו

### קבצים חדשים:
1. `..\SiNetSQL\SiNetSQL\Services\Coordinators\ProjectFileUploadService.cs`
   - שירות העלאה מרכזי לקבצי פרויקט

2. `..\SiNetSQL\SiNetSQL\Services\ServiceLocator.cs`
   - Service locator סטטי לגישה ל-DI מספריות משותפות

### קבצים שעודכנו:
3. `..\SiNetSQL\SiNetSQL\MVVM\ProjectFileNode.cs`
   - שינוי `CreateFileInstanceAsync` → `CreateFileInstanceAndUploadAsync`
   - הוספת לוגיקת העלאה ברקע
   - הוספת הודעות למשתמש

4. `SiNetProjectManagerV2\App.xaml.cs`
   - רישום `ProjectFileUploadService` ב-DI
   - אתחול `ServiceLocator` בעת Startup

---

## 🧪 איך לבדוק?

### מקרה מבחן 1: קובץ ACC
1. **הגדרה**: בחר `ProjectFile` שמוגדר ל-`StorageDestination.Acc`
2. **פעולה**: לחץ ימני → "אלטרנטיבה נוספת"
3. **תוצאה צפויה**:
   - הקובץ מועתק לשרת הקבצים
   - תהליך רקע מתחיל (ניתן לראות ב-Log)
   - הקובץ מועלה ל-ACC
   - חלון קופץ: "הקובץ 'XXX' הועלה בהצלחה ל-Acc!"
   - במסד נתונים: `ProjectFileInstance` מכיל `AccItemId`

### מקרה מבחן 2: קובץ FileServer
1. **הגדרה**: בחר `ProjectFile` שמוגדר ל-`StorageDestination.FileServer`
2. **פעולה**: לחץ ימני → "אלטרנטיבה נוספת"
3. **תוצאה צפויה**:
   - הקובץ מועתק לשרת הקבצים
   - **אין** העלאה לענן
   - **אין** הודעה קופצת

### מקרה מבחן 3: קובץ Google Drive
1. **הגדרה**: בחר `ProjectFile` שמוגדר ל-`StorageDestination.GoogleDrive`
2. **פעולה**: לחץ ימני → "אלטרנטיבה נוספת"
3. **תוצאה צפויה (כרגע)**:
   - הקובץ מועתק לשרת הקבצים
   - תהליך רקע מתחיל
   - חלון שגיאה: "העלאה ל-Google Drive טרם מיושמת."

---

## 📊 זרימת התהליך (Flow)

```
┌─────────────────────────────────────┐
│ משתמש: לחץ ימני → אלטרנטיבה נוספת  │
└──────────────┬──────────────────────┘
               │
               v
┌──────────────────────────────────────┐
│ ProjectFileNode.GetAlternativeNode   │
│ - בדיקת סוג קובץ                    │
│ - העתקה לשרת קבצים                   │
│ - קריאה ל-CreateFileInstanceAndUploadAsync │
└──────────────┬───────────────────────┘
               │
               v
┌──────────────────────────────────────┐
│ CreateFileInstanceAndUploadAsync      │
│ - יצירת ProjectFileInstance           │
│ - זיהוי StorageDestination            │
└──────────────┬───────────────────────┘
               │
               ├─────────────────────────────────┐
               │                                 │
               v (אם ACC/GoogleDrive)            v (אם FileServer)
┌──────────────────────────────────────┐    ┌──────────┐
│ Task.Run (רקע)                       │    │ סיום     │
│ - קבלת ProjectFileUploadService       │    └──────────┘
│ - קריאה ל-UploadFileInstanceAsync     │
└──────────────┬───────────────────────┘
               │
               v
┌──────────────────────────────────────┐
│ ProjectFileUploadService              │
│ - טעינת FileInstance + ProjectFile    │
│ - פתרון נתיב תיקייה                   │
│ - הבטחת מיפוי ACC                     │
│ - יצירת מבנה תיקיות ב-ACC             │
│ - זיהוי כפילויות                      │
│ - העלאה דרך Bim360Service             │
│ - עדכון AccItemId                     │
└──────────────┬───────────────────────┘
               │
               v
┌──────────────────────────────────────┐
│ הודעה למשתמש (UI thread)             │
│ ✅ "הקובץ הועלה בהצלחה!"             │
│ ❌ "שגיאה בהעלאה: XXX"               │
└──────────────────────────────────────┘
```

---

## 🚀 מה עוד חסר? (עבודה עתידית)

### Google Drive Upload
הקוד מוכן לקבל Google Drive, אבל יש להוסיף:
1. מימוש `UploadToGoogleDriveAsync` ב-`ProjectFileUploadService`
2. שימוש ב-`GoogleDriveService` מה-connector הקיים
3. מיפוי Google Drive Folder IDs למבנה הפרויקט

### UI Enhancements
- אינדיקטור התקדמות להעלאות גדולות
- תצוגת סטטוס העלאות פעילות (כמו ב-Email Management)
- אפשרות לביטול העלאה באמצע

### Error Handling
- retry logic להעלאות שנכשלו
- queue management להעלאות מרובות בו-זמנית

---

## 📝 הערות חשובות

1. **מעקב במסד נתונים**:
   - `ProjectFileInstance` כעת מתעד את סוג האחסון האמיתי
   - `AccItemId` מתעדכן רק לאחר העלאה מוצלחת
   - ניתן לזהות קבצים ש"תקועים" (נוצרו אבל לא הועלו) על ידי: `StorageDestination = ACC AND AccItemId IS NULL`

2. **Logging**:
   - כל פעולת העלאה מתועדת עם `correlationId` ייחודי
   - שגיאות נרשמות ב-`AppLogger` ובלוג המרכזי

3. **ביצועים**:
   - ההעלאה מתבצעת ב-`Task.Run` כך שלא חוסמת את ה-UI
   - המשתמש יכול להמשיך לעבוד מיד לאחר שהקובץ הועתק

4. **תאימות לאחור**:
   - קבצים קיימים (ללא `ProjectFileInstance`) ממשיכים לעבוד כרגיל
   - השינויים משפיעים רק על קבצים חדשים שנוצרים מעכשיו

---

## ✅ סיכום

התיקון מבטיח ש**כל** אלטרנטיבה חדשה שמתווספת עכשיו:
1. 📝 נרשמת נכון במסד הנתונים עם `StorageDestination` הנכון
2. ☁️ מועלית אוטומטית ל-ACC (אם מוגדר כך)
3. 📢 מודיעה למשתמש על הצלחה/כישלון
4. 📊 מתועדת מלאה ב-Log

**המערכת עכשיו עובדת כמצופה!** 🎉
