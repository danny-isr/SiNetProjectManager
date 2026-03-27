# Multi-Line Cell Test Data

**Purpose:** Test TSV parser with multi-line descriptions (Alt+Enter in Excel)

**Instructions:** Copy the entire test data block below and paste into the Import Window.

---

## Test Data Set 1: Basic Multi-Line Description

```
Project	Priority	Handler	Status	Description	CheckType	Type
1844-יבנה	רגיל	דני	פתוח	Line 1: בדיקת חשמל
Line 2: תיאור נוסף כאן
Line 3: הערות סופיות	כללי	סוג 1
1845-חיפה	גבוה	שרה	סגור	Single line description only	כללי	סוג 2
1846-ת"א	רגיל	יוסי	ממתין	Another multi-line
with second line
and third line	כללי	סוג 1
```

**Expected Result:**
- **3 rows** imported (not 9!)
- Row 1: Description has 3 lines
- Row 2: Description has 1 line
- Row 3: Description has 3 lines

**Debug Output Should Show:**
```
[TaskImport] Line 2: New row detected, 8 columns
[TaskImport] Line 3: Merged multi-line continuation into Description: 'Line 2: תיאור נוסף כאן'
[TaskImport] Line 4: Merged multi-line continuation into Description: 'Line 3: הערות סופיות'
[TaskImport] Line 5: New row detected, 8 columns
[TaskImport] Line 6: New row detected, 8 columns
[TaskImport] Line 7: Merged multi-line continuation into Description: 'with second line'
[TaskImport] Line 8: Merged multi-line continuation into Description: 'and third line'
[TaskImport] ParseTsvLines: Processed 8 lines → 3 rows (multi-line cells merged)
```

---

## Test Data Set 2: With Empty Rows

```
Project	Priority	Handler	Status	Description
1844-יבנה	רגיל	דני	פתוח	Description with
multiple lines
here

1845-חיפה	גבוה	שרה	סגור	Single line

```

**Expected Result:**
- **2 rows** imported
- Empty lines between rows are **skipped** (not merged)
- Row 1: 3-line description
- Row 2: 1-line description

---

## Test Data Set 3: Quoted Fields (Excel Export)

```
Project	Description
1844-יבנה	"This is quoted
with embedded newline"
1845-חיפה	Normal unquoted text
1846-ת"א	"Quote with ""internal"" quotes"
```

**Expected Result:**
- **3 rows** imported
- Row 1: Description = `This is quoted\nwith embedded newline` (quotes removed)
- Row 2: Description = `Normal unquoted text`
- Row 3: Description = `Quote with "internal" quotes` (doubled quotes unescaped)

---

## Test Data Set 4: Edge Case - No Tabs in Continuation

```
Project	Priority	Handler	Status	Description
1844-יבנה	רגיל	דני	פתוח	First line
Second line without any tabs
Third line also no tabs
1845-חיפה	גבוה	שרה	סגור	Another description
```

**Expected Result:**
- **2 rows** imported
- Row 1 Description: `First line\nSecond line without any tabs\nThird line also no tabs`
- Row 2 Description: `Another description`

---

## Test Data Set 5: Complex Real-World Example

```
Project	Priority	Handler	Status	Description	CheckType	Type
1844-יבנה מזרח	גבוה	דני כהן	פתוח	בדיקת חשמל:
1. בדוק לוח חשמל
2. בדוק קווים
3. אישור חשמלאי	בדיקה	חשמל
1845-חיפה נמל	רגיל	שרה לוי	ממתין	תיקון צנרת
הערות: לתאם עם קבלן	תיקון	אינסטלציה
1846-תל אביב	דחוף	יוסי מזרחי	החלטה	החלטת ועדה:
נדחה עד לאישור
יש לבדוק חלופות	החלטה	ועדה
1847-ירושלים	רגיל	מיכל ברק	סגור	הושלם בהצלחה	בדיקה	כללי
```

