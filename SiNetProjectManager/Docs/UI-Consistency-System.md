# 🎨 Universal UI Consistency System

## Overview
This application implements a **centralized theming system** where **all windows, dialogs, and controls** automatically inherit visual settings from the **General Settings** (AppSettings). Users can change fonts, colors, and sizes in one place, and the changes propagate instantly to all open windows.

---

## 🏗️ Architecture

### 1. **Dynamic Resources** (Single Source of Truth)
All visual properties are stored as **DynamicResource** keys in `App.xaml`:

```xaml
<FontFamily x:Key="AppFontFamily">Segoe UI</FontFamily>
<sys:Double x:Key="AppFontSize">12</sys:Double>
<SolidColorBrush x:Key="AppForeground">Black</SolidColorBrush>
<SolidColorBrush x:Key="AppBackground">White</SolidColorBrush>
```

These resources are updated via `App.ApplySettings()` whenever user changes settings.

---

### 2. **Global Implicit Styles** (Auto-Apply to All Controls)
`App.xaml` defines implicit styles for **every common WPF control type**:

```xaml
<Style TargetType="Window">
    <Setter Property="FontFamily" Value="{DynamicResource AppFontFamily}" />
    <Setter Property="FontSize" Value="{DynamicResource AppFontSize}" />
    <Setter Property="Foreground" Value="{DynamicResource AppForeground}" />
    <Setter Property="Background" Value="{DynamicResource AppBackground}" />
</Style>

<Style TargetType="Button">
    <Setter Property="FontFamily" Value="{DynamicResource AppFontFamily}" />
    <Setter Property="FontSize" Value="{DynamicResource AppFontSize}" />
    <Setter Property="Foreground" Value="{DynamicResource AppForeground}" />
    <Setter Property="Background" Value="{DynamicResource AppBackground}" />
</Style>

<!-- Similar styles for: TextBlock, Label, TextBox, ComboBox, DataGrid, 
     ListBox, CheckBox, RadioButton, GroupBox, TabControl, Menu, etc. -->
```

**Result:** Every control automatically inherits these styles unless explicitly overridden.

---

### 3. **Real-Time Updates** (Live Theme Changes)
When a user changes settings in the **Settings Window**, the changes apply **immediately** to all open windows:

#### Flow:
1. User changes font/color in Settings Window
2. `AppSettings` fires `PropertyChanged` event
3. `SettingsWindow.Settings_PropertyChanged()` calls `App.ApplySettings()`
4. `App.ApplySettings()` updates the 4 DynamicResource keys
5. **All controls automatically re-render** (WPF DynamicResource magic)

```csharp
// App.xaml.cs
public static void ApplySettings()
{
    if (AppSettings == null) return;
    
    // Update all dynamic resources — triggers immediate UI updates across ALL windows
    Current.Resources["AppFontFamily"] = new FontFamily(AppSettings.FontFamily);
    Current.Resources["AppFontSize"] = AppSettings.FontSize;
    Current.Resources["AppForeground"] = new SolidColorBrush(...);
    Current.Resources["AppBackground"] = new SolidColorBrush(...);
}
```

---

## 📐 Design Principles

### ✅ DO:
- **Bind to DynamicResources** for any visual property that should respond to settings
- **Use `BasedOn="{StaticResource {x:Type ControlType}}"` when creating custom styles** to preserve global theme
- **Use `Opacity` for muted text** instead of hardcoded "Gray" colors (respects current Foreground)
- **Test theme changes** with Settings Window to verify all controls update correctly

### ❌ DON'T:
- **Never hardcode colors** (`Foreground="Gray"`, `Background="White"`)
- **Never hardcode fonts** (`FontFamily="Arial"`, `FontSize="14"`)
- **Avoid overriding** Background/Foreground in local styles unless absolutely necessary
- **Don't use `StaticResource`** for theme properties (won't update dynamically)

---

## 🛠️ Implementation Checklist for New Windows

When creating a new window/dialog, ensure:

1. **No hardcoded visual properties** in XAML
2. **Custom styles inherit from global base**:
   ```xaml
   <Style TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
       <!-- Your custom setters here -->
   </Style>
   ```
3. **Use Opacity for muted text** (not `Foreground="Gray"`):
   ```xaml
   <TextBlock Text="Hint text" Opacity="0.7"/>
   ```
