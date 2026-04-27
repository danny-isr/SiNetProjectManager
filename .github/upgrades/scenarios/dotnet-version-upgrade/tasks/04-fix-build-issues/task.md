# 04-fix-build-issues: Resolve source-incompatibility and behavioral issues

Address the 43 source-incompatible (`Api.0002`) and 74 behavioral-change (`Api.0003`) issues identified by the assessment. Most are concentrated in `SiNetSQL`, `MasterPlan.SyncEngine`, `SiOffice.GoogleConnector`, and `SiOffice.AutodeskConnector`. Build the full solution and fix every compilation error in a single bounded pass; investigate each behavioral warning surfaced at runtime if encountered during smoke tests.

The 2,704 binary-incompatibility issues (`Api.0001`) typically resolve automatically on recompile and require no source changes — only act on those that surface as actual compiler errors.

**Done when**: `dotnet build SiNetProjectManager.sln` succeeds with 0 errors across all 10 projects; warnings related to deprecated APIs are reviewed (suppress only with justification).
