# System Health Google Diagnostics Integration

## 1. Purpose
This document specifies how to integrate the existing Google Drive diagnostics into the existing System Health mechanism. It explicitly documents that the integration reuses existing UI and health aggregation patterns without introducing new UI frameworks. It does not design a new mechanism.

## 2. Existing Mechanism
The system already contains a mechanism for monitoring health:
- **SystemHealthWindow**: `SiNetProjectManagerV2/Views/SystemHealthWindow.xaml.cs`
- **IServiceHealthCheck**: `SiNetSQL/Services/Health/IServiceHealthCheck.cs`
- **HealthRow**: Declared inside `SystemHealthWindow.xaml.cs`
- **Manual Refresh button**: Exists inside `SystemHealthWindow.xaml.cs` (`RefreshButton_Click`)
- **Severity/Color system**: `ServiceHealthState` enum (`SiNetSQL/Services/Health/ServiceHealthState.cs`) mapped to Unicode glyphs (🟢, 🟡, 🔴) in `HealthRow.GlyphFor()`.

**How it works**:
- Health checks return a `ServiceHealthStatus` object.
- The UI listens to an `ISystemHealthService` event to construct and update `HealthRow` items.
- Colors are determined by mapping the backend `ServiceHealthState` to UI glyphs.
- Refresh operates manually by calling `ISystemHealthService.RefreshAllAsync()`, which executes all registered checks.

## 3. Design Decision
- Do not create a new Health Panel.
- Do not create a new Flyout.
- Do not create a new Side Drawer.
- Do not create a new Notification Framework.
- **Integrate into the existing mechanism only.**

## 4. Google Diagnostics Sources
The source for Google diagnostics is the previously created service:
`GoogleDriveFolderDiagnosticService`

It supports the following diagnostic statuses:
- OK
- NotConfigured
- GoogleNotConfigured
- NotAuthenticated
- NoAccess
- NotFound
- InvalidType
- EmptyFolder
- AccessibleReadOnlyOrUnknownWritePermission
- Error

## 5. Planned Health Check Rows
Future rows to be added to System Health:
1. **Google configuration / OAuth readiness**
   - Source: `GoogleAuthService` checks
   - Key: N/A
   - Possible statuses: OK, GoogleNotConfigured
   - Severity mapping: OK -> Online, GoogleNotConfigured -> Warning
   - User-facing message: “חיבור Google לא מוגדר בתחנה זו. יש לפנות למנהל מערכת.”
   - Technical details: Postponed
2. **Google connected account**
   - Source: `GoogleAuthService`
   - Key: N/A
   - Possible statuses: OK, NotAuthenticated
   - Severity mapping: OK -> Online, NotAuthenticated -> RequiresAuthorization (Warning)
   - User-facing message: “יש להתחבר לחשבון Google כדי להשתמש בשירותי Google באפליקציה.”
   - Technical details: Postponed
3. **Google Drive inspection templates folder**
   - Source: `GoogleDriveFolderDiagnosticService`
   - Key: `InspectionTemplatesFolderId`
   - Possible statuses: OK, NoAccess, NotFound, EmptyFolder
   - Severity mapping: OK -> Online, NoAccess -> Warning, NotFound -> Warning, EmptyFolder -> Warning
   - User-facing messages:
     - NoAccess: “תיקיית תבניות הביקורת מוגדרת, אך לחשבון Google המחובר אין הרשאה לגשת אליה.”
     - NotFound: “תיקיית תבניות הביקורת לא נמצאה או אינה גלויה לחשבון Google המחובר.”
     - EmptyFolder: “תיקיית תבניות הביקורת נגישה, אך לא נמצאו בה קבצי Google Sheets.”
   - Technical details: Postponed
4. **Google Drive inspection reports folder**
   - Source: `GoogleDriveFolderDiagnosticService`
   - Key: `InspectionReportsFolderId`
   - Possible statuses: AccessibleReadOnlyOrUnknownWritePermission, NoAccess, NotFound
   - Severity mapping: AccessibleReadOnlyOrUnknownWritePermission -> Warning, NoAccess -> Warning, NotFound -> Warning
   - User-facing message: “תיקיית הדוחות נגישה, אך הרשאת כתיבה לא נבדקה בסבב זה.”
   - Technical details: Postponed

## 6. Severity Mapping
Approved mapping from `DiagnosticStatus` to `ServiceHealthState`:
- OK → Online / Green
- AccessibleReadOnlyOrUnknownWritePermission → Warning
- NotConfigured → Warning
- GoogleNotConfigured → Warning
- NotAuthenticated → RequiresAuthorization (Warning)
- NoAccess → Warning
- NotFound → Warning
- InvalidType → Offline (Error)
- EmptyFolder → Warning
- Error → Offline (Error)

*(Existing severity names used: Online, Warning, Offline, RequiresAuthorization, NotConfigured, Checking, Unknown)*

## 7. User-Facing Messages
- **Google configuration missing**: “חיבור Google לא מוגדר בתחנה זו. יש לפנות למנהל מערכת.”
- **Google account not authenticated**: “יש להתחבר לחשבון Google כדי להשתמש בשירותי Google באפליקציה.”
- **Templates folder no access**: “תיקיית תבניות הביקורת מוגדרת, אך לחשבון Google המחובר אין הרשאה לגשת אליה.”
- **Templates folder not found**: “תיקיית תבניות הביקורת לא נמצאה או אינה גלויה לחשבון Google המחובר.”
- **Templates folder empty**: “תיקיית תבניות הביקורת נגישה, אך לא נמצאו בה קבצי Google Sheets.”
- **Reports folder accessible but write not checked**: “תיקיית הדוחות נגישה, אך הרשאת כתיבה לא נבדקה בסבב זה.”

## 8. Admin vs User
- In the current round, we do not add separate hiding/filtering for Admin vs. User.
- Everyone sees friendly messages.
- Technical details are not displayed as `HealthRow` does not natively support them yet.
- Admin-only technical details view is postponed to a future round.

## 9. Refresh Behavior
- Use the existing manual Refresh button in `SystemHealthWindow`.
- Do not add background polling.
- Do not run Google API checks in a loop.
- Do not create a new cache if no existing mechanism is present.

## 10. Out of Scope
- New SystemHealthWindow
- Flyout
- Side Drawer
- New Status Indicator
- New Notification framework
- ACC Health Checks
- AI Health Checks
- Logging Health Checks
- DB schema changes
- Migration
- Fallback logic
- Per-user settings
- Google auth model changes
- Google Drive write test for reports folder

## 11. Future Implementation Checklist
- [ ] Create/extend `IServiceHealthCheck` implementation for Google config
- [ ] Create/extend `IServiceHealthCheck` implementation for Google account
- [ ] Create/extend `IServiceHealthCheck` implementation for templates folder
- [ ] Create/extend `IServiceHealthCheck` implementation for reports folder
- [ ] Register checks in existing DI / health registry
- [ ] Verify rows appear in `SystemHealthWindow`
- [ ] Verify Refresh runs checks
- [ ] Verify no XAML changes required
- [ ] Build with Visual Studio / MSBuild full

## 12. Dropped / Cancelled / Postponed

**דברים שירדו / בוטלו / הושהו**:
- עיצוב Health Panel חדש — בוטל.
- Flyout / Side Drawer / Status Indicator חדש — בוטל.
- שינוי XAML להצגת technical details — מושהה.
- Admin/User filtering — מושהה.
- Background polling — לא מאושר.
- ACC / AI / Logging checks — מושהים.
- Google Drive write test — מושהה.
- DB schema / Migration / fallback / per-user settings — לא מאושר.