# Sibling repository pins

> Status: authoritative build contract as of 2026-07-28.

## Neither solution is self-contained

Earlier documentation described `SiNet.sln` as a "self-contained CI solution". **That was wrong.**
Both solutions reach outside this repository:

| Consumer | External project reference |
| --- | --- |
| `src/SiNet.Infrastructure.Autodesk/SiNet.Infrastructure.Autodesk.csproj` | `../../../AutodeskIntegration/SiOffice.AutodeskConnector` |
| `SiNetProjectManagerV2/SiNetProjectManagerV2.csproj` | `../../SiNetSQL`, `../../AutodeskIntegration/SiOffice.GoogleConnector`, Autodesk connector |
| `src/SiNet.App.Wpf.Tests/SiNet.App.Wpf.Tests.csproj` | `SiNetProjectManagerV2` (transitively pulls all of the above) |

Because `SiNet.sln` contains both `SiNet.Infrastructure.Autodesk` and `SiNet.App.Wpf.Tests`, a clean
checkout of this repository alone **cannot** restore or build it. The sibling repositories must be
present at the exact relative paths listed below.

## The pins

Pinned commits live in [build/sibling-pins.json](../build/sibling-pins.json):

| Repository | Path relative to repo root | Branch | Pinned commit |
| --- | --- | --- | --- |
| [danny-isr/SiNetSQL](https://github.com/danny-isr/SiNetSQL) | `../SiNetSQL` | `SiWorkNet10` | `792b6ae64e65366801d5ea8f58ee0ba1a3b60a8f` |
| [danny-isr/SiOffice.AutodeskConnector](https://github.com/danny-isr/SiOffice.AutodeskConnector) | `../AutodeskIntegration/SiOffice.AutodeskConnector` | `SiWorkNet10` | `e27b99f96d6d02b9e3353ee2fdf4255f769b136f` |
| [danny-isr/SiOffice.GoogleConnector](https://github.com/danny-isr/SiOffice.GoogleConnector) | `../AutodeskIntegration/SiOffice.GoogleConnector` | `SiWorkNet10` | `c9e4d9a214a4f796b646a74d451759a0053c8a67` |

Resulting on-disk layout:

```
<parent>/
  SiNetProjectManager_GitHub/     <- this repository
  SiNetSQL/
  AutodeskIntegration/
    SiOffice.AutodeskConnector/
    SiOffice.GoogleConnector/
```

## Restoring the pinned state

```powershell
cd <path-to>\SiNetProjectManager_GitHub
pwsh .\build\fetch-siblings.ps1
```

The script clones any missing sibling, fetches, and checks out the pinned commit. It:

- fails with a non-zero exit code when a pin is missing, is not a 40-character lowercase SHA, or
  does not exist on the remote;
- **never discards local work** - if a sibling has uncommitted changes and is not already on the
  pinned commit, the script stops and asks you to resolve it;
- skips the network entirely for siblings already sitting on the pinned commit.

Validate the pins without touching the network:

```powershell
pwsh .\build\fetch-siblings.ps1 -ValidateOnly
```

### Private repositories

Authentication comes from the `SIBLING_REPOS_TOKEN` environment variable only. The token is passed
to git per invocation as an HTTP auth header and is never written to `.git/config`, a remote URL, or
any file. In CI it is supplied from `secrets.SIBLING_REPOS_TOKEN`. If the variable is unset the
script falls back to anonymous access, which only works while the repositories are public.

```powershell
$env:SIBLING_REPOS_TOKEN = '<personal-access-token>'
pwsh .\build\fetch-siblings.ps1
```

## Updating a pin

1. Push the sibling commit you want to pin.
2. Edit the `sha` (and `branch`, if it moved) in `build/sibling-pins.json`.
3. Update the table above with the same SHA.
4. Run `pwsh .\build\fetch-siblings.ps1` and then a full build to confirm the combination compiles.

Do not point a pin at an unpushed local commit - CI will fail with
`pinned commit '<sha>' does not exist on <url>`.

## The other three pins

Sibling commits are only one of four things this build pins. The rest:

| What | Where | Effect |
| --- | --- | --- |
| .NET SDK | `global.json` | `10.0.301`, `rollForward: latestFeature`, no prerelease |
| Package feeds | `NuGet.config` | `<clear />` then nuget.org only, so machine-level feeds cannot change the resolved graph |
| Package versions | `Directory.Packages.props` | Central Package Management, all versions exact, transitive pinning on |

`packages.lock.json` is deliberately **not** enabled — see the rationale in
[P2-TECH-DEBT-BACKLOG.md](./P2-TECH-DEBT-BACKLOG.md) under "Build determinism".

## Why not submodules or internal packages

Considered and explicitly rejected for this round (decision 2026-07-28): submodules would change the
local workflow for every developer, and internal NuGet packaging requires feed infrastructure that
does not exist yet. Both remain open options and are tracked in `docs/P2-TECH-DEBT-BACKLOG.md`.
