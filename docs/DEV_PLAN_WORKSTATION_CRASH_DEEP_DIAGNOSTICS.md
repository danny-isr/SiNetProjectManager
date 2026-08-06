# DEV plan — Deep crash diagnostics («דוח קריסות תחנה» round 2)

> **Title:** Workstation crash report — deep diagnostics (DEV-014)
> **Date:** 06.08.2026
> **Updated:** 06.08.2026
> **Status:** Approved (principles) — ready to implement on `development`
> **Scope:** Extends the shipped DEV-010 crash report with the data an external hardware/Civil 3D analyst asked for: BIOS/firmware facts, WHEA payload fields, an artifact index, per-crash human context, and a corrected incident-counting model. Local machine only. No remote inspection, no diagnosis by the app, no file copying.

Related: [`DEV_PLAN_WORKSTATION_CRASH_REPORT.md`](./DEV_PLAN_WORKSTATION_CRASH_REPORT.md) (base feature — this document extends §2, §4 and §8 of it), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md), [`SETTINGS.md`](./SETTINGS.md), [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md).

---

## 1. Why

The DEV-010 report is enough to say *«Civil 3D died N times»*. It is **not** enough to say *why*. An external analyst reviewing the current output listed the missing evidence; this round classifies each request by **whether the app can collect it locally, and at what cost**.

Two problems drive the round:

1. **Missing evidence.** No firmware identity, no raw WHEA payload, no crash dumps, no per-crash human context — so a RAM/BIOS fault and an add-in fault look identical in the report.
2. **Wrong arithmetic.** The current summary counts *event log records*, not *incidents*. One crash can produce Application Error 1000 + WER 1001, and one hard power loss produces Kernel-Power 41 + EventLog 6008. «Incidents per day» is therefore inflated and must not carry that name until records are grouped.

**Unchanged principle:** the app reports facts and flags. Interpretation stays with a human or an external AI.

---

## 1.1 Guiding constraint — keep the report small

**Decision (PROD, 06.08.2026):** this is not a research project. The goal is a short, readable report that lets a human or an AI reach a verdict — not the largest possible evidence dump. Every addition below is bounded by a hard cap, and the report must stay a **CSV + one Markdown file**.

| Guardrail | Limit |
| --- | --- |
| New machine-profile block | ~10 lines of fixed facts |
| Memory modules listed | max 8 rows (name, size, rated vs configured speed) |
| Plugins / add-ins listed | max 40, name + version only |
| Raw WHEA XML kept | **only** uncorrected events (18/19), max 5, truncated to 4 000 chars each, in an appendix |
| Artifact index | max 20 newest files — path, size, timestamp |
| Event table in Markdown | unchanged (200 rows); CSV keeps all rows |
| Files copied by the app | **none** |

If an addition cannot fit inside a cap, it does not ship. Prefer one decisive fact over ten suggestive ones.

---

## 2. Feasibility matrix

Effort scale: **S** ≈ hours · **M** ≈ 1 day · **L** ≈ multi-day · **X** = not achievable in-app.

### 2.1 BIOS and hardware identity

| Requested | Source | Effort | Notes |
| --- | --- | --- | --- |
| Motherboard manufacturer + model | WMI `Win32_BaseBoard` (`Manufacturer`, `Product`, `Version`, `SerialNumber`) | **S** | Same provider pattern as the existing GPU probe |
| BIOS version + date | WMI `Win32_BIOS` (`SMBIOSBIOSVersion`, `Manufacturer`, `ReleaseDate`) | **S** | `ReleaseDate` is CIM_DATETIME — needs conversion |
| System model | WMI `Win32_ComputerSystem` (`Manufacturer`, `Model`) | **S** | Already queried for RAM; add two properties |
| CPU microcode revision | Registry `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0` → `Update Revision`, `Previous Update Revision` (`REG_BINARY`) | **S** | Readable by standard users; needs endian-aware decode to hex |
| Memory module inventory | WMI `Win32_PhysicalMemory` (`Manufacturer`, `PartNumber`, `Capacity`, `Speed`, `ConfiguredClockSpeed`, `ConfiguredVoltage`, `BankLabel`, `DeviceLocator`) | **S** | Also gives the XMP signal below |
| **XMP / EXPO enabled** | *Inferred*: `ConfiguredClockSpeed` > JEDEC default (or > `Speed` of the SPD profile) | **M** | **Indicator only.** Report as a fact pair (rated vs configured), never as a boolean verdict |
| **Overclock / undervolt** | *Inferred*: `Win32_Processor.MaxClockSpeed` vs `CurrentClockSpeed`, plus `ProcessorPowerManagement` | **M** | Weak. Manual BIOS-set offsets are invisible to Windows |
| **Intel Default / Baseline Profile** | — | **X** | Vendor BIOS setting, no Windows API. Must be read from BIOS by a human |
| **MultiCore Enhancement (MCE)** | — | **X** | Same — BIOS-only |
| Mixed-DIMM warning | Derived from `Win32_PhysicalMemory` rows | **S** | Different `PartNumber`/`Speed` across banks is a genuine instability signal |

