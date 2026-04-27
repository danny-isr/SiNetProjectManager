# 05-run-tests: Validate with test suites

Run the two test projects (`SiNetSQL.Tests`, `SiNetSQL.E2ETests`) on the upgraded solution. Address any test failures introduced by the upgrade (typically driven by behavioral changes in EF Core, `System.Text.Json`, or SqlClient).

**Done when**: All tests pass on the upgraded solution; any new failures are either fixed or documented with rationale.
