# 02-update-tfms: Update target frameworks for all projects

Update the `<TargetFramework>` element in every project file from `net8.0` / `net8.0-windows` to `net10.0` / `net10.0-windows` (preserving the platform suffix). This is a uniform bump — no project requires multi-targeting.

Affects all 10 `.csproj` files across the 4 repositories.

**Done when**: Every project file declares `net10.0` or `net10.0-windows`; `dotnet restore` succeeds for the full solution.