**Conclusion:** everything *identifiable* is cheap (**S**). Everything *BIOS-configured* (Intel Default Profile, MCE, manual OC/undervolt) is **X** — the report must instead print a short **«לבדוק ב-BIOS»** checklist with the exact setting names, so the human check is not forgotten.

### 2.2 Full WHEA data

| Requested | Source | Effort | Notes |
| --- | --- | --- | --- |
| Full XML of every Event 19 | `EventRecord.ToXml()` — **already called** in `WindowsEventLogCrashReader.ReadEventData` | **S** | The XML is parsed and discarded today; keep it for hardware events |
| `ErrorSource`, `ApicId`, `MCABank`, `Address`, `MciStat`, `ProcessorId` | Same XML — WHEA writes these as named `Data` elements | **S** | Named-element reader already exists; add a WHEA-specific extraction |
| Corrected vs uncorrected classification | WHEA event id (17 = corrected, 18/19 = fatal/uncorrected) + `ErrorType` | **S** | Currently all three collapse into `IsHardwareEvent` |
| Repeat-bank detection | Group WHEA events by `MCABank` + `ApicId` | **M** | The same bank repeating is the single strongest CPU/RAM signal |

**Conclusion:** the raw payload is already in memory and thrown away. This is the **highest value / lowest cost** item in the round.

### 2.3 Crash artifacts (dumps / CER)

| Requested | Source | Effort | Notes |
| --- | --- | --- | --- |
| Locate Autodesk CER packages (`C3Dminidump.dmp`, `dumpdata.zip`, `dmpuserinfo.xml`) | `%LOCALAPPDATA%\Autodesk\CER\**` (and `%APPDATA%\Autodesk\CER`) | **M** | User-readable. Autodesk states these are **overwritten by the next crash** — so collection must be prompt |
| Copy CER artifacts next to the report | — | **Dropped** | `dumpdata.zip` is tens of MB per crash. Conflicts with §1.1. The report **points at** the files; a human copies them if the analyst asks |
| Windows WER report metadata (`Report.wer`) | `%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive\**` (own user) · `%ProgramData%\Microsoft\Windows\WER\**` (machine) | **S** | List only. Own-user path works unelevated; the machine path is admin-only — degrade to a warning |
| Windows kernel minidumps | `%SystemRoot%\Minidump\*.dmp` | **S** to list · **L** to interpret | List name/size/time only. **Never** parse — that is WinDbg territory |
| Upload dumps anywhere | — | **X** | Out of scope |

**Conclusion:** the slice is reduced to a **read-only index** — "these files exist, at this path, from this timestamp, this size", capped at 20 rows. That is the whole value: today nobody knows the CER artifacts exist or that Autodesk overwrites them on the next crash. Copying them is a human decision, not an automatic one.

### 2.4 Per-crash context

| Requested | Source | Effort | Notes |
| --- | --- | --- | --- |
| Drawing name + where it is stored | **User input** — new fields in the existing context form | **S** | Cannot be derived; Application Error 1000 carries no document name |
| Action performed before the crash | **User input** (free text — partially exists today) | **S** | Promote to its own field so the AI can separate it from the general description |
| Does it reproduce in an empty drawing? | **User input** — tri-state (yes / no / not tested) | **S** | Cheap and decisive for triage |
| How long Civil was open before the crash | *Partially derivable*: crash time − nearest preceding process start | **M** | Requires Event 4688 (process creation) which is **off by default**. Ask the user instead |
| Add-in / plugin inventory + versions | Registry `HKLM\|HKCU\Software\Autodesk\AutoCAD\R**\**\Applications` + bundle folders `%APPDATA%\Autodesk\ApplicationPlugins`, `%PROGRAMDATA%\Autodesk\ApplicationPlugins` | **M** | Covers Transoft and in-house bundles. `PackageContents.xml` gives name + version |
| Loaded-module snapshot at crash time | — | **X** | Only the dump has it; `ModuleName` from Event 1000 is the closest available proxy and is already captured |

**Conclusion:** most of this is a **form change**, not a collection change. The plugin inventory is the one real collection item and it is well worth it — a stale Transoft build is a classic repeat-crash cause.

### 2.5 System checks

