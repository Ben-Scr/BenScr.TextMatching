# Release 1.0.0

1. Require a clean master checkout and green Windows/Ubuntu verification for the exact source commit.
2. Freeze the verified CI `.nupkg` and `.snupkg`. Match their hashes, metadata, source commit and audited
   payloads to the release manifest. Do not rebuild or replace these files after their final audit.
3. Create `packages/BenScr.TextMatching/1.0.0` at that commit and verify the remote tag resolution.
4. Publish the exact archives to NuGet under `BenScr.TextMatching` and create the matching GitHub release.
5. Wait for NuGet processing and verify the public package using a fresh empty-cache consumer:

```powershell
pwsh -File tools/TestPackage.ps1 -Source https://api.nuget.org/v3/index.json `
    -ExpectedCommit <release-commit> -EvidencePath artifacts/public-consumer.json
```

6. Verify public signed package payloads, available matching Portable PDBs and Source Link source,
   plus the actual embedded icon on the NuGet page. Preserve these results and the CI URL with the
   exact artifacts in the release evidence.

NuGet signing changes the ZIP file, so compare public payload bytes rather than requiring an unsigned
local archive hash to equal the signed archive hash. If the version exists already, inspect its owner,
source commit and content; do not overwrite or rely on skipping duplicate pushes. Never place tokens
in source, scripts, logs or manifests. `tested_local`, `ready_to_publish` and `published_verified`
describe different states; release evidence must reflect the checks actually completed.
