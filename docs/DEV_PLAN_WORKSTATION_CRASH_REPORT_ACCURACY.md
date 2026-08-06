# DEV-015 — Crash report accuracy (Ship 1.1)

> **Title:** Workstation crash report — accuracy fixes after analyst review  
> **Date:** 06.08.2026  
> **Status:** Implemented on `development`  
> **Scope:** Report-generation correctness only (labels, WHEA corrected flag, microcode decode, WER-only incidents, minidump → Bugcheck flag). No Ship 2 (CER copy, context form, plugins). No Settings/schema.

Related: [`DEV_PLAN_WORKSTATION_CRASH_DEEP_DIAGNOSTICS.md`](./DEV_PLAN_WORKSTATION_CRASH_DEEP_DIAGNOSTICS.md), [`DEV_PLAN_WORKSTATION_CRASH_REPORT.md`](./DEV_PLAN_WORKSTATION_CRASH_REPORT.md), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md).

---

## Locked fixes (from analyst review of Ship 1 output)

1. **WHEA corrected** — classify from XML/message (`corrected` / `uncorrected` / severity fields), not `EventId == 17` alone. Appendix title and CSV `WheaCorrected` must match. Raw XML appendix only for truly uncorrected.
2. **Microcode** — decode `Update Revision` as little-endian DWORD → `0xNNN` (e.g. bytes `20 01 00 00` → `0x120`).
3. **WER-only clusters** — ReportId groups with no Application Error 1000 / Hang 1002 are **not** incidents (Supporting only).
4. **Labels** — Markdown/UI use explicit names: filtered app crash / hang vs other-app crash; separate counts.
5. **Bugcheck flag** — `HasBugCheck` is true if Event Log BugCheck **or** at least one file under `%SystemRoot%\Minidump\*.dmp` (list count only, no parse). Cap listed names at 20 in Markdown.

## Out of scope

Ship 2 items, dump parsing, BIOS reads, Settings keys, version bump (unless publish requested).