4. **Test with different themes**: Change font size/color in Settings and verify the window updates

---

## 🔍 Quick Audit Command
To find hardcoded values in XAML files:

```powershell
Get-ChildItem "SiNetProjectManager" -Recurse -Filter "*.xaml" | 
    Select-String -Pattern "Background=|Foreground=|FontFamily=|FontSize=" | 
    Where-Object { $_ -notmatch "DynamicResource|StaticResource|Binding" }
```

---

## 📦 Supported Control Types
All implicit styles defined in `App.xaml`:

- **Containers:** Window, Grid, StackPanel, Border, GroupBox, TabControl, Expander
- **Text:** TextBlock, Label, Run
- **Input:** TextBox, ComboBox, ListBox, CheckBox, RadioButton
- **Buttons:** Button
- **Data:** DataGrid, DataGridCell, DataGridRow, DataGridColumnHeader
- **Trees:** TreeView, TreeViewItem
- **Menus:** Menu, MenuItem, ContextMenu, ToolTip
- **Other:** StatusBar, ColorPicker (Xceed)

---

## 🎯 Benefits

1. **Single Source of Truth**: All settings in `AppSettings.cs`
2. **Zero Duplication**: No repeated color/font definitions
3. **Real-Time Updates**: Changes apply instantly to all windows
4. **Accessibility**: Users can adjust fonts/colors for readability
5. **Maintainability**: Add new windows without worrying about theme consistency

---

## 🧪 Testing Theme Changes

1. Open the application
2. Go to **Settings** (from main menu)
3. Change:
   - Font Family (e.g., Arial → Calibri)
   - Font Size (e.g., 12 → 16)
   - Foreground Color (e.g., Black → Navy)
   - Background Color (e.g., White → Light Yellow)
4. **Observe:** All open windows/dialogs update **immediately**
5. Click **Save** to persist changes

---

## 🔄 Migration Guide (Existing Windows)

To update an existing window that has hardcoded values:

### Before:
```xaml
<TextBlock Text="Status" Foreground="Gray" FontSize="11"/>
<DataGrid Background="White" FontFamily="Arial">
```

### After:
```xaml
<TextBlock Text="Status" Opacity="0.7"/>
<DataGrid>
    <!-- Inherits global style automatically -->
</DataGrid>
```

---

## 📚 Key Files

| File | Purpose |
|------|---------|
| `App.xaml` | Global DynamicResource keys + implicit styles |
| `App.xaml.cs` | `ApplySettings()` updates DynamicResources |
| `AppSettings.cs` | Stores user preferences with `INotifyPropertyChanged` |
| `SettingsWindow.xaml.cs` | Triggers real-time updates via `Settings_PropertyChanged` |
| `SettingsManager.cs` | Persists settings to `settings.json` |

---

## 🎓 Advanced: Custom Styles with Theme Support

If you need a custom style that **extends** the global theme:

```xaml
<Style x:Key="MyCustomButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Padding" Value="20,10"/>
    <Setter Property="BorderThickness" Value="2"/>
    <!-- FontFamily, FontSize, Foreground, Background inherited from global style -->
</Style>
```

**Key:** Use `BasedOn="{StaticResource {x:Type Button}}"` to preserve theme inheritance.

---

## ✅ Quality Assurance

### Pre-Release Checklist:
- [ ] All windows respond to Settings changes in real-time
- [ ] No hardcoded colors found via audit command
- [ ] All custom styles use `BasedOn` to inherit global theme
- [ ] Tested with extreme settings (e.g., FontSize=24, Dark colors)
- [ ] All dialogs (StatusMapping, ProjectDecisions, etc.) use dynamic resources

---

## 🚀 Future Enhancements

- **Theme Presets**: "Light", "Dark", "High Contrast"
- **Per-Window Overrides**: Allow specific windows to opt-out
- **Color Palette Service**: Manage complementary colors (e.g., hover states)
- **User Profiles**: Save multiple theme configurations

---

## 📞 Support

If you encounter a window that doesn't respond to theme changes:
1. Check for hardcoded values in XAML (use audit command)
2. Ensure custom styles use `BasedOn="{StaticResource {x:Type ...}}"`
3. Verify the window doesn't override `FontFamily`/`Foreground` locally

---

**Last Updated:** 2026-01-28  
**Version:** 2.0 (Phase 38 - Project Decisions System)
