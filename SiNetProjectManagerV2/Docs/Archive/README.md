# Archive

- **Created:** 26.05.2026
- **Status:** Historical material only — **not** an active source of truth.

## Purpose
This folder holds historical material: drafts, fix notes, phase notes, old specs, temporary test guides, and superseded documents.

## Rules
1. **Archive is not authoritative.** The active sources of truth live under `Docs\Domains\` and `Docs\Decisions\`.
2. **Conflict rule:** if an Archive document contradicts a `Domains` or `Decisions` document, the `Domains` / `Decisions` document wins.
3. **No deletion without explicit decision.** Nothing is removed from the archive without a clear approval to do so.
4. **No silent revival.** Mechanisms or designs described here that are marked dropped / cancelled / postponed in the active Principles documents must not be silently revived from the archive.
5. **History preservation.** Files moved into the archive are moved with `git mv` so history is preserved.

## Contents
- `work\` — old work / spec / planning documents previously under `Docs\work`.
- UI fix notes — historical fixes (background, colors, row coloring, multi-line tests).
- Phase notes — testing guides and phase appendices that are no longer current.
- Old specs and planning PDFs / text files.

## NeedsReview
Documents whose status is unclear may be tagged `NeedsReview` in the archive index in a future round, rather than being moved silently. For Round B, large architecture overviews are left in place pending an explicit decision.
