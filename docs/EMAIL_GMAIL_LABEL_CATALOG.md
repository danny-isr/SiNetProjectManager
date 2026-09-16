# Gmail label catalog — read/write consistency

> **Title:** Gmail label catalog  
> **Date:** 16.09.2026  
> **Status:** Active  
> **Scope:** Shared in-process Gmail label map for `SiNet.App.Wpf` mailbox reads/writes.  
> **Related:** [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md)

## Existing mechanism

`GmailEmailGateway` (Singleton) cached `_cachedLabelMap` keyed by `GmailService` reference.
`GmailEmailModifyService` (Singleton) created/renamed/deleted labels via its own `Labels.List` /
`Labels.Create` and never invalidated that cache.

`ResolveLabelNames` skipped unknown `LabelId` values. After a newly created project label was
attached, a later `GetById` / mailbox page reused the stale map, so `EmailSummary.LabelNames`
omitted the project path and `EmailListRowMapper` set `IsFiledToProject = false`.

Mailbox filing SoT remains the **Gmail project label**, not SQL `ProjectId`.

## Target

One session-scoped catalog (`IGmailLabelCatalog`) shared by the gateway and modify service.

| Event | Catalog action |
| --- | --- |
| First read / empty cache | One `Labels.List`, then cache |
| Create / rename / delete by SiNet | Immediate upsert / rename / remove (no wait for TTL) |
| Unknown `LabelId` on a message | One single-flight `Labels.List`, rebuild, resolve again |
| Still unknown after refresh | Leave unresolved + Warn |
| Sign-out / new `GmailService` | Drop cache via session *generation* only — never via live auth null-checks |
| Gmail file / label write | Must not call `Logout` / delete tokens; deny on identity mismatch instead |
| Choose project / identity evaluate | Must not logout Gmail. `DisconnectGoogleOnMismatch` is false at every production call site. |

Label catalog invalidation must never dispose `GmailClientProvider`, clear credentials, or raise `AuthStateChanged(false)`.

Only an explicit user logout, account-switch, or a real authentication failure may transition the app to disconnected. File/label operations (`Labels.List`, label create, catalog invalidate/refresh, `Messages.Modify`, post-write Get, mailbox reload) must keep `IsSignedIn = true` and the same provider session.

TTL is not used as the primary consistency mechanism.

## Out of Scope

- SQL `ProjectId` as filing proof
- Per-message `Labels.List`
- Disabling mailbox refresh / keeping optimistic rows forever
- DB migrations / ThreadStatusMapping cardinality
- Extra Gmail WRITE calls

## Dropped / Cancelled / Postponed

- Optional short TTL — postponed; invalidation + unknown-id refresh is sufficient.