| Requested | Source | Effort | Notes |
| --- | --- | --- | --- |
| Windows Memory Diagnostic result | Event log `System`, provider `Microsoft-Windows-MemoryDiagnostics-Results`, IDs 1201/1202 | **S** | Reads an already-run test result — does not run the test. **Included** |
| Disk SMART / predicted failure | WMI `Win32_DiskDrive.Status` (+ `MSStorageDriver_FailurePredictStatus` where exposed) | **S** | One line per physical disk. Absence = "unknown", never "healthy". **Included** |
| Reliability Monitor history | WMI `Win32_ReliabilityRecords` | **M** | **Postponed** — largely duplicates the Event Log data already collected, and may need elevation. Not worth the size |
| Events ±1 h around each crash | Second Event Log pass with a widened window, unfiltered | **M** | **Postponed** — this is the single biggest size risk in the round. Revisit only if a specific incident stays unexplained |
| Temperatures | `MSAcpi_ThermalZoneTemperature` (root\wmi) | **X** in practice | Usually unimplemented on desktops and typically requires elevation. Real readings need a kernel driver (LibreHardwareMonitor) — **not** acceptable to install on pilot machines |
| Voltages | — | **X** | Same; sensor-chip access requires a signed ring-0 driver |
| CPU stress test / idle-load test | — | **X** (deliberately) | The app must not stress a production workstation. Provide a printed instruction block instead (Prime95 / OCCT / `mdsched.exe`) |
| RAM test | — | **X** to run · **S** to read | Reading the result is §2.5 row 1; running it means a reboot |

**Conclusion:** *reading existing test results* is cheap and stays; *running tests*, *sensor access*, and the wide event window are out. Temperatures and voltages must be answered by a human with HWiNFO/BIOS, and the report says so explicitly rather than leaving a silent gap.

### 2.6 Counting model (correctness fix)

| Requested | Current behavior | Effort |
| --- | --- | --- |
| Group WER records by `ReportId` | No dedup — 1000 and 1001 both counted | **M** |
| Merge Kernel-Power 41 with EventLog 6008 | Both `Critical`, both counted | **S** |
| Separate Civil crash / Civil hang / other-app crash / hardware event | Single `AppCrash` bucket, plus `Critical` | **M** |
| Stop calling duplicated records "incidents per day" | `CrashesPerDay = (appCrashes + criticals) / days` | **S** (rename) + **M** (real grouping) |

See §4 for the proposed model.

---

## 3. Proposed slices

| Slice | Content | Effort | Value |
| --- | --- | --- | --- |
| **C** | **Incident model** — group records into incidents, rename the per-day metric, split Civil crash / hang / other app / hardware | M | **Highest** (correctness) |
| **B** | **WHEA payload** — extract bank/address/status/ApicId; corrected vs uncorrected; repeat-bank flag; capped raw-XML appendix for 18/19 only | S | **Highest** value per hour |
| **A** | **Firmware & hardware identity** — BIOS, baseboard, system model, microcode, DIMM inventory (≤8), rated-vs-configured clock, mixed-DIMM flag | S | High |
| **E** | **Context form + plugins** — drawing name/location, action before crash, reproduces-in-empty-drawing, session length; plugin inventory (≤40) | S–M | High |
| **D** | **Artifact index** — list Autodesk CER, own-user WER, kernel minidumps (≤20 rows). **No copying** | S | High |
| **F** | **Light system checks** — memory-diagnostic result, disk status, and a printed «manual checks» block (BIOS settings, temperatures, stress tests) | S | Medium |

**Order:** **C → B → A → E → D → F.** Fixing the arithmetic first (**C**) prevents every later addition from inheriting inflated numbers; **B** costs almost nothing because the data is already read and discarded.

**Shipping:** incremental, two ships. **Ship 1 = C + B + A** (report correctness and hardware evidence). **Ship 2 = E + D + F** (human context and pointers). Each ship gets its own desktop version bump per [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md).

---

## 4. Incident model (slice C)

### 4.1 Definition

An **incident** is a correlated cluster of records describing one real-world failure. Records group when they share a `ReportId`, **or** fall within a short window and belong to a known pair.

| Incident kind | Members | Grouping key |
| --- | --- | --- |
| `ApplicationCrash` | Application Error 1000 + WER 1001 (+ .NET 1026) | `ReportId`, else app + ±60 s |
| `ApplicationHang` | Application Hang 1002 + WER 1001 | `ReportId`, else app + ±60 s |
| `OtherApplicationCrash` | Same, app **not** in the filter list | Same |
| `UnexpectedShutdown` | Kernel-Power 41 + EventLog 6008 (+ BugCheck 1001) | Boot session / ±5 min |
| `HardwareError` | WHEA 17/18/19, disk 7/11/153, Ntfs 55 | Provider + bank/device + ±5 min |

