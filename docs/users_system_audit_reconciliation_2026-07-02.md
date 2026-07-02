# User system audit reconciliation — 2026-07-02

> **Original audit:** internal review dated 2026-07-02 (documentation-only scan; rating 8.5/10).  
> **This document:** factual corrections after scanning `src/SiNet.App.Wpf.Tests/Identity/` and the sibling **SiNetSQL** repo.

---

## Why this reconciliation exists

The original audit did **not** scan:

- `src/SiNet.App.Wpf.Tests/Identity/` (7 test files)
- `SiNetSQL/` (sibling repo — adapter, VM migrations, integration tests)

**Lesson for future audits:** scan **all** test subdirectories under `SiNet.App.Wpf.Tests/`, not only `Projects/`.

---

## Correction 1 — "No Identity tests" → 32+ test methods (GitHub repo)

| # | File | What it covers |
| --- | --- | --- |
| 1 | `ActionPermissionQueryServiceStubTests.cs` | Null `UserId` → false; delegation; `ActionPermissionCodes` |
| 2 | `AppFeatureAuthorizationTests.cs` | Feature × role, hierarchy, unknown code throws |
| 3 | `AppFeatureCodesCoverageTests.cs` | All feature codes registered; feature × role matrix |
| 4 | `AppRoleEnumParityTests.cs` | `AppRole` ↔ `AppUserRole`, `AppAccUserType` ↔ `AccUserType` |
| 5 | `AuthorizationQueryServiceStubTests.cs` | Unauthenticated deny; unknown feature throws |
| 6 | `CurrentUserProfileDisplayTests.cs` | DisplayName → LoginName → `משתמש #id` → null |
| 7 | `NewShellAuthorizationArchitectureTests.cs` | WPF ≠ SiNetSQL; no legacy leaks; factories use ports |

**Path:** `src/SiNet.App.Wpf.Tests/Identity/`

**SiNetSQL repo (sibling — required for full P4/P5 parity):**

| File | Covers |
| --- | --- |
| `UserManagementPortAdapterTests.cs` | P5 adapter + lookup methods |
| `LegacyActionPermissionQueryServiceTests.cs` | P4 host adapter |
| `ActionPermissionServiceTests.cs` | Deny-by-default, admin bypass |
| `UserServiceTests.cs` | Admin-only writes, self-protection |

**Estimated total:** ~51+ Identity-related test methods across both repos.

---

## Correction 2 — Q10 fail-closed → documented and closed

**Q10** in [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md) §10:

- **Query ports:** `false`/empty when `ICurrentUserContext.UserId == null`
- **Write services:** `RequireAdmin()` → `UnauthorizedAccessException`; no anonymous writes

**Status:** Closed (2026-07-02 reconciliation).

---

## Correction 3 — Self-protection → documented in port contract

`IUserManagementService` XML docs (`src/SiNet.Application/Identity/IUserManagementService.cs`):

- **AddUserAsync:** fail-closed via `RequireAdmin()` / `UnauthorizedAccessException`
- **UpdateUsersAsync:** admin cannot self-deactivate, self-demote, or change own `LoginName` → `InvalidOperationException`

Enforcement remains in legacy `UserService`; port documents the contract.

---

## Correction 4 — DI extension path

| Audit stated | Actual |
| --- | --- |
| `Services/Identity/IdentityServiceCollectionExtensions.cs` | `SiNetProjectManagerV2/Services/Composition/IdentityServiceCollectionExtensions.cs` |

Registered via `AddSiNetIdentityLegacyAdapters()` in `App.xaml.cs`.

---

## What remains correct from the original audit

| Item | Status |
| --- | --- |
| User filter in ProjectSelector deferred | Accurate — see `PROJECTS.md` §6, Q6 |
| No User entity in Domain | Accurate — intentional at this phase |
| `CurrentUserContext` legacy singleton behind adapters | Accurate |
| AD integration isolated in legacy | Accurate — do not refactor in identity slices |

---

## Updated rating

| Criterion | Original | Reconciled |
| --- | --- | --- |
| Ports + adapters | 8.5 | 8.5 |
| Tests | "Missing" | ✅ 32+ (GitHub) + ~20 (SiNetSQL) |
| Self-protection docs | "Not in contract" | ✅ XML on port |
| Q10 fail-closed | "Undefined" | ✅ Closed in spec |
| Enum parity safety | "None" | ✅ `AppRoleEnumParityTests` |
| **Overall** | **8.5/10** | **9.0/10** |

---

## Approval status

| Scope | Status |
| --- | --- |
| **GitHub repo** (ports, tests, docs, shell wiring) | Approved for identity slice closure |
| **P5 full slice** (adapter + VM migration) | Conditional on **SiNetSQL** push/merge: `UserManagementPortAdapter.cs`, `UserManagementViewModel.cs`, `AddUserViewModel.cs`, `UserManagementPortAdapterTests.cs` |

See also: [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md) Appendix C (test inventory).
