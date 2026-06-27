# SiNet Target Architecture

> **Status:** Draft — Foundation Round (2026-06-27)
> **Working branch:** `SiWorkNet10`
> **Frozen reference (do not modify):** `Before_refactoring`
> **New solution:** `SiNet.sln` · **Legacy/functional reference:** `SiNetProjectManager.sln`

This document describes the **target** clean architecture for the SiNet ecosystem and the
rules every new project/file must follow. It is the source of truth for the refactoring;
when code and this document disagree, fix the document first, then the code.

---

## 1. Purpose

The SiNet app is **pre-production**: no live users, no backward-compatibility constraint.
We are restructuring aggressively using a **parallel / strangler** approach:

- The existing solution (`SiNetProjectManager.sln`) stays **working and untouched** as the
  functional reference.
- New, clean code lives in `SiNet.sln` and grows **beside** the old code.
- Functionality is moved **one domain at a time** behind interfaces.
- Old code is deleted **only after** the new path compiles, the UI flow works, tests exist,
  and `MIGRATION_MAP.md` marks the old path as replaced.

---

## 2. Solution layout (`SiNet.sln`)

| Project | TFM | Depends on | Responsibility |
| --- | --- | --- | --- |
| `SiNet.Domain` | `net10.0` | — | Entities, value objects, enums, pure domain rules. No framework dependencies. |
| `SiNet.Application` | `net10.0` | Domain | **Ports** (interfaces), application services/use-cases, DTOs. No infrastructure. |
| `SiNet.Infrastructure.Sql` | `net10.0` | Application, Domain | EF Core persistence through `IDbContextFactory<>` over the existing SiNetSQL context (wired in a later round). |
| `SiNet.Infrastructure.Google` | `net10.0` | Application, Domain | Gmail / Google integration. **No WPF.** |
| `SiNet.Infrastructure.Autodesk` | `net10.0` | Application, Domain | ACC / BIM 360 integration. **No WPF.** |
| `SiNet.Infrastructure.FileSystem` | `net10.0` | Application, Domain | File storage and IO. |
| `SiNet.Infrastructure.Logging` | `net10.0` | Application, Domain | Logging implementation (Serilog adapter in a later round). |
| `SiNet.LegacyBridge` | `net10.0` | Application, Domain | Adapters that implement new ports by **delegating to legacy code** during migration. |
| `SiNet.App.Composition` | `net10.0` | Application, Domain, all Infrastructure.*, LegacyBridge | DI **composition root**; aggregates the modular `AddSiNet*` extensions. |
| `SiNet.App.Wpf` | `net10.0-windows` | App.Composition (+ Application, Domain) | WPF host/shell: views & view-models only. **The only project allowed to use WPF types.** |

---

## 3. Dependency rules

Allowed reference direction (left depends on nothing; arrows point to dependencies):

```
Domain  ◄─ Application  ◄─ Infrastructure.* / LegacyBridge  ◄─ App.Composition  ◄─ App.Wpf
```

- `Domain` depends on **nothing**.
- `Application` depends on **Domain only**.
- `Infrastructure.*` and `LegacyBridge` depend on **Application + Domain only**.
- `App.Composition` depends on Application, Domain, every `Infrastructure.*`, and `LegacyBridge`.
- `App.Wpf` depends on `App.Composition` (+ Application/Domain). It is the **only** project that
  may reference WPF/UI types.
- `Infrastructure.*` projects must **not** reference each other.
- Nothing may reference `App.Wpf`.

---

## 4. Hard conventions

1. **File size:** no new file above **~600 lines**. If it grows, split by responsibility.
2. **No WPF outside `App.Wpf`:** connectors/infrastructure return primitives (e.g. a hex
   `string` color), never `Brush`/`DependencyObject`/etc.
3. **No `DbContext` in UI:** the UI talks to Application ports. `Infrastructure.Sql` uses
   `IDbContextFactory<>`.
4. **Everything external behind an interface:** Google, Autodesk, SQL, and FileSystem are
   reached only through ports defined in `SiNet.Application`.
5. **Modular DI:** each module exposes `AddSiNet…(this IServiceCollection)`; the composition
   root aggregates them. No monolithic `ConfigureServices`.
6. **Async & cancellation:** all IO is async; propagate `CancellationToken`; no
   sync-over-async (`.GetAwaiter().GetResult()`).
7. **No business logic in code-behind** or oversized view-models.
8. **No MediatR and no full Repository pattern** unless explicitly requested.
9. **EF migrations:** never hand-edit migrations, `*.Designer.cs`, or `ModelSnapshot`. Change
   model/configuration only and report when `Add-Migration`/`Remove-Migration` is required.

---

## 5. Strangler workflow (per domain)

1. Define/confirm the ports in `SiNet.Application`.
2. Implement them in the right `Infrastructure.*` **or** add a `SiNet.LegacyBridge` adapter that
   delegates to the existing service.
3. Register via the module's `AddSiNet*` extension.
4. Point new consumers at the port.
5. `dotnet build SiNet.sln`; run and extend tests.
6. Update `MIGRATION_MAP.md`; retire the old path only when its status is **✅ Replaced**.

Migration order: **Email/Google → Workflow → Inspection → ACC/Autodesk → SQL/DbContext →
App startup/DI**.

---

## 6. Build & guardrails

- Verify the new solution with `dotnet build SiNet.sln`.
- During the Foundation Round do **not** modify: `SiNetProjectManager.sln`, legacy projects,
  EF migrations, `DbContext`, `ModelSnapshot`, `*.Designer.cs`, or existing WPF screens.
- Recover any old code from the frozen `Before_refactoring` branch.