### 4.2 Metric naming

| Old | New | Meaning |
| --- | --- | --- |
| `CrashesPerDay` | `IncidentsPerDay` | Grouped incidents ÷ lookback days |
| — | `RecordsPerDay` | Raw record count ÷ days (kept for transparency) |
| `TotalEvents` | `TotalRecords` | Unchanged semantics, honest name |

Both numbers appear in the report so the difference between them is visible rather than hidden.

### 4.3 Compatibility

`WorkstationCrashEventDto` keeps its shape; incidents are a **new layer above it** (`CrashIncidentDto` holding member records). The CSV keeps one row per record — an analyst still wants the raw rows — plus a new `IncidentId` column. The Markdown leads with incidents and keeps the record table below.

---

## 5. Settings

**No new settings keys, and no schema change.** The caps in §1.1 are constants in the code, not configuration — a knob nobody turns is only a maintenance cost. The existing `Diagnostics.CrashReportSharePath`, `CrashAppFilters`, `CrashLookbackDays` and `CrashReportRetentionDays` keys stay exactly as they are.

Because nothing new is configurable, `SettingsView` / `SettingsViewModel` are **not touched** in this round.

---

## 6. Privacy and permissions

- Everything runs **as the signed-in user on their own machine**; no remote access, unchanged from DEV-010.
- The `Security` log is still never read.
- **New exposure to review:** BIOS/board serial numbers, DIMM part numbers, and drawing paths typed by the user. Slice D lists **paths only** — no dump content ever leaves the machine, which removes the main privacy concern of the original request.
- Probes that need elevation (machine-wide WER, Reliability Monitor, SMART on some controllers) must degrade into a `CollectionWarnings` entry, exactly like today's WMI probes — never a hard failure.

---

## 7. Out of Scope

- Parsing or symbolizing any `.dmp` file (WinDbg / Autodesk support territory).
- Installing a kernel driver or any third-party agent for sensor access.
- Running stress, RAM, or disk tests from the app.
- Reading or changing BIOS settings.
- Remote collection from other workstations.
- Uploading artifacts to Autodesk or any external service.
- An in-app «נתח ב-AI» button (still deferred from DEV-010 §8).
- **Copying any crash artifact.** The report indexes files; it never moves them.
- New configuration keys or Settings UI changes.

## 8. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Temperature and voltage readings | **Dropped** | Requires a ring-0 sensor driver; unacceptable on pilot workstations. Replaced by a printed manual-check block |
| Intel Default/Baseline Profile, MCE, manual OC/undervolt state | **Dropped (in-app)** | No Windows API exposes BIOS-configured state. Replaced by a «לבדוק ב-BIOS» checklist in the report |
| Running CPU / RAM stress tests | **Dropped** | The app must not load a production machine; only *results* of externally-run tests are read |
| Loaded-module snapshot at crash time | **Postponed** | Only obtainable from a dump. `ModuleName` from Event 1000 remains the proxy |
| Civil session length derived from Event 4688 | **Postponed** | Process-creation auditing is off by default; enabling it machine-wide is a separate ops decision. User input instead |
| Machine-wide WER (`%ProgramData%`) | **Conditional** | Listed when the user happens to be elevated; otherwise own-user WER only, with a warning |
| Copying CER / dump artifacts to the share | **Dropped** | Size. The report indexes them; a human copies on demand |
| Automatic background CER capture at app startup | **Dropped** | A background file-copy behavior is disproportionate to the problem. The report warns that Autodesk overwrites the artifacts, and that is enough to prompt a human |
| `Win32_ReliabilityRecords` | **Postponed** | Duplicates Event Log data already collected; may need elevation |
| ±1 h event window around each incident | **Postponed** | Largest size risk in the round; revisit only for a specific unexplained incident |
| New `Diagnostics.*` settings keys | **Dropped** | Caps are constants, not configuration |

## 9. Decisions taken (06.08.2026, PROD)

| Question | Decision |
| --- | --- |
| Copy `dumpdata.zip` / CER to the share? | **No.** Index only — path, size, timestamp |
| Automatic CER capture at startup? | **No.** The report prints an overwrite warning instead |
| Count crashes of other applications? | **Yes, as one aggregate line** («N crashes of other applications in the period»), not as full rows. Cheap signal, no size cost |
| One ship or incremental? | **Incremental** — Ship 1 = C+B+A, Ship 2 = E+D+F |
| Report size | **Bounded by §1.1.** An addition that cannot fit a cap does not ship |

## 10. Needs Review

- Whether Ship 2 is needed at all once Ship 1 output has been reviewed by the analyst — the corrected counts plus WHEA bank data may already settle the diagnosis.
