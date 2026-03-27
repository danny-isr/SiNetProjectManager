# Phase 4 Testing Guide - Data Cleaning & Summary Display

**Date:** 2026-02-28  
**Feature:** Automatic Empty Row Filtering + "Ready to Import" Summary  
**Status:** ✅ Built Successfully - Ready for Testing After App Restart

---

## 🧪 Quick Test Procedure

### **Test Data (Copy/Paste This)**
```
1844-יבנה	פתוח	תיאור 1	דני	רגיל	25/02/2026	סוג 1
		
1844-יבנה	ממתין	תיאור 2	שרה	גבוה	26/02/2026	סוג 2
1845-חיפה	סגור	תיאור 3	יוסי	רגיל	27/02/2026	סוג 1
		
1845-חיפה	החלטה	תיאור 4	דני	דחוף	28/02/2026	החלטה
1846-ת"א	פתוח	תיאור 5	שרה	רגיל	01/03/2026	סוג 3
```

**Note:** Lines 2 and 5 are **intentionally empty** (simulates Excel copy-paste with blank rows)

---

## ✅ Expected Behavior

### **Step 1: After Clicking "Preview"**
- ✅ Preview grid shows **5 rows** (not 7!)
- ✅ Empty rows automatically filtered (silently removed)
- ✅ Status message: **"תצוגה מקדימה: 5 שורות תקינות. התעלמו מ-2 שורות ריקות."**
- ✅ Status Mapping section appears (yellow) with 4 unique statuses
- ✅ Import button: **DISABLED** (grayed out)

### **Step 2: Configure Status Mapping**
- ✅ Statuses shown:
  - פתוח (2) → Default: Task
  - ממתין (1) → Default: Task
  - סגור (1) → Default: Task
  - החלטה (1) → **Change to Decision** (click right radio button)

