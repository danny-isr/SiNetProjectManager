# AI Development Guide — SiNet (new architecture)

> Audience: AI assistants and developers working inside `SiNet.sln`.
> Read together with [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md) and
> [`MIGRATION_MAP.md`](./MIGRATION_MAP.md).

The goal of the new structure is to make the codebase **predictable for AI-assisted work**:
small files, clear layers, explicit interfaces, and modular wiring.

---

## 1. Where does code go?

| You are adding… | Put it in… |
| --- | --- |
| An entity, value object, enum, or pure domain rule | `SiNet.Domain` |
| An interface/port, application service, use-case, or DTO | `SiNet.Application` |
| A Google/Gmail integration | `SiNet.Infrastructure.Google` |
| An Autodesk/ACC integration | `SiNet.Infrastructure.Autodesk` |
| EF Core / SQL persistence | `SiNet.Infrastructure.Sql` |
| File IO / storage | `SiNet.Infrastructure.FileSystem` |
| Logging implementation | `SiNet.Infrastructure.Logging` |
| An adapter that calls **existing legacy** code behind a new port | `SiNet.LegacyBridge` |
| DI registration for a module | that module's `AddSiNet*` extension |
| The aggregate DI wiring | `SiNet.App.Composition` |
| A view, view-model, converter, or window | `SiNet.App.Wpf` (UI only) |

---

## 2. Hard rules (do not break)

1. **No file above ~600 lines.** Split by responsibility.
2. **No WPF types outside `SiNet.App.Wpf`.** Connectors/infrastructure expose primitives
   (e.g. a hex color `string`), never `Brush`/`DependencyObject`.
3. **No `DbContext` in the UI.** Go through Application ports; `Infrastructure.Sql` uses
   `IDbContextFactory<>`.
4. **External systems only behind interfaces.** Google, Autodesk, SQL, FileSystem are reached
   through ports in `SiNet.Application`.
5. **No business logic in code-behind** or oversized view-models.
6. **Register with modular `AddSiNet*(this IServiceCollection)` extensions.** Never grow a
   monolithic `ConfigureServices`.
7. **Respect dependency direction** (see `ARCHITECTURE_TARGET.md` §3). Do not add disallowed
   project references.
8. **Async everywhere for IO**; propagate `CancellationToken`; no sync-over-async
   (`.GetAwaiter().GetResult()`).
9. **No MediatR, no full Repository pattern** unless explicitly requested.
10. **Never hand-edit EF migrations, `*.Designer.cs`, or `ModelSnapshot`.** Change
	model/configuration only and report when `Add-Migration`/`Remove-Migration` is needed.

---

## 3. Strangler workflow (per domain)

1. Define or confirm the ports in `SiNet.Application`.
2. Implement the port in the right `Infrastructure.*` project, **or** add a
   `SiNet.LegacyBridge` adapter that delegates to the existing legacy service.
3. Register it through the module's `AddSiNet*` extension.
4. Point new consumers at the port (not the legacy class).
5. `dotnet build SiNet.sln`; run and extend tests.
6. Update `MIGRATION_MAP.md`; retire the old path only when its status is **✅ Replaced**.

---

## 4. Build & verify

- Build the new solution: `dotnet build SiNet.sln`. Keep it green before finishing a task.
- The legacy solution `SiNetProjectManager.sln` stays buildable and is the **functional
  reference**; do not modify it during migration unless wiring in a replaced port.

---

## 5. Reference & recovery

- **Functional reference:** `SiNetProjectManager.sln`.
- **Old code recovery:** the frozen branch `Before_refactoring` (never modify it).
