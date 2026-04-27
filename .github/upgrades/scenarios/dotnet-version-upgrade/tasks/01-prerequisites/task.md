# 01-prerequisites: Verify SDK and tooling

Confirm .NET 10 SDK is installed and accessible to the build, and that any `global.json` files in the four repositories either don't pin an older SDK or are updated to allow .NET 10. Verify Visual Studio version supports .NET 10.

**Done when**: `dotnet --list-sdks` shows a 10.x SDK; no `global.json` blocks .NET 10; solution opens without SDK warnings.
