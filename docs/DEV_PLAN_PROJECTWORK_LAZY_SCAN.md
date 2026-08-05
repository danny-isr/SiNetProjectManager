# DEV-013 — ProjectWork lazy scan, unload on collapse, parallel IO

> **Title:** Lazy expand / unload collapse / presence probe / scoped rescan / DOP-4 parallel IO  
> **Date:** 05.08.2026  
> **Status:** Implemented on `development` — awaiting PROD publish + verify  
> **Scope:** `SiNet.App.Wpf` ProjectWork tree performance for large projects (~20k+ files)  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`DEV_PLAN_PROJECTWORK_DISK_FOLDERS.md`](./DEV_PLAN_PROJECTWORK_DISK_FOLDERS.md) (DEV-012 colors/filing unchanged)

---

## Locked behavior

1. **Lazy on expand** — scan files and discover one child-folder level only when a folder is expanded.
2. **Unload on collapse** — remove File/Alt/Version nodes; keep folder skeleton + probe flags; re-scan on next expand.
3. **Presence probe** — cheap check for physical files (for gray/purple/green) without building version nodes.
4. **Parallel IO** — `SemaphoreSlim(4)`; enumerate off UI; integrate/unload on UI only.
5. **Watcher** — watch project root path(s) only; rescan only expanded folders (probe the rest).

No EF/schema changes. DEV-012 rules unchanged.