### **Step 3: Click "Apply Mapping"**
- ✅ Preview grid rows change colors:
  - Rows 1, 3, 5 (פתוח, סגור) → **Light Blue** (#E3F2FD)
  - Row 2 (ממתין) → **Light Blue**
  - Row 4 (החלטה) → **Light Green** (#E8F5E9)
- ✅ "Apply Mapping" button → Changes to green "✓ מיפוי הוחל"
- ✅ Status message: **"✓ מיפוי הוחל! 4 שורות → משימות (כחול), 1 שורות → החלטות (ירוק). התעלמו מ-2 שורות ריקות."**

### **Step 4: Ready to Import Summary Appears** ✨
- ✅ Green box appears below Apply Mapping button
- ✅ Title: **"✓ מוכן לייבוא"**
- ✅ Count display:
  - **"4 משימות (כחול)"** (blue text)
  - **"|"** (separator)
  - **"1 החלטות (ירוק)"** (green text)
  - **"|"** (separator)
  - **"התעלמו מ-2 שורות ריקות"** (orange text) ← Only if empty rows were filtered
- ✅ Instruction text: "בדוק את הצבעים בטבלה למעלה ולחץ על 'ייבא למערכת' להשלמת הייבוא"

### **Step 5: Import Button Enabled**
- ✅ Import button: **NOW ENABLED** (green background)
- ✅ User can click to complete import

---

## 🔍 What to Verify

### **Empty Row Filtering:**
- [ ] Empty rows **not visible** in preview grid
- [ ] Row count matches **valid rows only** (5 instead of 7)
- [ ] Ignored count shown in status message (**2 שורות ריקות**)
- [ ] Debug console shows: `[TaskImport] Filtered empty row: RowNumber=X`

### **Summary Display:**
- [ ] Green "Ready to Import" box **only appears after clicking "Apply Mapping"**
- [ ] Task count shows **4** in **blue** color (#1976D2)
- [ ] Decision count shows **1** in **green** color (#388E3C)
- [ ] Ignored count shows **2** in **orange** color (#F57C00)
- [ ] Ignored count **only visible** if `HasIgnoredRows = true`

### **Color-Coding:**
- [ ] Blue rows (פתוח, ממתין, סגור) → 4 rows
- [ ] Green rows (החלטה) → 1 row
- [ ] Total colored rows: 5 (matches valid row count)

### **Button States:**
- [ ] Import button **disabled** before Apply Mapping
- [ ] Import button **enabled** after Apply Mapping
- [ ] Apply Mapping button **disabled** after clicking (turns green)

---

## 🐛 Edge Cases to Test

### **Test Case 1: All Empty Rows**
**Input:**
```
		
		
		
```

**Expected:**
- Status message: "לא נמצאו שורות תקינות בנתונים."
- Preview grid: Empty
- Status Mapping section: Hidden
- Import button: Disabled

---

### **Test Case 2: No Empty Rows**
**Input:** (all valid rows, no blanks)
```
1844-יבנה	פתוח	תיאור 1	דני	רגיל	25/02/2026	סוג 1
1844-יבנה	ממתין	תיאור 2	שרה	גבוה	26/02/2026	סוג 2
```

**Expected:**
- Status message: "תצוגה מקדימה: 2 שורות מוכנות." (**No ignored text!**)
- After Apply Mapping:
  - Summary shows: **"2 משימות (כחול) | 0 החלטות (ירוק)"**
  - **No ignored count displayed** (HasIgnoredRows = false)

---

### **Test Case 3: Re-Mapping After Change**
**Steps:**
1. Preview data
2. Configure mapping: פתוח → Task
3. Apply Mapping → See blue rows
4. **Change mapping**: פתוח → Decision
5. **Click "Apply Mapping" again**

**Expected:**
- Rows re-color: פתוח rows change from blue to green
- Summary updates with new counts
- Import button stays enabled

---

### **Test Case 4: Preview Refresh**
**Steps:**
1. Preview data with 2 empty rows
2. Apply Mapping → Summary shows "התעלמו מ-2"
3. **Edit TSV text** (remove empty rows manually)
4. Click **Preview again**

**Expected:**
- `IsMappingApplied` resets to `false`
- Summary box **disappears** (requires new Apply Mapping)
- Ignored count recalculates (should be 0 now)
- Import button **disabled again**

---

## 🎯 Success Criteria

✅ **PASS** if:
1. Empty rows automatically filtered without errors
2. Ignored count displayed correctly in status messages
3. "Ready to Import" summary box appears after Apply Mapping
4. Task/Decision/Ignored counts accurate and color-coded
5. Import button disabled until mapping applied
6. Re-mapping works (can re-click Apply Mapping)
7. Preview refresh clears mapping state

❌ **FAIL** if:
- Empty rows visible in preview grid
- Summary box appears before Apply Mapping clicked
- Count numbers incorrect
- Colors not applied to rows
- Import button enabled before mapping applied
- App crashes on empty row data

---

## 📊 Debug Console Output (Expected)

```
[TaskImport] Filtered empty row: RowNumber=2
[TaskImport] Filtered empty row: RowNumber=5
[TaskImport] Preview complete: 5 valid rows, 2 empty rows ignored, 4 unique statuses, HasStatusMappings=True
  Status: 'פתוח' (2 items)
  Status: 'ממתין' (1 items)
  Status: 'סגור' (1 items)
  Status: 'החלטה' (1 items)
```

---

## 🛠️ How to Test

1. **Stop debugging** (if running)
2. **Restart application** (required for INotifyPropertyChanged changes)
3. Open **Import Window** (from main menu or floating tasks)
4. Copy **test data** from this document
5. Paste into TSV input box
6. Click **"Preview"**
7. Verify empty rows filtered
8. Configure **Status Mapping** (change "החלטה" to Decision)
9. Click **"Apply Mapping"**
10. Verify colors applied
11. Verify **"Ready to Import" summary** displays correctly
12. Click **"Import"**
13. Verify data imported to correct tables

---

## 📝 Test Results Log

| Test Case | Status | Notes |
|-----------|--------|-------|
| Empty row filtering | ⬜ Not Tested | Expected: 2 rows ignored |
| Summary box display | ⬜ Not Tested | Expected: Shows after Apply Mapping |
| Task count (blue) | ⬜ Not Tested | Expected: 4 |
| Decision count (green) | ⬜ Not Tested | Expected: 1 |
| Ignored count (orange) | ⬜ Not Tested | Expected: 2 |
| Color-coded rows | ⬜ Not Tested | Expected: 4 blue, 1 green |
| Button states | ⬜ Not Tested | Expected: Disabled → Enabled |
| Re-mapping works | ⬜ Not Tested | Expected: Colors update |
| All empty rows | ⬜ Not Tested | Expected: "לא נמצאו שורות" |
| No empty rows | ⬜ Not Tested | Expected: No ignored text |

**Fill in during testing:** ✅ Pass | ❌ Fail | ⚠️ Issue

---

## 🎉 Completion

Once all tests pass:
- [ ] Mark Visual-Confirmation-Import-Feature.md as **FULLY TESTED**
- [ ] Update main documentation with screenshots
- [ ] Consider Phase 5 enhancements (if needed)

**Phase 4 is COMPLETE!** 🚀
