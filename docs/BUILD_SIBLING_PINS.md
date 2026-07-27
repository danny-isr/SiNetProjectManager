# Hybrid solution sibling pins

`SiNetProjectManager.sln` is a hybrid legacy/new-stack solution. It depends on sibling repositories
outside this repository:

- `..\SiNetSQL`
- `..\AutodeskIntegration`

Build and release users must check out the commits pinned by the consuming branch before building the
hybrid solution. The self-contained `SiNet.sln` remains the CI solution and does not require these
sibling repositories.