**Expected Result:**
- **4 rows** imported
- Row 1 (1844): Description with numbered list (3 lines)
- Row 2 (1845): Description with note (2 lines)  
- Row 3 (1846): Description with multi-line decision (3 lines)
- Row 4 (1847): Single line description

**Status Mapping:**
- פתוח → Task
- ממתין → Task
- החלטה → **Decision** (user must select)
- סגור → Task

**After Apply Mapping:**
- Rows 1, 2, 4 → **Blue** (Tasks)
- Row 3 → **Green** (Decision)

---

## How to Test

1. **Stop debugging** (if app is running)
2. **Restart application**
3. Open **Import Window**
4. **Copy one test data set** from above
5. **Paste** into TSV input box
6. Click **"Preview"** button
7. **Check Output window** for debug messages:
   - Open **View → Output** (Ctrl+Alt+O)
   - Select **"Debug"** from dropdown
   - Look for `[TaskImport] Merged multi-line continuation...` messages
8. **Verify preview grid**:
   - Row count should match expected (not line count!)
   - Click on rows to see full Description in details
9. **Configure Status Mapping** (if applicable)
10. Click **"Apply Mapping"** to see colors
11. **Verify "Ready to Import" summary**:
    - Shows correct Task/Decision counts
    - Shows ignored count if empty rows present
12. Click **"Import"** to commit to database
13. **Verify imported data** in database or main view

---

## Success Indicators

✅ **Parser Working Correctly:**
- Debug output shows "Merged multi-line continuation" messages
- Row count in preview = actual data rows (not line count)
- No orphaned rows in preview grid
- Descriptions contain all lines with newlines preserved

❌ **Parser Broken:**
- Row count > actual data rows (continuation lines treated as rows)
- Orphaned rows with empty ProjectLink
- Descriptions truncated at first newline
- Import errors: "ProjectLink is required"

---

## Troubleshooting

### **Issue: Continuation lines still appearing as separate rows**

**Possible Causes:**
1. App not restarted after code changes
2. Continuation lines have too many tabs (look like valid rows)
3. First column has unexpected content triggering "new row" detection

**Solution:**
- Restart app
- Check debug output to see why line was detected as new row
- Adjust `MinValidColumns` or project pattern matching if needed

### **Issue: Valid rows being merged into previous row**

**Possible Causes:**
1. First column (ProjectLink) is empty
2. First column doesn't match project pattern (no digit, no quote, too short)

**Solution:**
- Ensure ProjectLink column has content
- Add leading digit or quote to trigger "new row" detection
- Check debug output: "WARNING - Continuation line but no Description column"

---

## Quick Reference: Column Mapping

| Index | Column Name | Example Value | Notes |
|-------|-------------|---------------|-------|
| 0 | ProjectLink | `1844-יבנה` | **Must have content** for new row |
| 1 | Priority | `רגיל` | Optional |
| 2 | Handler | `דני כהן` | Optional |
| 3 | StatusName | `פתוח` | Triggers Status Mapping |
| 4 | **Description** | `Multi-line text` | **Continuation lines merged here** |
| 5 | CheckType | `בדיקה` | Optional |
| 6 | TypeName | `חשמל` | Optional |
| 7+ | Extra | Various | Additional fields |

**Key:** Column 4 (Description) is where multi-line content is merged.

---

## Notes

- **Alt+Enter in Excel** creates `\n` (LF) character inside cell
- **Excel CSV Export** wraps cells with newlines in double quotes `"..."`
- **Google Sheets** similar behavior to Excel
- **Tab character** (`\t`) is primary field separator
- **Empty lines** between rows are skipped (not merged)
- **Leading tabs** in continuation lines are trimmed
- **Quoted fields** are unquoted by `CleanCell()` method

---

**Last Updated:** 2026-02-28  
**Related Docs:** Multi-Line-Cell-Support.md, Visual-Confirmation-Import-Feature.md
